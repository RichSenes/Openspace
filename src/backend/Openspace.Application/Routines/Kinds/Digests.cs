using System.Globalization;
using System.Text;
using Openspace.Application.Attention;
using Openspace.Application.Brain;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The weekly recap kept in the vault, so the wiki has the history the chat forgets: one note per
/// week, and one per month that gathers that month's weeks. The month needs no routine or schedule
/// of its own: each week adds itself to it, and a week written again replaces its own part.
/// </summary>
public static class Digests
{
    public const string Folder = "Digests";

    /// <summary>"Digests/2026-W47.md", by the ISO week of the day.</summary>
    public static string WeekPath(DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"{Folder}/{WeekName(day)}.md");

    /// <summary>"Digests/2026-11.md".</summary>
    public static string MonthPath(DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"{Folder}/{day.Year:0000}-{day.Month:00}.md");

    /// <summary>Writes the week's note and puts the week into its month's note.</summary>
    /// <param name="day">The day the recap was made, on the person's clock.</param>
    /// <param name="from">What made it: the routine, by name.</param>
    public static async Task WriteAsync(IVault vault, DateOnly day, string text, string from, CultureInfo culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var week = WeekName(day);
        var name = SourceName(from);

        // One part per routine, so two offices' recaps of the same week sit side by side.
        var weekPath = WeekPath(day);
        var existing = await vault.ReadAsync(weekPath, cancellationToken).ConfigureAwait(false);
        var weekBody = WithSection(existing?.Body ?? $"# {week}\n", $"## {name}", text.Trim());
        var weekNote = existing is null ? Digest(weekPath, weekBody, from) : existing with { Body = weekBody };
        await vault.WriteAsync(weekNote, DecisionRules.Shiori, cancellationToken, $"wrote the digest of {week}").ConfigureAwait(false);

        var monthPath = MonthPath(day);
        var month = await vault.ReadAsync(monthPath, cancellationToken).ConfigureAwait(false);
        var heading = $"# {Capital(day.ToString("MMMM yyyy", culture))}";
        var body = WithSection(month?.Body ?? $"{heading}\n", $"## [[{week}]] · {name}", text.Trim());
        var note = month is null ? Digest(monthPath, body, from) : month with { Body = body };
        await vault.WriteAsync(note, DecisionRules.Shiori, cancellationToken, $"added {week} to the digest of {Path.GetFileNameWithoutExtension(monthPath)}").ConfigureAwait(false);
    }

    /// <summary>"Week" from "routine Week": the name a part of the digest is headed with.</summary>
    private static string SourceName(string from) =>
        from.StartsWith("routine ", StringComparison.Ordinal) ? from["routine ".Length..] : from;

    /// <summary>The body with this section's text in place of what it had, or added at the end.</summary>
    public static string WithSection(string body, string heading, string text)
    {
        ArgumentNullException.ThrowIfNull(body);
        var lines = body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').ToList();
        var start = lines.FindIndex(l => l.TrimEnd() == heading);
        var section = new[] { heading, string.Empty }.Concat(text.Split('\n')).Append(string.Empty).ToList();
        if (start < 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(section);
        }
        else
        {
            var end = lines.FindIndex(start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
            lines.RemoveRange(start, (end < 0 ? lines.Count : end) - start);
            lines.InsertRange(start, section);
        }

        return new StringBuilder(string.Join('\n', lines).TrimEnd('\n')).Append('\n').ToString();
    }

    private static string WeekName(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(date):0000}-W{ISOWeek.GetWeekOfYear(date):00}");
    }

    private static Note Digest(string path, string body, string from) =>
        Note.Create(path, VaultFormat.DerivedTitle(path, body), body) with { Kind = "digest", From = from, Author = DecisionRules.Shiori };

    private static string Capital(string text) => text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));
}
