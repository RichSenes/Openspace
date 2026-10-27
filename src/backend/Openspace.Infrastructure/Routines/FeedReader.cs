using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

/// <summary>Reads RSS 2.0 and Atom feeds; no DTDs, no external entities.</summary>
internal sealed partial class FeedReader(HttpClient http) : IFeedReader
{
    private const int MaxSummary = 1500;

    public async Task<IReadOnlyList<FeedItem>> ReadAsync(Uri feed, string source, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(feed, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return Parse(stream, feed, source);
    }

    internal static IReadOnlyList<FeedItem> Parse(Stream stream, Uri feed, string source)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        using var reader = XmlReader.Create(stream, settings);
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException($"{source}: the feed is empty.");
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace content = "http://purl.org/rss/1.0/modules/content/";

        if (root.Name == atom + "feed")
        {
            return
            [
                .. root.Elements(atom + "entry").Select(e => Item(
                    source,
                    (string?)e.Element(atom + "title"),
                    e.Elements(atom + "link").FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value,
                    (string?)e.Element(atom + "published") ?? (string?)e.Element(atom + "updated"),
                    (string?)e.Element(atom + "summary") ?? (string?)e.Element(atom + "content"),
                    feed)).OfType<FeedItem>(),
            ];
        }

        return
        [
            .. root.Descendants("item").Select(i => Item(
                source,
                (string?)i.Element("title"),
                (string?)i.Element("link") ?? (string?)i.Element("guid"),
                (string?)i.Element("pubDate") ?? (string?)i.Element(XName.Get("date", "http://purl.org/dc/elements/1.1/")),
                (string?)i.Element("description") ?? (string?)i.Element(content + "encoded"),
                feed)).OfType<FeedItem>(),
        ];
    }

    private static FeedItem? Item(string source, string? title, string? link, string? date, string? summary, Uri feed)
    {
        var clean = Text(title);
        if (string.IsNullOrWhiteSpace(clean) || !Uri.TryCreate(feed, link?.Trim(), out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var text = Text(summary);
        return new FeedItem(source, clean, uri, Date(date), text.Length <= MaxSummary ? text : string.Concat(text.AsSpan(0, MaxSummary), "…"));
    }

    /// <summary>Plain text out of a feed's HTML: tags dropped, entities decoded, spaces collapsed.</summary>
    internal static string Text(string? html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    private static DateTimeOffset? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // RSS dates are RFC 822 ("Tue, 27 Oct 2026 07:00:00 +0100"), sometimes with a zone name.
        var trimmed = ZoneName().Replace(text.Trim(), "+0000");
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            || DateTimeOffset.TryParseExact(trimmed, "ddd, d MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : null;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s(GMT|UTC|UT|Z)$")]
    private static partial Regex ZoneName();
}
