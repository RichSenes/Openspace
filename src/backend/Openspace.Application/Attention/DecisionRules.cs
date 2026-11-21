using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Openspace.Application.Brain;

namespace Openspace.Application.Attention;

/// <summary>
/// The rules Shiori follows on proposals until it exists as a chat, and the few fields of a
/// decision's life after that (who accepted it, whether the person looked at it). They are kept
/// in the note's own front matter, so the vault alone tells the whole story.
/// </summary>
public static class DecisionRules
{
    /// <summary>Who accepts proposals and writes digests: one voice, one name in the vault's history.</summary>
    public const string Shiori = "Shiori";

    public const string AcceptedBy = "accepted_by";

    public const string Reviewed = "reviewed";

    /// <summary>How long a kept decision waits before it comes back.</summary>
    public const int KeepMonths = 3;

    /// <summary>
    /// Why a proposal waits for the person instead of being accepted by Shiori; null when
    /// Shiori may accept it. The rule is narrow on purpose: anything that reaches past one
    /// office, changes the constitution, replaces a decision or may contradict one is the
    /// person's to say.
    /// </summary>
    /// <param name="accepted">The accepted decisions it is weighed against.</param>
    public static string? WhyWaiting(Note proposal, IEnumerable<Note> accepted)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(accepted);
        if (string.Equals(proposal.Kind, "rule", StringComparison.OrdinalIgnoreCase))
        {
            return "A rule of the constitution: only you change those.";
        }

        if (proposal.Offices.Count > 1)
        {
            return $"It is about {proposal.Offices.Count} offices: Shiori accepts only what stays in one.";
        }

        if (!string.IsNullOrWhiteSpace(proposal.Supersedes))
        {
            return $"It would replace {proposal.Supersedes}: replacing a decision is yours to say.";
        }

        var clash = accepted.FirstOrDefault(d => d.Path != proposal.Path && Overlap(proposal, d) && SameSubject(proposal, d));
        return clash is null ? null : $"[[{Link(clash.Path)}]] is accepted on the same subject and may say otherwise.";
    }

    /// <summary>Whether the note is an accepted decision, the kind proposals are weighed against.</summary>
    public static bool IsAcceptedDecision(Note note) =>
        note is not null && string.Equals(note.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase)
        && string.Equals(note.Kind, "decision", StringComparison.OrdinalIgnoreCase);

    public static bool IsProposed(Note note) => string.Equals(note?.Status, NoteStatus.Proposed, StringComparison.OrdinalIgnoreCase);

    /// <summary>Accepted by Shiori and not yet looked at by the person.</summary>
    public static bool ToReview(Note note) =>
        note is not null && string.Equals(note.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Field(note, AcceptedBy), Shiori, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Field(note, Reviewed), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Accepted decisions whose review date has come by <paramref name="today"/>, about the office
    /// named or about every office.
    /// </summary>
    public static IEnumerable<Note> Due(IEnumerable<Note> notes, string? office, DateOnly today) =>
        notes.Where(n => IsAcceptedDecision(n) && n.Review is { } review && review <= today
            && (n.Offices.Count == 0 || office is null || n.Offices.Any(o => Same(o, office))));

    /// <summary>One of the note's own fields, as written, without quotes; null when it has none.</summary>
    public static string? Field(Note note, string key)
    {
        ArgumentNullException.ThrowIfNull(note);
        var raw = note.Extra.LastOrDefault(e => e.Key == key).Value;
        return raw is null ? null : raw.Trim().Trim('"', '\'');
    }

    /// <summary>The note with this field set, in place when it had it, at the end otherwise.</summary>
    public static Note With(Note note, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(note);
        var extra = note.Extra.Where(e => e.Key != key).ToList();
        extra.Add(new(key, " " + value));
        return note with { Extra = extra };
    }

    /// <summary>A stable id for a note, so the inbox can name it like any other item.</summary>
    public static Guid IdOf(string path) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(path ?? string.Empty)).AsSpan(0, 16));

    /// <summary>The note's path as a wikilink target: without <c>.md</c>.</summary>
    public static string Link(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;

    /// <summary>A date as notes write it.</summary>
    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Notes about every office overlap with all; otherwise they must share one.</summary>
    private static bool Overlap(Note a, Note b) =>
        a.Offices.Count == 0 || b.Offices.Count == 0 || a.Offices.Any(o => b.Offices.Any(p => Same(o, p)));

    /// <summary>The same subject: the same title, or the same file name once its date is left out.</summary>
    private static bool SameSubject(Note a, Note b)
    {
        var (titleA, titleB) = (Subject(a.Title), Subject(b.Title));
        var (fileA, fileB) = (Subject(FileSubject(a.Path)), Subject(FileSubject(b.Path)));
        return (titleA.Length > 0 && titleA == titleB) || (fileA.Length > 0 && fileA == fileB);
    }

    private static string FileSubject(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        // "2026-11-20-merge-order" is about "merge order", whatever day it was written.
        return name.Length > 11 && DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && name[10] == '-'
            ? name[11..]
            : name;
    }

    /// <summary>Lower case letters and digits, one space between words: what a title says, not how.</summary>
    private static string Subject(string text)
    {
        var words = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c))
            {
                words.Append(char.ToLowerInvariant(c));
            }
            else if (words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().Trim();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
