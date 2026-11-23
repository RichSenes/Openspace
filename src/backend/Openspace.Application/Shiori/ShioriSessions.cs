using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Application.Functions;

namespace Openspace.Application.Shiori;

/// <summary>
/// Shiori's sessions: opening one, talking in it, switching its mode, changing its whiteboard and
/// ending it. A message returns at once; the answer is written in the background, streamed to
/// whoever watches, then saved with the session and copied into its note. One answer at a time per
/// session, and every change to a session goes through one lock, so a whiteboard change made by a
/// tool while an answer is written never loses the answer, nor the other way round.
/// </summary>
public sealed partial class ShioriSessions(
    IShioriSessionStore store,
    IShioriRuntime runtime,
    IVault vault,
    IShioriPublisher publisher,
    IOptionsMonitor<FunctionModelOptions> models,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ShioriSessions> logger) : IDisposable
{
    public const int MaxMessage = 20_000;

    public const int MaxTitle = 120;

    public const string BusyCode = "shiori-busy";

    public const string EndedCode = "shiori-ended";

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, Task> _answering = new();

    // Whether the last message of a session was said aloud: its answer is then a spoken one.
    private readonly ConcurrentDictionary<Guid, bool> _spoken = new();

    public void Dispose()
    {
        foreach (var gate in _locks.Values)
        {
            gate.Dispose();
        }
    }

    /// <param name="person">How the person is named in the note's transcript.</param>
    public async Task<ShioriSessionView> CreateAsync(string? title, ShioriMode mode, string person, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var name = string.IsNullOrWhiteSpace(title) ? $"Session of {Local(now):yyyy-MM-dd HH:mm}" : title.ReplaceLineEndings(" ").Trim();
        name = name.Length <= MaxTitle ? name : name[..MaxTitle];

        // Two sessions on the same day and subject still get a note each.
        var stem = SessionNote.PathFor(Local(now), name)[..^3];
        var path = $"{stem}.md";
        for (var n = 2; await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not null; n++)
        {
            path = $"{stem}-{n}.md";
        }

        var session = new ShioriSession
        {
            Id = Guid.CreateVersion7(),
            Title = name,
            Person = string.IsNullOrWhiteSpace(person) ? "You" : person.Trim(),
            Mode = mode,
            RuntimeSessionId = Guid.NewGuid().ToString("D"),
            Token = NewToken(),
            NotePath = path,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.AddAsync(session, cancellationToken).ConfigureAwait(false);
        await WriteNoteAsync(session, "opened", cancellationToken).ConfigureAwait(false);
        return View(session);
    }

    public async Task<IReadOnlyList<ShioriSessionSummary>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Select(s => new ShioriSessionSummary(
            s.Id, s.Title, s.Mode, s.NotePath, s.Transcript.Count(e => e.Role is TranscriptRole.Person or TranscriptRole.Shiori),
            s.CreatedAt, s.UpdatedAt, s.EndedAt, IsAnswering(s.Id)))];

    public async Task<ShioriSessionView> GetAsync(Guid id, CancellationToken cancellationToken) =>
        View(await FindAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Records the person's message and starts the answer; returns the message as recorded.</summary>
    public Task<TranscriptEntry> SendAsync(Guid id, string text, CancellationToken cancellationToken) =>
        SendAsync(id, text, spoken: false, cancellationToken);

    /// <summary>As <see cref="SendAsync(Guid, string, CancellationToken)"/>; a <paramref name="spoken"/> message gets a short, spoken answer.</summary>
    public async Task<TranscriptEntry> SendAsync(Guid id, string text, bool spoken, CancellationToken cancellationToken)
    {
        var message = text?.Trim();
        if (string.IsNullOrEmpty(message))
        {
            throw new RejectedException("empty-text", "Write something to Shiori first.");
        }

        if (message.Length > MaxMessage)
        {
            throw new RejectedException("text-too-long", $"A message holds at most {MaxMessage} characters.");
        }

        TranscriptEntry entry = null!;
        var session = await ChangeAsync(id, s =>
        {
            Open(s);
            if (IsAnswering(s.Id))
            {
                throw new RejectedException(BusyCode, "Shiori is still answering: wait for the answer, or end the session.");
            }

            entry = new TranscriptEntry(TranscriptRole.Person, message, s.Mode, clock.GetUtcNow());
            _spoken[s.Id] = spoken;
            s.Transcript.Add(entry);

            // Marked before the lock opens, so a second message sent at once is refused.
            _answering[s.Id] = Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

        await PublishAsync(p => p.MessageAsync(id, entry, CancellationToken.None)).ConfigureAwait(false);
        await PublishAsync(p => p.StateAsync(new ShioriState(id, session.Mode, true, false), CancellationToken.None)).ConfigureAwait(false);
        // The run waits until it is on record, so its own end can never come before it is listed.
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _answering[id] = Task.Run(
            async () =>
            {
                await listed.Task.ConfigureAwait(false);
                await AnswerAsync(id).ConfigureAwait(false);
            },
            CancellationToken.None);
        listed.SetResult();
        return entry;
    }

    /// <summary>The answer being written in a session, for tests and a clean stop; done when there is none.</summary>
    public Task Completion(Guid id) => _answering.TryGetValue(id, out var running) ? running : Task.CompletedTask;

    public async Task<ShioriSessionView> SetModeAsync(Guid id, ShioriMode mode, CancellationToken cancellationToken)
    {
        var changed = false;
        var session = await ChangeAsync(id, s =>
        {
            Open(s);
            if (s.Mode == mode)
            {
                return;
            }

            s.Mode = mode;
            s.Transcript.Add(new TranscriptEntry(TranscriptRole.Mode, $"Mode: {ShioriPrompt.Name(mode)}", mode, clock.GetUtcNow()));
            changed = true;
        }, cancellationToken).ConfigureAwait(false);

        if (changed)
        {
            await WriteNoteAsync(session, $"switched to {ShioriPrompt.Name(mode)}", cancellationToken).ConfigureAwait(false);
            await PublishAsync(p => p.MessageAsync(id, session.Transcript[^1], CancellationToken.None)).ConfigureAwait(false);
            await PublishAsync(p => p.StateAsync(State(session), CancellationToken.None)).ConfigureAwait(false);
        }

        return View(session);
    }

    /// <summary>
    /// Ends a session: the decisions of the whiteboard become
    /// proposals (the acceptor and the person take them from there), an answer still being written
    /// stops and the note gets its last copy.
    /// Ending an ended session changes nothing.
    /// </summary>
    public async Task<ShioriEnding> EndAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (current.Ended)
        {
            return new ShioriEnding(View(current), []);
        }

        var proposed = new List<string>();
        var session = await ChangeAsync(id, async s =>
        {
            if (s.Ended)
            {
                return;
            }

            for (var i = 0; i < s.Board.Count; i++)
            {
                var item = s.Board[i];
                if (item.Kind != WhiteboardKind.Decision || item.Note is not null)
                {
                    continue;
                }

                var draft = new ShioriProposals.Draft(
                    "decision", item.Text, item.Text,
                    "Taken during the session, as its whiteboard shows; the transcript has the discussion.",
                    item.Office is null ? [] : [item.Office], [], []);
                var path = await ShioriProposals.WriteAsync(vault, s, draft, Local(clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
                s.Board[i] = item with { Note = path };
                proposed.Add(path);
            }

            s.EndedAt = clock.GetUtcNow();
            s.Transcript.Add(new TranscriptEntry(TranscriptRole.Ended, "Session ended", s.Mode, s.EndedAt.Value));
        }, cancellationToken).ConfigureAwait(false);

        // Stopped once the end is saved: whatever the answer still brings finds the session ended and is dropped.
        if (IsAnswering(id))
        {
            await runtime.StopAsync(session.RuntimeSessionId).ConfigureAwait(false);
        }

        await WriteNoteAsync(session, "ended", cancellationToken).ConfigureAwait(false);
        await PublishAsync(p => p.BoardAsync(id, [.. session.Board], CancellationToken.None)).ConfigureAwait(false);
        await PublishAsync(p => p.StateAsync(State(session), CancellationToken.None)).ConfigureAwait(false);
        return new ShioriEnding(View(session), proposed);
    }

    /// <summary>The session a tool call names with its secret; refused once it has ended.</summary>
    public async Task<ShioriSession> ByTokenAsync(string? token, CancellationToken cancellationToken)
    {
        var session = string.IsNullOrWhiteSpace(token) ? null : await store.FindByTokenAsync(token.Trim(), cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new RejectedException("shiori-unknown", "Openspace does not know this Shiori session.");
        }

        Open(session);
        return session;
    }

    /// <summary>Changes the whiteboard and shows it to whoever watches; the note follows with the next answer.</summary>
    public async Task<WhiteboardItem> ChangeBoardAsync(Guid id, WhiteboardChange change, CancellationToken cancellationToken)
    {
        WhiteboardItem item = null!;
        var session = await ChangeAsync(id, s =>
        {
            Open(s);
            item = Whiteboard.Apply(s, change);
        }, cancellationToken).ConfigureAwait(false);
        await PublishAsync(p => p.BoardAsync(id, [.. session.Board], CancellationToken.None)).ConfigureAwait(false);
        return item;
    }

    /// <summary>Links a whiteboard item to the proposal made from it, so ending the session does not propose it twice.</summary>
    public async Task LinkProposalAsync(Guid id, string item, string notePath, CancellationToken cancellationToken)
    {
        var session = await ChangeAsync(id, s =>
        {
            var at = s.Board.FindIndex(i => string.Equals(i.Id, item.Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                throw new RejectedException("whiteboard-item", $"There is no item \"{item}\" on the whiteboard.");
            }

            s.Board[at] = s.Board[at] with { Note = notePath };
        }, cancellationToken).ConfigureAwait(false);
        await PublishAsync(p => p.BoardAsync(id, [.. session.Board], CancellationToken.None)).ConfigureAwait(false);
    }

    private async Task AnswerAsync(Guid id)
    {
        var buffer = new StringBuilder();
        string? error = null;
        ShioriSession? session = null;
        try
        {
            session = await FindAsync(id, CancellationToken.None).ConfigureAwait(false);
            var turn = await TurnAsync(session).ConfigureAwait(false);
            string answer;
            try
            {
                answer = await runtime.AnswerAsync(turn, async chunk =>
                {
                    buffer.Append(chunk);
                    await PublishAsync(p => p.TextAsync(id, chunk, CancellationToken.None)).ConfigureAwait(false);
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RejectedException or InvalidOperationException or IOException or TimeoutException or HttpRequestException)
            {
                error = ex.Message;
                answer = buffer.ToString();
            }

            session = await ChangeAsync(id, s =>
            {
                // Ended meanwhile: what was being written was stopped on purpose.
                if (s.Ended)
                {
                    return;
                }

                var text = (answer.Length > 0 ? answer : buffer.ToString()).Trim();
                if (text.Length > 0)
                {
                    s.Transcript.Add(new TranscriptEntry(TranscriptRole.Shiori, text, turn.Mode, clock.GetUtcNow()));
                }

                if (error is not null)
                {
                    s.Transcript.Add(new TranscriptEntry(TranscriptRole.Failed, error, turn.Mode, clock.GetUtcNow()));
                }

                if (error is null || text.Length > 0)
                {
                    s.RuntimeStarted = true;
                }
                else if (!s.RuntimeStarted)
                {
                    // The engine may have kept half a conversation under that id: the next try starts a clean one.
                    s.RuntimeSessionId = Guid.NewGuid().ToString("D");
                }
            }, CancellationToken.None).ConfigureAwait(false);

            if (!session.Ended)
            {
                await WriteNoteAsync(session, "answered", CancellationToken.None).ConfigureAwait(false);
                foreach (var entry in session.Transcript.TakeLast(error is null ? 1 : 2).Where(e => e.Role is TranscriptRole.Shiori or TranscriptRole.Failed))
                {
                    await PublishAsync(p => p.MessageAsync(id, entry, CancellationToken.None)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // In the background nobody would see it: the page hears it through the state.
            LogAnswerFailed(logger, ex, id);
            error ??= ex.Message;
        }
        finally
        {
            _answering.TryRemove(id, out _);
            if (session is not null)
            {
                var state = State(session) with { Answering = false, Error = error };
                await PublishAsync(p => p.StateAsync(state, CancellationToken.None)).ConfigureAwait(false);
            }
        }
    }

    private async Task<ShioriTurn> TurnAsync(ShioriSession session)
    {
        var constitution = (await vault.ReadAsync("Constitution.md", CancellationToken.None).ConfigureAwait(false))?.Body;
        IReadOnlyList<Domain.Offices.Office> offices;
        await using (var scope = scopes.CreateAsyncScope())
        {
            offices = await scope.ServiceProvider.GetRequiredService<IOfficeRepository>().ListAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var model = models.CurrentValue.Shiori;
        var person = session.Transcript.Last(e => e.Role == TranscriptRole.Person);
        return new ShioriTurn(
            session.Id, session.RuntimeSessionId, session.RuntimeStarted, session.Mode, model.Engine, model.Model,
            ShioriPrompt.Instructions(session.Mode, constitution, offices), ShioriPrompt.Message(session.Mode, person.Text, _spoken.GetValueOrDefault(session.Id)),
            session.Token, ShioriTools.For(session.Mode));
    }

    /// <summary>Reads the session, changes it and saves it, all under the session's lock.</summary>
    private Task<ShioriSession> ChangeAsync(Guid id, Action<ShioriSession> change, CancellationToken cancellationToken) =>
        ChangeAsync(id, s =>
        {
            change(s);
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task<ShioriSession> ChangeAsync(Guid id, Func<ShioriSession, Task> change, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await FindAsync(id, cancellationToken).ConfigureAwait(false);
            await change(session).ConfigureAwait(false);
            session.UpdatedAt = clock.GetUtcNow();
            await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ShioriSession> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Shiori session", id);

    private async Task WriteNoteAsync(ShioriSession session, string what, CancellationToken cancellationToken)
    {
        try
        {
            await vault.WriteAsync(SessionNote.Render(session, clock.LocalTimeZone), Attention.DecisionRules.Shiori, cancellationToken, $"{what} {session.NotePath}")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The session is safe in the database; the note is written again with the next change.
            LogNoteFailed(logger, ex, session.NotePath);
        }
    }

    private async Task PublishAsync(Func<IShioriPublisher, Task> send)
    {
        try
        {
            await send(publisher).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException)
        {
            LogNotPublished(logger, ex.Message);
        }
    }

    private static void Open(ShioriSession session)
    {
        if (session.Ended)
        {
            throw new RejectedException(EndedCode, $"The session \"{session.Title}\" has ended: open a new one.");
        }
    }

    private bool IsAnswering(Guid id) => _answering.ContainsKey(id);

    private ShioriSessionView View(ShioriSession session) => ShioriSessionView.From(session, IsAnswering(session.Id));

    private ShioriState State(ShioriSession session) => new(session.Id, session.Mode, IsAnswering(session.Id), session.Ended);

    private DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    [LoggerMessage(Level = LogLevel.Error, Message = "Shiori could not answer in session {SessionId}")]
    private static partial void LogAnswerFailed(ILogger logger, Exception exception, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the session note {Path}; it is written again with the next change")]
    private static partial void LogNoteFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not show a Shiori session's change: {Reason}")]
    private static partial void LogNotPublished(ILogger logger, string reason);
}
