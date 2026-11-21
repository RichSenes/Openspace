using System.Globalization;
using Openspace.Application.Attention;
using Openspace.Application.Brain;

namespace Openspace.Application.Shiori;

/// <summary>
/// Decisions and rules Shiori proposes from a session. They are written like the agents' proposals
/// (<c>status: proposed</c>, in <c>Decisions/</c>) so the acceptor and the inbox treat them the
/// same way, signed by Shiori and coming from the session.
/// </summary>
public static class ShioriProposals
{
    /// <summary>What a proposal says, before it has a place.</summary>
    /// <param name="Kind"><c>decision</c>, or <c>rule</c> for the constitution (which only the person accepts).</param>
    public sealed record Draft(
        string Kind, string Title, string Body, string Reasons, IReadOnlyList<string> Offices, IReadOnlyList<string> Rooms, IReadOnlyList<string> Paths);

    /// <summary>Where a session's proposals say they come from.</summary>
    public static string From(ShioriSession session) => $"session {session?.Id}";

    /// <returns>The new note's path; a name already taken gets a number, nothing is overwritten.</returns>
    public static async Task<string> WriteAsync(IVault vault, ShioriSession session, Draft draft, DateTimeOffset localNow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(draft);
        var kind = draft.Kind.Trim().ToLowerInvariant();
        if (kind is not ("decision" or "rule"))
        {
            throw new RejectedException("brain-kind", $"Shiori proposes a decision or a rule, not \"{draft.Kind}\".");
        }

        var title = Guard(draft.Title, "A proposal's title", 120).ReplaceLineEndings(" ");
        var body = Guard(draft.Body, "A proposal's body", 20000);
        var reasons = Guard(draft.Reasons, "A proposal's reasons", 4000);
        var stem = $"Decisions/{localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{SessionNote.Slug(title, "proposal")}";
        var path = $"{stem}.md";
        for (var n = 2; await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not null; n++)
        {
            path = $"{stem}-{n}.md";
        }

        var text = $"# {title}\n\n{body}\n\nFrom the session [[{DecisionRules.Link(session.NotePath)}|{session.Title}]].\n\n## Reasons\n\n{reasons}\n";
        var note = new Note(
            path, kind, NoteStatus.Proposed, title, List(draft.Offices), List(draft.Rooms), List(draft.Paths), null, null,
            From(session), DecisionRules.Shiori, text, []);
        await vault.WriteAsync(note, DecisionRules.Shiori, cancellationToken, $"proposed {path}").ConfigureAwait(false);
        return path;
    }

    private static List<string> List(IReadOnlyList<string>? items) =>
        items is null ? [] : [.. items.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal)];

    private static string Guard(string? text, string what, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RejectedException("empty-text", $"{what} cannot be empty.");
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
