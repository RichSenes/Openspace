using System.Globalization;
using System.Text.RegularExpressions;

namespace Openspace.Application.Brain;

/// <summary>An accepted decision that explains why a file is the way it is.</summary>
/// <param name="Decided">The date the decision's file name starts with, else the day its file last changed.</param>
/// <param name="Covering">The decision's path globs that cover the file asked about.</param>
/// <param name="From">The task, routine or session it came from, as the note says it.</param>
public sealed record WikiReason(
    string Path,
    string Title,
    DateOnly Decided,
    IReadOnlyList<string> Offices,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<string> Covering,
    string? From,
    DateOnly? Review,
    DateTimeOffset Modified);

/// <summary>
/// "Why is this so?": the accepted decisions whose paths cover a file of a repository. Only
/// decisions that name paths answer: one without paths is about how people work, not about a
/// file. Newest first, because the latest word on a file is the one that holds.
/// </summary>
public static partial class WikiWhy
{
    /// <param name="file">A file of the repository, relative to its root: <c>src/web/cart.ts</c>.</param>
    /// <param name="office">When given, only decisions about that office or about every office.</param>
    public static IReadOnlyList<WikiReason> For(string file, string? office, IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(notes);
        if (string.IsNullOrWhiteSpace(file))
        {
            return [];
        }

        var asked = string.IsNullOrWhiteSpace(office) ? null : office.Trim();
        return [.. notes
            .Where(IsAcceptedDecision)
            .Where(n => asked is null || n.Offices.Count == 0 || n.Offices.Any(o => string.Equals(o.Trim(), asked, StringComparison.OrdinalIgnoreCase)))
            .Select(n => (Note: n, Covering: n.Paths.Where(g => !string.IsNullOrWhiteSpace(g) && BrainSearch.Covers(file, g)).ToList()))
            .Where(x => x.Covering.Count > 0)
            .Select(x => new WikiReason(
                x.Note.Path, x.Note.Title, Decided(x.Note), x.Note.Offices, x.Note.Rooms, x.Covering, x.Note.From, x.Note.Review, x.Note.Modified))
            .OrderByDescending(r => r.Decided)
            .ThenByDescending(r => r.Modified)
            .ThenBy(r => r.Path, StringComparer.Ordinal)];
    }

    private static bool IsAcceptedDecision(Note note) =>
        string.Equals(note.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase)
        && string.Equals(note.Kind, "decision", StringComparison.OrdinalIgnoreCase);

    // Decisions are named by the day they were made ("2026-11-20-merge-order.md"); the file's
    // own date moves whenever someone touches it, so it is only the fallback.
    private static DateOnly Decided(Note note)
    {
        var day = DatePrefix().Match(note.Path[(note.Path.LastIndexOf('/') + 1)..]);
        return day.Success && DateOnly.TryParseExact(day.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.FromDateTime(note.Modified.UtcDateTime);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePrefix();
}
