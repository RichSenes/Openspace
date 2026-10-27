using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>What a routine's kind works with during one run.</summary>
public sealed class RoutineContext(
    Routine routine, RoutineRun run, IRoutineRepository routines, IRoutineOutbox outbox, TimeProvider clock, IRoutineKind? kind = null)
{
    /// <summary>Met during this run: not in the database until the run is saved.</summary>
    private readonly Dictionary<string, SeenItem> _met = new(StringComparer.Ordinal);

    public Routine Routine { get; } = routine;

    public RoutineRun Run { get; } = run;

    public TimeProvider Clock { get; } = clock;

    /// <summary>The routine's settings.</summary>
    public JsonElement Settings { get; } = JsonDocument.Parse(routine.Settings).RootElement.Clone();

    public DateTimeOffset Now => Clock.GetUtcNow();

    /// <summary>A line in the run's log, for people looking at what it did.</summary>
    public void Write(string line) => Run.Write(line, Now);

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

    /// <summary>Says what became of something met: shown, skipped, dismissed or taken up.</summary>
    public async Task MarkAsync(string key, SeenState state, CancellationToken cancellationToken)
    {
        if (await SeenAsync(key, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            seen.Mark(state, Now);
        }
    }

    private async Task<SeenItem?> SeenAsync(string key, CancellationToken cancellationToken) =>
        _met.TryGetValue(key.Trim(), out var met) ? met : await routines.FindSeenAsync(Routine.Id, key.Trim(), cancellationToken).ConfigureAwait(false);

    /// <summary>Sends what the run made to the person: the paper, a summary.</summary>
    public async Task DeliverAsync(string text, string? filePath, CancellationToken cancellationToken)
    {
        await outbox.SendAsync(Routine, text, filePath, cancellationToken).ConfigureAwait(false);
        Write(filePath is null ? "Delivered a message" : $"Delivered {Path.GetFileName(filePath)}");
    }

    /// <summary>A short, stable digest of the parts of something that matter, to tell when it changed.</summary>
    public static string Fingerprint(params string?[] parts)
    {
        var normalized = string.Join('\u001f', parts.Select(p => string.Join(' ', (p ?? string.Empty).ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))[..16];
    }
}
