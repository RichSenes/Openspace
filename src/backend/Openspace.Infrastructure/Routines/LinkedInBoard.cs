using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

/// <summary>
/// LinkedIn's job search as anyone who is not signed in sees it: the results of a search in
/// Japan and each offer's public page, honestly identified, with no account. A board is a
/// search, "linkedin:full stack developer", or a search in a place, "linkedin:react@Osaka".
/// It reads a few pages of the last week's results, pausing between requests.
/// </summary>
internal sealed partial class LinkedInBoard(HttpClient http) : IJobBoard
{
    public const string Prefix = "linkedin:";

    private static readonly Uri Search = new("https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search");

    private static readonly Uri Posting = new("https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/");

    /// <summary>Results LinkedIn gives per page, and the pages read per search.</summary>
    private const int PageSize = 25;

    private const int Pages = 3;

    /// <summary>Of each offer's text, what routines read at most.</summary>
    private const int MostText = 8000;

    /// <summary>The pause between two requests, to read like a person and not a crawler.</summary>
    internal TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(2);

    public bool Reads(string board) => board.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && Query(board).Keywords.Length > 0;

    public async Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken)
    {
        var (keywords, place) = Query(board);
        var offers = new List<JobOffer>();
        for (var page = 0; page < Pages; page++)
        {
            if (page > 0)
            {
                await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
            }

            var url = new Uri($"{Search}?keywords={Uri.EscapeDataString(keywords)}&location={Uri.EscapeDataString(place)}&f_TPR=r604800&start={page * PageSize}");
            var found = Cards(await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false), board);
            offers.AddRange(found);
            if (found.Count < PageSize / 2)
            {
                break;
            }
        }

        return [.. offers.DistinctBy(o => o.Url)];
    }

    public async Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (IdOf(offer.Url) is not { } id)
        {
            return string.Empty;
        }

        await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
        var text = Details(await http.GetStringAsync(new Uri(Posting, id), cancellationToken).ConfigureAwait(false));
        return text.Length <= MostText ? text : text[..MostText];
    }

    /// <summary>"linkedin:react@Osaka": the words searched and the place, Japan when none is given.</summary>
    internal static (string Keywords, string Place) Query(string board)
    {
        var query = board[Prefix.Length..];
        var at = query.LastIndexOf('@');
        return at < 0 ? (query.Trim(), "Japan") : (query[..at].Trim(), query[(at + 1)..].Trim() is { Length: > 0 } place ? place : "Japan");
    }

    /// <summary>The offers of a page of results; their link is the offer's plain public page.</summary>
    internal static List<JobOffer> Cards(string html, string board)
    {
        var offers = new List<JobOffer>();
        foreach (Match card in Card().Matches(html))
        {
            var body = card.Groups["body"].Value;
            var title = Clean(Title().Match(body).Groups[1].Value);
            var company = Clean(Company().Match(body).Groups[1].Value);
            if (title.Length == 0)
            {
                continue;
            }

            var location = Clean(Place().Match(body).Groups[1].Value);
            DateTimeOffset? posted = DateTimeOffset.TryParse(Posted().Match(body).Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null;
            var remote = title.Contains("remote", StringComparison.OrdinalIgnoreCase) || title.Contains("リモート", StringComparison.Ordinal) ? "remote" : string.Empty;
            // A title written mostly in Japanese is for people who read it, as on the other boards.
            offers.Add(new JobOffer(
                board, new Uri($"https://www.linkedin.com/jobs/view/{card.Groups["id"].Value}/"), company.Length > 0 ? company : "LinkedIn", title, JapanPlaces.Tidy(location),
                JapanPlaces.WrittenInJapanese(title) ? JapaneseLevel.Business : JapaneseLevel.Unknown, null, null, null, null, string.Empty, remote, [], posted,
                JapanPlaces.Mentions(location)));
        }

        return offers;
    }

    /// <summary>An offer's public page as plain text: what LinkedIn says of it first (seniority, kind), then its description.</summary>
    internal static string Details(string html)
    {
        var criteria = Criteria().Matches(html).Select(m => $"{Clean(m.Groups[1].Value)}: {Clean(m.Groups[2].Value)}");
        var description = Description().Match(html) is { Success: true } m ? Plain(m.Groups[1].Value) : string.Empty;
        return string.Join('\n', criteria.Append(string.Empty).Append(description)).Trim();
    }

    private static string? IdOf(Uri url) => JobId().Match(url.AbsolutePath) is { Success: true } m ? m.Groups[1].Value : null;

    private static string Clean(string text) => WebUtility.HtmlDecode(Spaces().Replace(Tags().Replace(text, " "), " ")).Trim();

    /// <summary>HTML as lines: paragraphs, breaks and list items each on their own.</summary>
    private static string Plain(string html)
    {
        var lines = Breaks().Replace(html, "\n");
        lines = WebUtility.HtmlDecode(Tags().Replace(lines, " "));
        return string.Join('\n', lines.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0));
    }

    [GeneratedRegex(@"data-entity-urn=""urn:li:jobPosting:(?<id>\d+)""(?<body>.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex Card();

    [GeneratedRegex(@"base-search-card__title"">(.*?)</h3>", RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"base-search-card__subtitle"">(.*?)</h4>", RegexOptions.Singleline)]
    private static partial Regex Company();

    [GeneratedRegex(@"job-search-card__location"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex Place();

    [GeneratedRegex(@"<time[^>]*datetime=""([^""]+)""")]
    private static partial Regex Posted();

    [GeneratedRegex(@"description__job-criteria-subheader"">(.*?)</h3>\s*<span[^>]*>(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex Criteria();

    [GeneratedRegex(@"show-more-less-html__markup[^>]*>(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex Description();

    [GeneratedRegex(@"/jobs/view/(?:[^/]*-)?(\d+)")]
    private static partial Regex JobId();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"<br\s*/?>|</p>|</li>|<li[^>]*>|</h\d>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();
}
