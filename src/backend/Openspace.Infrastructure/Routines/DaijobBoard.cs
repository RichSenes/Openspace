using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

/// <summary>
/// Daijob, a board of bilingual jobs in Japan, searched in English as anyone sees it, honestly
/// identified, where its robots.txt allows: "daijob:software engineer". Each card says the
/// salary and the Japanese asked for; each offer's text is its page's schema.org posting.
/// </summary>
internal sealed partial class DaijobBoard(HttpClient http) : IJobBoard
{
    public const string Prefix = "daijob:";

    private static readonly Uri Home = new("https://www.daijob.com/");

    private const int Pages = 2;

    private const int MostText = 8000;

    /// <summary>The pause between two requests, to read like a person and not a crawler.</summary>
    internal TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(2);

    public bool Reads(string board) => board.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && board[Prefix.Length..].Trim().Length > 0;

    public async Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken)
    {
        var keywords = board[Prefix.Length..].Trim();
        var offers = new List<JobOffer>();
        for (var page = 1; page <= Pages; page++)
        {
            if (page > 1)
            {
                await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
            }

            var url = new Uri(Home, $"en/jobs/search_result?keyword={Uri.EscapeDataString(keywords)}&job_post_language=1&page={page}");
            var found = Cards(await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false), board);
            offers.AddRange(found);
            if (found.Count == 0)
            {
                break;
            }
        }

        return [.. offers.DistinctBy(o => o.Url)];
    }

    public async Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
        var text = JapanDevBoard.Posting(await http.GetStringAsync(offer.Url, cancellationToken).ConfigureAwait(false)) ?? string.Empty;
        return text.Length <= MostText ? text : text[..MostText];
    }

    /// <summary>The offers of a page of results, with their place, salary and Japanese.</summary>
    internal static List<JobOffer> Cards(string html, string board)
    {
        var offers = new List<JobOffer>();
        foreach (Match card in Card().Matches(html))
        {
            var body = card.Value;
            if (Title().Match(body) is not { Success: true } title)
            {
                continue;
            }

            var place = string.Join(", ", Place().Match(body).Groups[1].Value is var links && links.Length > 0
                ? Link().Matches(links).Select(l => Clean(l.Groups[1].Value)).Where(p => p is not ("Asia" or "Japan")).TakeLast(2)
                : []);
            var (min, max) = Salary(Clean(Field("Salary").Match(body).Groups[1].Value));
            offers.Add(new JobOffer(
                board, new Uri(Home, title.Groups["href"].Value), Clean(Company().Match(body).Groups[1].Value), Clean(title.Groups["title"].Value),
                place.Length > 0 ? place : "Japan", Level(Clean(Field("Japanese Level").Match(body).Groups[1].Value)), null, null, min, max,
                string.Empty, string.Empty, [], null, InJapan: body.Contains(">Japan<", StringComparison.Ordinal)));
        }

        return offers;
    }

    /// <summary>"JPY 5500K - JPY 7000K" as yen a year; nothing for other currencies.</summary>
    internal static (long? Min, long? Max) Salary(string text)
    {
        if (!text.Contains("JPY", StringComparison.Ordinal))
        {
            return (null, null);
        }

        var amounts = Thousands().Matches(text).Select(m => long.Parse(m.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture) * 1000).ToList();
        return amounts.Count switch
        {
            0 => (null, null),
            1 => (amounts[0], amounts[0]),
            _ => (amounts[0], amounts[^1]),
        };
    }

    /// <summary>Daijob's words for Japanese, as levels: "Business Level(JLPT Level 2 or N2)" is business.</summary>
    internal static JapaneseLevel Level(string said)
    {
        var text = said.ToUpperInvariant();
        return text switch
        {
            _ when text.Contains("NATIVE", StringComparison.Ordinal) || text.Contains("FLUENT", StringComparison.Ordinal) || text.Contains("N1", StringComparison.Ordinal) => JapaneseLevel.Fluent,
            _ when text.Contains("BUSINESS", StringComparison.Ordinal) || text.Contains("N2", StringComparison.Ordinal) => JapaneseLevel.Business,
            _ when text.Contains("CONVERSATION", StringComparison.Ordinal) || text.Contains("N3", StringComparison.Ordinal) => JapaneseLevel.Conversational,
            _ when text.Contains("BASIC", StringComparison.Ordinal) || text.Contains("BEGINNER", StringComparison.Ordinal) || text.Contains("N4", StringComparison.Ordinal) || text.Contains("N5", StringComparison.Ordinal) => JapaneseLevel.Basic,
            _ when text.Contains("NOT REQUIRED", StringComparison.Ordinal) || text.Contains("NONE", StringComparison.Ordinal) => JapaneseLevel.None,
            _ => JapaneseLevel.Unknown,
        };
    }

    private static string Clean(string text) => WebUtility.HtmlDecode(Spaces().Replace(Tags().Replace(text, " "), " ")).Trim();

    private static Regex Field(string name) => new($@"<span>{Regex.Escape(name)}</span>\s*</dt>\s*<dd>(.*?)</dd>", RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"<article class=""job-card"">.*?</article>", RegexOptions.Singleline)]
    private static partial Regex Card();

    [GeneratedRegex(@"<h2 class=""job-card__title[^""]*"">\s*<a href=""(?<href>/en/jobs/detail/\d+)""[^>]*>(?<title>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"<div class=""fw-bolder[^""]*"">\s*<a [^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Company();

    [GeneratedRegex(@"<span>Location</span>\s*</dt>\s*<dd>(.*?)</dd>", RegexOptions.Singleline)]
    private static partial Regex Place();

    [GeneratedRegex(@"<a [^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Link();

    [GeneratedRegex(@"JPY\s*([\d,]+)K")]
    private static partial Regex Thousands();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
