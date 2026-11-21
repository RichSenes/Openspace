using System.Globalization;
using Openspace.Application.Brain;

namespace Openspace.Application.Attention;

/// <summary>What the person can say about a decision from the inbox.</summary>
public enum DecisionVerb
{
    /// <summary>Accepted by Shiori, and the person agrees.</summary>
    Fine,

    /// <summary>Accepted by Shiori, and the person takes it back.</summary>
    Undo,

    /// <summary>Still proposed, and the person accepts it.</summary>
    Accept,

    /// <summary>Still proposed, and the person turns it down.</summary>
    Reject,

    /// <summary>Due for review, and it still holds: it comes back in a few months.</summary>
    Keep,

    /// <summary>Due for review, and it no longer holds.</summary>
    Supersede,
}

/// <summary>
/// The person's answers on decisions. Each one rewrites the note's front matter and commits it
/// with the person as author; taking back or turning down a proposal also tells the desk that
/// proposed it, in its journal, so the agent learns why next time.
/// </summary>
public sealed class DecisionCommands(IVault vault, TimeProvider clock)
{
    public const string WrongStatusCode = "decision-status";

    /// <param name="who">The person, as their commits name them.</param>
    /// <param name="reason">Why, in the person's words; optional.</param>
    /// <returns>The note as it is now, or null when there is no such note.</returns>
    public async Task<Note?> DoAsync(string path, DecisionVerb verb, string who, string? reason, CancellationToken cancellationToken)
    {
        if (await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);
        var (changed, summary) = verb switch
        {
            DecisionVerb.Fine when DecisionRules.ToReview(note) => (DecisionRules.With(note, DecisionRules.Reviewed, "true"), $"reviewed {note.Path}"),
            DecisionVerb.Undo when IsAccepted(note) => (DecisionRules.With(note with { Status = NoteStatus.Rejected }, DecisionRules.Reviewed, "true"), $"undid {note.Path}"),
            DecisionVerb.Accept when DecisionRules.IsProposed(note) => (
                DecisionRules.With(DecisionRules.With(note with { Status = NoteStatus.Accepted }, DecisionRules.AcceptedBy, who), DecisionRules.Reviewed, "true"),
                $"accepted {note.Path}"),
            DecisionVerb.Reject when DecisionRules.IsProposed(note) => (note with { Status = NoteStatus.Rejected }, $"turned down {note.Path}"),
            DecisionVerb.Keep when IsAccepted(note) => (note with { Review = today.AddMonths(DecisionRules.KeepMonths) }, $"kept {note.Path} until {DecisionRules.Day(today.AddMonths(DecisionRules.KeepMonths))}"),
            DecisionVerb.Supersede when IsAccepted(note) => (note with { Status = NoteStatus.Superseded }, $"marked {note.Path} superseded"),
            _ => throw new RejectedException(WrongStatusCode, $"\"{note.Title}\" is {note.Status ?? "without a status"}: it cannot be {Past(verb)} now."),
        };

        await vault.WriteAsync(changed, who, cancellationToken, summary).ConfigureAwait(false);
        if (verb is DecisionVerb.Undo or DecisionVerb.Reject)
        {
            await TellProposerAsync(note, verb, reason, cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    /// <summary>
    /// A line in the proposing desk's journal: what became of its proposal, and why when the person
    /// said. Written as the desks write their own (a heading per day, a line per entry), so the
    /// journal reads as one story.
    /// </summary>
    private async Task TellProposerAsync(Note note, DecisionVerb verb, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note.Author))
        {
            return;
        }

        var desk = note.Author.Trim();
        var what = verb == DecisionVerb.Undo ? "the person undid" : "the person did not accept";
        var why = string.IsNullOrWhiteSpace(reason) ? "No reason given." : $"Why: {reason.Trim()}";
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);
        await Journals.AppendAsync(
            vault, desk, now, $"{DecisionRules.Shiori}: {what} [[{DecisionRules.Link(note.Path)}|{note.Title}]]. {why}", DecisionRules.Shiori,
            $"told {desk} about {note.Path}", cancellationToken).ConfigureAwait(false);
    }

    private static bool IsAccepted(Note note) => string.Equals(note.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase);

    private static string Past(DecisionVerb verb) => verb switch
    {
        DecisionVerb.Fine => "marked as reviewed",
        DecisionVerb.Undo => "undone",
        DecisionVerb.Accept => "accepted",
        DecisionVerb.Reject => "turned down",
        DecisionVerb.Keep => "kept",
        _ => "superseded",
    };
}
