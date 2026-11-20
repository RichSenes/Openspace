using System.Text;
using System.Text.RegularExpressions;

namespace Openspace.Application.Brain;

/// <param name="Text">Words to look for; every note when blank.</param>
/// <param name="Office">The office asking: notes about it come first.</param>
/// <param name="Room">The room asking: notes about it come first.</param>
/// <param name="Path">A file being worked on: notes whose paths cover it come first.</param>
/// <param name="Kind">Only notes of this kind, when given.</param>
public sealed record BrainQuery(string? Text, string? Office = null, string? Room = null, string? Path = null, string? Kind = null, int Limit = BrainSearch.DefaultLimit);

public sealed record BrainHit(
    string Path,
    string Title,
    string? Kind,
    string? Status,
    IReadOnlyList<string> Offices,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<string> Paths,
    DateOnly? Review,
    DateTimeOffset Modified,
    string Excerpt);

/// <summary>
/// Finds notes by words and ranks them in the order the plan gives: the words first (all of them
/// in the title beats finding them in the body), then how well the note's scope fits the office,
/// room and file asked about, then its status, then the index's score of the words, then how
/// recent it is. Superseded notes always come last: they are history, kept to answer "why",
/// not what to follow today.
/// </summary>
public sealed partial class BrainSearch(IBrainIndex index)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<IReadOnlyList<BrainHit>> SearchAsync(BrainQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = Words(query.Text);
        if (words.Count == 0 && !string.IsNullOrWhiteSpace(query.Text))
        {
            // Only punctuation: nothing can match it.
            return [];
        }

        var kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.Trim();
        var found = await index.FindAsync(words, kind, cancellationToken).ConfigureAwait(false);
        return [.. found
            .OrderBy(n => IsSuperseded(n) ? 1 : 0)
            .ThenByDescending(n => InTitle(words, n.Title) ? 1 : 0)
            .ThenByDescending(n => ScopeScore(query, n))
            .ThenBy(StatusRank)
            .ThenBy(n => n.TextRank)
            .ThenByDescending(n => n.Modified)
            .ThenBy(n => n.Path, StringComparer.Ordinal)
            .Take(Math.Clamp(query.Limit, 1, MaxLimit))
            .Select(n => new BrainHit(n.Path, n.Title, n.Kind, n.Status, n.Offices, n.Rooms, n.Paths, n.Review, n.Modified, n.Excerpt))];
    }

    /// <summary>The words of a query, as the index splits them.</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. Word().Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Two points for each asked scope the note names, one when the note leaves it open (it
    /// applies everywhere), none when it names only others.
    /// </summary>
    public static int ScopeScore(BrainQuery query, IndexedNote note)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(note);
        static int Fit(string? asked, IReadOnlyList<string> scope, Func<string, string, bool> matches) =>
            string.IsNullOrWhiteSpace(asked) ? 0
            : scope.Count == 0 ? 1
            : scope.Any(s => matches(asked.Trim(), s)) ? 2
            : 0;

        static bool Same(string asked, string named) => string.Equals(asked, named.Trim(), StringComparison.OrdinalIgnoreCase);
        return Fit(query.Office, note.Offices, Same) + Fit(query.Room, note.Rooms, Same) + Fit(query.Path, note.Paths, Covers);
    }

    /// <summary>Whether a note's path glob covers a file, or lies inside a folder, being asked about.</summary>
    public static bool Covers(string path, string glob)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(glob);
        var asked = path.Replace('\\', '/').Trim().TrimStart('.', '/').TrimEnd('/');
        var pattern = glob.Replace('\\', '/').Trim().TrimStart('.', '/');
        if (Regex.IsMatch(asked, GlobToRegex(pattern), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            return true;
        }

        // Asking about a folder finds the notes about what is inside it.
        var star = pattern.IndexOfAny(['*', '?']);
        var literal = star >= 0 ? pattern[..star] : pattern;
        return asked.Length > 0 && literal.StartsWith(asked + "/", StringComparison.Ordinal);
    }

    private static string GlobToRegex(string glob)
    {
        var regex = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var slash = i + 2 < glob.Length && glob[i + 2] == '/';
                regex.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else
            {
                regex.Append(c switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(c.ToString()) });
            }
        }

        // A plain folder covers everything under it.
        return regex.Append(glob.EndsWith('/') || glob.Contains('*', StringComparison.Ordinal) ? "$" : "(?:/.*)?$").ToString();
    }

    private static bool IsSuperseded(IndexedNote note) => string.Equals(note.Status, NoteStatus.Superseded, StringComparison.OrdinalIgnoreCase);

    private static int StatusRank(IndexedNote note) =>
        string.Equals(note.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase) ? 0
        : string.Equals(note.Status, NoteStatus.Proposed, StringComparison.OrdinalIgnoreCase) ? 1
        : 2;

    private static bool InTitle(IReadOnlyList<string> words, string title)
    {
        if (words.Count == 0)
        {
            return false;
        }

        var inTitle = Words(title);
        return words.All(w => inTitle.Any(t => t.StartsWith(w, StringComparison.Ordinal)));
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex Word();
}
