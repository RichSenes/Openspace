using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>What a routine's kind works with during one run.</summary>
public sealed class RoutineContext(
    Routine routine, RoutineRun run, IRoutineRepository routines, IRoutineOutbox outbox, TimeProvider clock, IRoutineKind? kind = null,
    IRoutineProgress? progress = null)
{
    /// <summary>Met during this run: not in the database until the run is saved.</summary>
    private readonly Dictionary<string, SeenItem> _met = new(StringComparer.Ordinal);

    /// <summary>What to let go of when the run ends: its turn on the graphics card.</summary>
    private readonly List<Action> _atEnd = [];

    public Routine Routine { get; } = routine;

    public RoutineRun Run { get; } = run;

    public TimeProvider Clock { get; } = clock;

    /// <summary>The routine's settings.</summary>
    public JsonElement Settings { get; } = JsonDocument.Parse(routine.Settings).RootElement.Clone();

    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>A line in the run's log, for people looking at what it did.</summary>
    public void Write(string line)
    {
        Run.Write(line, Now);
        progress?.Report(Routine, line);
    }

    /// <summary>
    /// Where a long step is, for whoever watches, without filling the log: "Sorting 12 of 40".
    /// Long steps say it at every item, so a run that stops talking is a run that is stuck.
    /// </summary>
    public void Progress(string line) => progress?.Report(Routine, line);

    /// <summary>This run's turn on the graphics card, for the local models it asks.</summary>
    public LocalModels.RoutineTurn Turn => new(Run.Id.Value.ToString(), Routine.Name, action =>
    {
        lock (_atEnd)
        {
            _atEnd.Add(action);
        }
    });

    /// <summary>The run is over, whatever happened: what it held is let go.</summary>
    public void Ended()
    {
        List<Action> actions;
        lock (_atEnd)
        {
            actions = [.. _atEnd];
            _atEnd.Clear();
        }

        actions.ForEach(a => a());
    }

    /// <summary>
    /// Something the routine wants to do with the person's things. What can be undone is done
    /// at once, unless the routine asks first; anything else waits for a yes.
    /// </summary>
    public async Task<Proposal> ProposeAsync(string action, string title, string body, object? payload, CancellationToken cancellationToken)
    {
        var proposal = Proposal.Make(
            Run, action, title, body, payload is null ? null : payload as string ?? JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), Now);
        routines.AddProposal(proposal);
        if (Routine.AskFirst || kind is null || !kind.CanUndo(action))
        {
            Write($"Proposed: {title}");
            return proposal;
        }

        proposal.Approve(Now);
        try
        {
            var outcome = await kind.CarryOutAsync(proposal, this, cancellationToken).ConfigureAwait(false);
            proposal.Carried(string.IsNullOrWhiteSpace(outcome) ? "Done." : outcome, Now);
            Write($"Done on its own: {title}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            proposal.CouldNotCarry(ex.Message, Now);
            Write($"Could not do {title}: {ex.Message}");
        }

        return proposal;
    }

    /// <summary>
    /// Meets something (an offer, a news item) by what makes it the same thing again. True
    /// when it deserves a look: never met, or changed in substance and not dismissed.
    /// </summary>
    /// <param name="fingerprint">A digest of what matters in it; <see cref="Fingerprint"/> makes one.</param>
    public async Task<bool> MeetAsync(string key, string fingerprint, string title, CancellationToken cancellationToken)
    {
        if (await SeenAsync(key, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            return seen.MeetAgain(fingerprint, title, Now);
        }

        var item = SeenItem.Meet(Routine.Id, key, fingerprint, title, SeenState.Skipped, Now);
        routines.AddSeen(item);
        _met[item.Key] = item;
        return true;
    }

    /// <summary>
    /// Whether meeting it would give it a look, without meeting it: for a routine that looks first
    /// and meets only what it could judge, so nothing is lost when judging fails.
    /// </summary>
    public async Task<bool> IsNewAsync(string key, string fingerprint, CancellationToken cancellationToken) =>
        await SeenAsync(key, cancellationToken).ConfigureAwait(false) is not { } seen
        || (seen.State != SeenState.Dismissed && !string.Equals(seen.Fingerprint, fingerprint, StringComparison.Ordinal));

    /// <summary>Says what became of something met: shown, skipped, dismissed or taken up.</summary>
    public async Task MarkAsync(string key, SeenState state, CancellationToken cancellationToken)
    {
        if (await SeenAsync(key, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            seen.Mark(state, Now);
        }
    }

    /// <summary>The keys starting with <paramref name="prefix"/> of what was shown, dismissed or taken up: all but the skipped.</summary>
    public async Task<IReadOnlySet<string>> DealtWithAsync(string prefix, CancellationToken cancellationToken)
    {
        var saved = await routines.ListSeenAsync(Routine.Id, prefix, cancellationToken).ConfigureAwait(false);
        return saved.Concat(_met.Values.Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(s => s.State != SeenState.Skipped)
            .Select(s => s.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<SeenItem?> SeenAsync(string key, CancellationToken cancellationToken) =>
        _met.TryGetValue(key.Trim(), out var met) ? met : await routines.FindSeenAsync(Routine.Id, key.Trim(), cancellationToken).ConfigureAwait(false);

    /// <summary>Sends what the run made to the person: the paper, a summary.</summary>
    public async Task DeliverAsync(string text, string? filePath, CancellationToken cancellationToken)
    {
        if (await outbox.SendAsync(Routine, text, filePath, cancellationToken).ConfigureAwait(false))
        {
            Write(filePath is null ? "Delivered a message" : $"Delivered {Path.GetFileName(filePath)}");
        }
        else
        {
            Write("Not delivered: this routine names no channel.");
        }
    }

    /// <summary>A short, stable digest of the parts of something that matter, to tell when it changed.</summary>
    public static string Fingerprint(params string?[] parts)
    {
        var normalized = string.Join('\u001f', parts.Select(p => string.Join(' ', (p ?? string.Empty).ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))[..16];
    }
}
