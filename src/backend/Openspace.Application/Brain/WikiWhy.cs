using System.Globalization;
using System.Text.RegularExpressions;

namespace Openspace.Application.Brain;

/// <summary>An accepted decision that explains why a file is the way it is.</summary>
/// <param name="Decided">The date the decision's file name starts with, else the day its file last changed.</param>
/// <param name="Covering">The decision's path globs that cover the file asked about.</param>
/// <param name="From">The task, routine or session it came from, as the note says it.</param>
/// <param name="General">A decision about no file in particular, shown because its office and room fit.</param>
public sealed record WikiReason(
    string Path,
    string Title,
    DateOnly Decided,
    IReadOnlyList<string> Offices,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<string> Covering,
    string? From,
    DateOnly? Review,
    DateTimeOffset Modified,
    bool General = false);

/// <summary>
/// "Why is this so?": the accepted decisions whose paths cover a file of a repository, newest
/// first, because the latest word on a file is the one that holds. When the question says which
/// office or room the file belongs to, the decisions with no paths that apply there follow, marked
/// general: they are about how that place works, which explains a file too, but less directly.
/// </summary>
public static partial class WikiWhy
{
    /// <param name="file">A file of the repository, relative to its root: <c>src/web/cart.ts</c>.</param>
    /// <param name="office">When given, only decisions about that office or about every office.</param>
    /// <param name="room">When given, decisions about other rooms are left out.</param>
    public static IReadOnlyList<WikiReason> For(string file, string? office, IEnumerable<Note> notes, string? room = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(notes);
        if (string.IsNullOrWhiteSpace(file))
        {
            return [];
        }

        var asked = Blank(office);
        var askedRoom = Blank(room);
        var fitting = notes
            .Where(IsAcceptedDecision)
            .Where(n => Fits(n.Offices, asked) && Fits(n.Rooms, askedRoom))
            .ToList();

        var covering = fitting
            .Select(n => (Note: n, Covering: n.Paths.Where(g => !string.IsNullOrWhiteSpace(g) && BrainSearch.Covers(file, g)).ToList()))
            .Where(x => x.Covering.Count > 0)
            .Select(x => Reason(x.Note, x.Covering, general: false));

        // Without an office or room every decision with no paths would answer for every file.
        var general = asked is null && askedRoom is null
            ? []
            : fitting.Where(n => n.Paths.All(string.IsNullOrWhiteSpace)).Select(n => Reason(n, [], general: true));

        return [.. Newest(covering), .. Newest(general)];
    }

    private static WikiReason Reason(Note n, IReadOnlyList<string> covering, bool general) =>
        new(n.Path, n.Title, Decided(n), n.Offices, n.Rooms, covering, n.From, n.Review, n.Modified, general);

    private static IEnumerable<WikiReason> Newest(IEnumerable<WikiReason> reasons) =>
        reasons.OrderByDescending(r => r.Decided).ThenByDescending(r => r.Modified).ThenBy(r => r.Path, StringComparer.Ordinal);

    /// <summary>A scope with nothing in it is everywhere; otherwise it must name the place asked about.</summary>
    private static bool Fits(IReadOnlyList<string> scope, string? asked) =>
        asked is null || scope.Count == 0 || scope.Any(s => string.Equals(s.Trim(), asked, StringComparison.OrdinalIgnoreCase));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
