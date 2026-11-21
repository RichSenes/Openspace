using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;

namespace Openspace.Application.Shiori;

/// <summary>
/// A session as a note of <c>Sessions/</c>: the transcript, then the whiteboard by column. It is a
/// copy for reading, linking and searching; the session itself lives in Openspace's database.
/// </summary>
public static partial class SessionNote
{
    public const string Folder = "Sessions";

    /// <summary>The heading of what an ended session leaves for the next day.</summary>
    public const string NextHeading = "## Next";

    /// <summary>Where a new session's note goes: <c>Sessions/2026-11-21-merge-order.md</c>.</summary>
    public static string PathFor(DateTimeOffset localNow, string title) =>
        $"{Folder}/{localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{Slug(title, "session")}.md";

    public static Note Render(ShioriSession session, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(zone);
        string Time(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var body = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {session.Title}")
            .AppendLine()
            .AppendLine("## Transcript")
            .AppendLine();
        if (session.Transcript.Count == 0)
        {
            body.AppendLine("Nothing said yet.").AppendLine();
        }

        foreach (var entry in session.Transcript)
        {
            var mode = ShioriPrompt.Name(entry.Mode);
            switch (entry.Role)
            {
                case TranscriptRole.Person:
                    body.AppendLine(CultureInfo.InvariantCulture, $"**{session.Person}** · {Time(entry.At)} · {mode}").AppendLine().AppendLine(entry.Text).AppendLine();
                    break;
                case TranscriptRole.Shiori:
                    body.AppendLine(CultureInfo.InvariantCulture, $"**{DecisionRules.Shiori}** · {Time(entry.At)}").AppendLine().AppendLine(entry.Text).AppendLine();
                    break;
                case TranscriptRole.Mode:
                    body.AppendLine(CultureInfo.InvariantCulture, $"*Mode: {mode}* · {Time(entry.At)}").AppendLine();
                    break;
                case TranscriptRole.Failed:
                    body.AppendLine(CultureInfo.InvariantCulture, $"*No answer* · {Time(entry.At)}: {entry.Text}").AppendLine();
                    break;
                default:
                    body.AppendLine(CultureInfo.InvariantCulture, $"*Ended* · {Time(entry.At)}").AppendLine();
                    break;
            }
        }

        body.AppendLine("## Whiteboard").AppendLine();
        foreach (var (kind, heading) in Columns)
        {
            var items = session.Board.Where(i => i.Kind == kind).ToList();
            body.AppendLine(CultureInfo.InvariantCulture, $"### {heading}").AppendLine();
            if (items.Count == 0)
            {
                body.AppendLine("None.").AppendLine();
                continue;
            }

            foreach (var item in items)
            {
                var office = item.Office is null ? string.Empty : $" ({item.Office})";
                var note = item.Note is null ? string.Empty : $" → [[{DecisionRules.Link(item.Note)}]]";
                body.AppendLine(CultureInfo.InvariantCulture, $"- [{(item.Done ? "x" : " ")}] {item.Text}{office}{note}");
            }

            body.AppendLine();
        }

        // The tasks still open when the session ends are what the next day starts from: the
        // "tomorrow" routine reads unticked items under a "Next" heading of recent sessions.
        var open = session.Board.Where(i => i.Kind == WhiteboardKind.Task && !i.Done).ToList();
        if (session.EndedAt is not null && open.Count > 0)
        {
            body.AppendLine(NextHeading).AppendLine();
            foreach (var item in open)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {item.Text}{(item.Office is null ? string.Empty : $" ({item.Office})")}");
            }

            body.AppendLine();
        }

        List<KeyValuePair<string, string>> extra =
        [
            new("session", $" {session.Id}"),
            new("mode", $" {ShioriPrompt.Name(session.Mode)}"),
        ];
        if (session.EndedAt is { } ended)
        {
            extra.Add(new("ended", $" {Time(ended)}"));
        }

        return new Note(session.NotePath, "session", null, session.Title, [], [], [], null, null, null, DecisionRules.Shiori, body.ToString().TrimEnd() + "\n", extra);
    }

    private static readonly (WhiteboardKind Kind, string Heading)[] Columns =
    [
        (WhiteboardKind.Idea, "Ideas"),
        (WhiteboardKind.Doubt, "Doubts"),
        (WhiteboardKind.Decision, "Decisions"),
        (WhiteboardKind.Task, "Tasks"),
    ];

    /// <summary>Text as a file name: lowercase words joined by dashes, accents dropped.</summary>
    public static string Slug(string text, string fallback)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            }
        }

        var slug = Dashes().Replace(plain.ToString(), "-").Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        return slug.Length > 0 ? slug : fallback;
    }

    [GeneratedRegex("-+")]
    private static partial Regex Dashes();
}
