using System.Globalization;
using System.Net;
using System.Text.Json;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Google;

namespace Openspace.Infrastructure.Routines;

/// <summary>
/// Companies' own job pages, through the public APIs their hiring services publish so that
/// anyone may list the openings: Greenhouse, Lever, Ashby and Workable. A board is named by
/// service and company, "lever:acme"; one call gives the offers with their full text.
/// </summary>
internal sealed class CompanyBoards(HttpClient http) : IJobBoard
{
    /// <summary>Of each offer's text, what routines read at most.</summary>
    private const int MostText = 8000;

    private static readonly string[] Services = ["greenhouse", "lever", "ashby", "workable"];

    public bool Reads(string board) => Split(board) is { } named && Services.Contains(named.Service);

    public async Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken)
    {
        var (service, company) = Split(board) ?? throw new ArgumentException($"Not a company board: {board}.", nameof(board));
        var slug = Uri.EscapeDataString(company);
        var uri = service switch
        {
            "greenhouse" => $"https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true",
            "lever" => $"https://api.lever.co/v0/postings/{slug}?mode=json",
            "ashby" => $"https://api.ashbyhq.com/posting-api/job-board/{slug}?includeCompensation=true",
            _ => $"https://apply.workable.com/api/v1/widget/accounts/{slug}?details=true",
        };
        using var json = JsonDocument.Parse(await http.GetStringAsync(new Uri(uri), cancellationToken).ConfigureAwait(false));
        var key = $"{service}:{company}";
        var offers = service switch
        {
            "greenhouse" => Greenhouse(key, company, json.RootElement),
            "lever" => Lever(key, company, json.RootElement),
            "ashby" => Ashby(key, company, json.RootElement),
            _ => Workable(key, company, json.RootElement),
        };
        return [.. offers.Where(o => o.Title.Length > 0).DistinctBy(o => o.Url)];
    }

    public Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return Task.FromResult(offer.Text ?? string.Empty);
    }

    private static (string Service, string Company)? Split(string board)
    {
        var at = board.IndexOf(':', StringComparison.Ordinal);
        return at > 0 && at < board.Length - 1 ? (board[..at].Trim().ToLowerInvariant(), board[(at + 1)..].Trim()) : null;
    }

    internal static IEnumerable<JobOffer> Greenhouse(string board, string company, JsonElement root)
    {
        foreach (var job in Array(root, "jobs"))
        {
            var title = Text(job, "title");
            var place = job.TryGetProperty("location", out var location) ? Text(location, "name") : string.Empty;
            var text = Plain(WebUtility.HtmlDecode(Text(job, "content")));
            yield return Offer(
                board, Text(job, "absolute_url"), Text(job, "company_name") is { Length: > 0 } name ? name : company, title, place,
                Text(job, "language") == "ja", Date(Text(job, "first_published")) ?? Date(Text(job, "updated_at")), null, null, text);
        }
    }

    internal static IEnumerable<JobOffer> Lever(string board, string company, JsonElement root)
    {
        foreach (var job in root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : new List<JsonElement>())
        {
            var categories = job.TryGetProperty("categories", out var c) ? c : default;
            var places = Array(categories, "allLocations").Select(l => l.GetString() ?? string.Empty).Prepend(Text(categories, "location")).Where(p => p.Length > 0).Distinct();
            var lists = string.Join("\n\n", Array(job, "lists").Select(l => $"{Text(l, "text")}\n{Plain(Text(l, "content"))}"));
            var text = string.Join("\n\n", new[] { Text(job, "descriptionPlain"), lists, Text(job, "additionalPlain") }.Where(t => t.Length > 0));
            long? min = null, max = null;
            if (job.TryGetProperty("salaryRange", out var salary) && salary.ValueKind == JsonValueKind.Object
                && Text(salary, "currency") == "JPY" && Text(salary, "interval").Contains("year", StringComparison.OrdinalIgnoreCase))
            {
                min = Number(salary, "min");
                max = Number(salary, "max");
            }

            yield return Offer(
                board, Text(job, "hostedUrl"), company, Text(job, "text"), string.Join(", ", places), false,
                Number(job, "createdAt") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null, min, max, text, Text(job, "workplaceType"));
        }
    }

    internal static IEnumerable<JobOffer> Ashby(string board, string company, JsonElement root)
    {
        foreach (var job in Array(root, "jobs"))
        {
            if (job.TryGetProperty("isListed", out var listed) && listed.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var places = Array(job, "secondaryLocations").Select(l => Text(l, "location")).Prepend(Text(job, "location")).Where(p => p.Length > 0).Distinct();
            long? min = null, max = null;
            foreach (var part in job.TryGetProperty("compensation", out var pay) ? Array(pay, "summaryComponents") : [])
            {
                if (Text(part, "compensationType") == "Salary" && Text(part, "currencyCode") == "JPY" && Text(part, "interval").Contains("YEAR", StringComparison.OrdinalIgnoreCase))
                {
                    min = Number(part, "minValue");
                    max = Number(part, "maxValue");
                }
            }

            yield return Offer(
                board, Text(job, "jobUrl"), company, Text(job, "title"), string.Join(", ", places), false, Date(Text(job, "publishedAt")), min, max,
                Text(job, "descriptionPlain"), Text(job, "workplaceType"));
        }
    }

    internal static IEnumerable<JobOffer> Workable(string board, string company, JsonElement root)
    {
        var name = Text(root, "name") is { Length: > 0 } n ? n : company;
        foreach (var job in Array(root, "jobs"))
        {
            var place = string.Join(", ", new[] { Text(job, "city"), Text(job, "state"), Text(job, "country") }.Where(p => p.Length > 0).Distinct());
            var remote = job.TryGetProperty("telecommuting", out var t) && t.ValueKind == JsonValueKind.True ? "remote" : string.Empty;
            yield return Offer(
                board, Text(job, "url"), name, Text(job, "title"), place, false, Date(Text(job, "published_on")), null, null, Plain(Text(job, "description")), remote);
        }
    }

    /// <summary>What every service gives the same way: Japanese when the posting or its title is in Japanese, the rest left for the local model to read.</summary>
    private static JobOffer Offer(
        string board, string url, string company, string title, string place, bool japanesePosting, DateTimeOffset? posted, long? min, long? max, string text, string remote = "")
    {
        title = title.Trim();
        return new JobOffer(
            board,
            Uri.TryCreate(url, UriKind.Absolute, out var link) ? link : new Uri($"https://{board.Replace(':', '.')}.invalid/"),
            company.Trim(),
            title,
            place.Length > 0 ? place : "Not stated",
            japanesePosting || JapanPlaces.WrittenInJapanese(title) ? JapaneseLevel.Business : JapaneseLevel.Unknown,
            null,
            null,
            min > 0 ? min : null,
            max > 0 ? max : null,
            string.Empty,
            remote.ToLowerInvariant(),
            [],
            posted,
            JapanPlaces.Mentions(place),
            text.Length <= MostText ? text : text[..MostText]);
    }

    private static List<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? [.. list.EnumerateArray()] : [];

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : string.Empty;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) ? (long)n : null;

    private static DateTimeOffset? Date(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    private static string Plain(string html) => html.Length == 0 ? html : GmailMailbox.Plain(html);
}
