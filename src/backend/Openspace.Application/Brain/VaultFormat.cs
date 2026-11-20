using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Openspace.Application.Brain;

/// <summary>
/// Reads and writes notes as Markdown with YAML front matter. Only the YAML that notes use is
/// understood (plain and quoted values, lists in brackets or with dashes, as Obsidian writes them);
/// anything else in the front matter is kept as written, so the person's own fields survive a rewrite.
/// </summary>
public static partial class VaultFormat
{
    private const string Fence = "---";

    private static readonly string[] Known = ["kind", "status", "title", "offices", "rooms", "paths", "review", "supersedes", "from", "author"];

    public static Note Parse(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (entries, body) = Split(text.TrimStart('\uFEFF').ReplaceLineEndings("\n"));
        var values = entries.Where(e => Known.Contains(e.Key)).GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last());
        var extra = entries.Where(e => !Known.Contains(e.Key)).Select(e => new KeyValuePair<string, string>(e.Key, e.Raw)).ToList();

        string? Scalar(string key) => values.TryGetValue(key, out var e) ? ScalarOf(e) : null;
        IReadOnlyList<string> List(string key) => values.TryGetValue(key, out var e) ? ListOf(e) : [];

        DateOnly? review = null;
        if (values.TryGetValue("review", out var due))
        {
            // A date Openspace cannot read is kept as written rather than dropped.
            if (DateOnly.TryParseExact(ScalarOf(due), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                review = date;
            }
            else
            {
                extra.Add(new("review", due.Raw));
            }
        }

        var title = Scalar("title");
        return new Note(
            path, Scalar("kind"), Scalar("status"),
            string.IsNullOrWhiteSpace(title) ? DerivedTitle(path, body) : title,
            List("offices"), List("rooms"), List("paths"), review,
            Scalar("supersedes"), Scalar("from"), Scalar("author"), body, extra);
    }

    public static string Write(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var yaml = new StringBuilder();
        void Line(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                yaml.Append(key).Append(": ").Append(value).Append('\n');
            }
        }

        Line("kind", Quote(note.Kind));
        Line("status", Quote(note.Status));

        // The title is written only when the file could not tell it on its own.
        Line("title", note.Title == DerivedTitle(note.Path, note.Body) ? null : Quote(note.Title));
        Line("offices", Flow(note.Offices));
        Line("rooms", Flow(note.Rooms));
        Line("paths", Flow(note.Paths));
        Line("review", note.Review?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Line("supersedes", Quote(note.Supersedes));
        Line("from", Quote(note.From));
        Line("author", Quote(note.Author));
        foreach (var (key, raw) in note.Extra)
        {
            yaml.Append(key).Append(':').Append(raw).Append('\n');
        }

        return yaml.Length == 0 ? note.Body : $"{Fence}\n{yaml}{Fence}\n{note.Body}";
    }

    /// <summary>The targets of the <c>[[wikilinks]]</c> in a text, without headings or aliases.</summary>
    public static IReadOnlyList<string> Links(string text) =>
        [.. WikiLink().Matches(text).Select(m => m.Groups[1].Value.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary>The first heading of the body, else the file's name.</summary>
    public static string DerivedTitle(string path, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var heading = Heading().Match(body);
        return heading.Success ? heading.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path);
    }

    private sealed record Entry(string Key, string Inline, IReadOnlyList<string> More)
    {
        /// <summary>Everything after the key's colon, as it was in the file.</summary>
        public string Raw => (Inline.Length > 0 ? " " + Inline : "") + string.Concat(More.Select(m => "\n" + m));
    }

    private static (List<Entry> Entries, string Body) Split(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length < 2 || lines[0].TrimEnd() != Fence)
        {
            return ([], text);
        }

        var end = Array.FindIndex(lines, 1, l => l.TrimEnd() is Fence or "...");
        if (end < 0)
        {
            return ([], text);
        }

        var entries = new List<Entry>();
        string? key = null;
        var inline = "";
        var more = new List<string>();
        foreach (var line in lines[1..end])
        {
            var top = line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] is not '-' and not '#' ? KeyLine().Match(line) : Match.Empty;
            if (top.Success)
            {
                if (key is not null)
                {
                    entries.Add(new(key, inline, more));
                }

                key = top.Groups[1].Value;
                inline = top.Groups[2].Value.Trim();
                more = [];
            }
            else if (key is not null)
            {
                more.Add(line);
            }
        }

