using System.Globalization;
using System.Text.RegularExpressions;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

/// <summary>
/// TokyoDev, a board of developer jobs in Japan. Its offers' pages turn automatic readers away,
/// so it is read through the sitemap it publishes for anyone: each offer's company and title
/// come from its address, and its text stays unread. The person pastes it in the chat for a
/// full judgement.
/// </summary>
internal sealed partial class TokyoDevBoard(HttpClient http) : IJobBoard
{
    public const string Key = "tokyodev";

    public static readonly Uri Sitemap = new("https://www.tokyodev.com/sitemap.xml");

    /// <summary>What the judge reads in place of the text it may not open.</summary>
    public const string Unread =
        "TokyoDev lets no automatic reader open its offers: only the company and the title are known, from the address. Judge by them, with care, and say that the text is still to read.";

    public bool Reads(string board) => string.Equals(board, Key, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken) =>
        Offers(await http.GetStringAsync(Sitemap, cancellationToken).ConfigureAwait(false));

    public Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken) => Task.FromResult(Unread);

    /// <summary>The offers the sitemap lists: "/companies/acme/jobs/senior-engineer" is Acme's "Senior engineer".</summary>
    internal static List<JobOffer> Offers(string sitemap) =>
    [
        .. Entry().Matches(sitemap)
            .Select(m => (Url: new Uri(m.Groups["url"].Value), Company: m.Groups["company"].Value, Job: m.Groups["job"].Value, Changed: m.Groups["changed"].Value))
            .Select(e => new JobOffer(
                Key, e.Url, Words(e.Company, everyWord: true), Words(e.Job, everyWord: false), "Japan", JapaneseLevel.Unknown, null, null, null, null,
                string.Empty, string.Empty, ["TokyoDev"],
                DateTimeOffset.TryParse(e.Changed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null))
            .DistinctBy(o => o.Url),
    ];

    /// <summary>"senior-software-engineer" as "Senior software engineer"; a company's every word capitalised.</summary>
    private static string Words(string slug, bool everyWord)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select((w, i) => everyWord || i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w);
        return string.Join(' ', words);
    }

    [GeneratedRegex(@"<loc>(?<url>https://www\.tokyodev\.com/companies/(?<company>[a-z0-9-]+)/jobs/(?<job>[a-z0-9-]+))</loc>\s*(?:<lastmod>(?<changed>[^<]+)</lastmod>)?")]
    private static partial Regex Entry();
}