        if (key is not null)
        {
            entries.Add(new(key, inline, more));
        }

        // Trailing blank lines belong to no field.
        foreach (var e in entries)
        {
            while (e.More is List<string> { Count: > 0 } m && string.IsNullOrWhiteSpace(m[^1]))
            {
                m.RemoveAt(m.Count - 1);
            }
        }

        return (entries, string.Join('\n', lines[(end + 1)..]));
    }

    private static string? ScalarOf(Entry e)
    {
        var more = e.More.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        if (e.Inline is "|" or "|-" or ">" or ">-")
        {
            return string.Join(e.Inline[0] == '|' ? "\n" : " ", more);
        }

        // An unquoted wikilink is what people mean, even if YAML would read a list in it.
        if (e.Inline.StartsWith("[[", StringComparison.Ordinal))
        {
            return e.Inline;
        }

        var value = Unquote(string.Join(' ', more.Prepend(e.Inline).Where(m => m.Length > 0)));
        return value is "" or "~" or "null" ? null : value;
    }

    private static List<string> ListOf(Entry e)
    {
        if (e.Inline.StartsWith('['))
        {
            var flow = string.Join(' ', e.More.Select(m => m.Trim()).Prepend(e.Inline)).Trim();
            var close = flow.LastIndexOf(']');
            return [.. SplitFlow(flow[1..(close > 0 ? close : flow.Length)]).Select(Unquote).Where(i => i.Length > 0)];
        }

        var items = e.More.Select(m => m.Trim()).Where(m => m.StartsWith('-')).Select(m => Unquote(m[1..])).Where(i => i.Length > 0).ToList();
        if (items.Count > 0)
        {
            return items;
        }

        var single = Unquote(e.Inline);
        return single.Length > 0 ? [single] : [];
    }

    private static IEnumerable<string> SplitFlow(string inner)
    {
        var start = 0;
        char? quote = null;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (quote is not null)
            {
                if (c == '\\' && quote == '"')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == ',')
            {
                yield return inner[start..i];
                start = i + 1;
            }
        }

        yield return inner[start..];
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 1 && value[0] == '"')
        {
            var text = new StringBuilder();
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '"')
                {
                    break;
                }

                if (c == '\\' && i + 1 < value.Length)
                {
                    var next = value[++i];
                    text.Append(next switch { 'n' => '\n', 't' => '\t', _ => next });
                }
                else
                {
                    text.Append(c);
                }
            }

            return text.ToString();
        }

        if (value.Length >= 1 && value[0] == '\'')
        {
            var close = value.Length;
            for (var i = 1; i < value.Length; i++)
            {
                if (value[i] == '\'' && (i + 1 >= value.Length || value[i + 1] != '\''))
                {
                    close = i;
                    break;
                }

                if (value[i] == '\'')
                {
                    i++;
                }
            }

            return value[1..close].Replace("''", "'", StringComparison.Ordinal);
        }

        // A plain value ends where a comment starts.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }

    private static string? Quote(string? value) =>
        string.IsNullOrEmpty(value) ? null
        : Plain().IsMatch(value) ? value
        : "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    private static string? Flow(IReadOnlyList<string> items) =>
        items.Count == 0 ? null : "[" + string.Join(", ", items.Select(Quote)) + "]";

    [GeneratedRegex(@"^([A-Za-z_][\w.-]*)[ \t]*:(?:[ \t]+(.*)|[ \t]*)$")]
    private static partial Regex KeyLine();

    // Safe to write without quotes: no YAML punctuation, no leading or trailing space.
    [GeneratedRegex(@"^[\p{L}\p{N}_(][\p{L}\p{N}_ ./()+-]*(?<! )$")]
    private static partial Regex Plain();

    [GeneratedRegex(@"\[\[([^\[\]|#\n]+)(?:#[^\[\]|\n]*)?(?:\|[^\[\]\n]*)?\]\]")]
    private static partial Regex WikiLink();

    [GeneratedRegex(@"^#[ \t]+(.+?)[ \t]*#*[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
