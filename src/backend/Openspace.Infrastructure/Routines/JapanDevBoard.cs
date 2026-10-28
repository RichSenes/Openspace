using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Google;

namespace Openspace.Infrastructure.Routines;

/// <summary>
/// Japan Dev, a board of jobs in Japan for people who speak English. It reads the public list
/// page and each offer's page, as a browser gets them, honestly identified: the list carries
/// its data in the page (Nuxt's payload), each offer its schema.org posting.
/// </summary>
internal sealed partial class JapanDevBoard(HttpClient http) : IJobBoard
{
    public static readonly Uri Home = new("https://japan-dev.com/");

    private static readonly string[] TagLists = ["skill_names", "job_type_names"];

    /// <summary>Of each offer's text, what routines read at most.</summary>
    private const int MostText = 8000;

    public string Key => "japan-dev";

    public string Name => "Japan Dev";

    public async Task<IReadOnlyList<JobOffer>> LatestAsync(CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync(new Uri(Home, "jobs"), cancellationToken).ConfigureAwait(false);
        return Offers(html);
    }

    public async Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var html = await http.GetStringAsync(offer.Url, cancellationToken).ConfigureAwait(false);
        var text = Posting(html) ?? string.Empty;
        return text.Length <= MostText ? text : text[..MostText];
    }

    /// <summary>The offers in the list page's payload; none when the page has no payload this reader understands.</summary>
    internal static List<JobOffer> Offers(string html)
    {
        if (Payload().Match(html) is not { Success: true } match)
        {
            return [];
        }

        var data = JsonNode.Parse(match.Groups[1].Value)?.AsArray() ?? [];
        var offers = new List<JobOffer>();
        foreach (var hit in Hits(Revive(data, 0, new Dictionary<int, JsonNode?>(), 0)))
        {
            if (Offer(hit) is { } offer)
            {
                offers.Add(offer);
            }
        }

        return [.. offers.DistinctBy(o => o.Url)];
    }

    /// <summary>
    /// Undoes the payload's packing (devalue): every value is an index into one flat array, and a
    /// few arrays are tagged ("Reactive", "Set"). Depth is bounded, so a strange page cannot loop.
    /// </summary>
    private static JsonNode? Revive(JsonArray data, int index, Dictionary<int, JsonNode?> done, int depth)
    {
        if (index < 0 || index >= data.Count || depth > 40)
        {
            return null;
        }

        if (done.TryGetValue(index, out var known))
        {
            return known?.DeepClone();
        }

        var value = data[index];
        JsonNode? revived;
        switch (value)
        {
            case JsonArray list when list.Count > 0 && list[0] is JsonValue tag && tag.TryGetValue<string>(out var name):
                revived = name switch
                {
                    "Reactive" or "ShallowReactive" or "Ref" or "ShallowRef" => list.Count > 1 ? Revive(data, Index(list[1]), done, depth + 1) : null,
                    "Set" => new JsonArray([.. list.Skip(1).Select(i => Revive(data, Index(i), done, depth + 1))]),
                    "Date" => list.Count > 1 ? list[1]?.DeepClone() : null,
                    _ => null,
                };
                break;
            case JsonArray list:
                revived = new JsonArray([.. list.Select(i => Revive(data, Index(i), done, depth + 1))]);
                break;
            case JsonObject map:
                var revivedMap = new JsonObject();
                foreach (var (key, i) in map)
                {
                    revivedMap[key] = Revive(data, Index(i), done, depth + 1);
                }

                revived = revivedMap;
                break;
            default:
                revived = value?.DeepClone();
                break;
        }

        done[index] = revived;
        return revived;
    }

    private static int Index(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : -1;

    /// <summary>Every job in the revived payload: objects with a slug and a word on visas.</summary>
    private static IEnumerable<JsonObject> Hits(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject map when map.ContainsKey("slug") && map.ContainsKey("sponsors_visas"):
                yield return map;
                break;
            case JsonObject map:
                foreach (var (_, child) in map)
                {
                    foreach (var hit in Hits(child))
                    {
                        yield return hit;
                    }
                }

                break;
            case JsonArray list:
                foreach (var child in list)
                {
                    foreach (var hit in Hits(child))
                    {
                        yield return hit;
                    }
                }

                break;
        }
    }

    internal static JobOffer? Offer(JsonObject hit)
    {
        string Text(string name) => hit[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : string.Empty;
        long? Yen(string name) => hit[name] is JsonValue v && v.TryGetValue<long>(out var n) && n > 0 ? n : null;
        string Enum(string name) => Text(name) is { Length: > 0 } value && value.StartsWith(name + "_", StringComparison.Ordinal) ? value[(name.Length + 1)..] : Text(name);

        var slug = Text("slug");
        var company = hit["company"]?["slug"] is JsonValue c && c.TryGetValue<string>(out var companySlug) ? companySlug : string.Empty;
        if (slug.Length == 0 || company.Length == 0 || Text("title").Length == 0 || hit["is_internship"] is JsonValue intern && intern.TryGetValue<bool>(out var isIntern) && isIntern)
        {
            return null;
        }

        var tags = TagLists
            .SelectMany(n => hit[n] is JsonArray list ? list.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null) : [])
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new JobOffer(
            "japan-dev",
            new Uri(Home, $"jobs/{Uri.EscapeDataString(company)}/{Uri.EscapeDataString(slug)}"),
            Text("company_name") is { Length: > 0 } name ? name : company,
            Text("title"),
            Text("location") is { Length: > 0 } location ? location : "Japan",
            Enum("japanese_level_enum") switch
            {
                "japanese_level_not_required" => JapaneseLevel.None,
                "japanese_level_basic" => JapaneseLevel.Basic,
                "japanese_level_conversational" => JapaneseLevel.Conversational,
                "japanese_level_business_level" => JapaneseLevel.Business,
                "japanese_level_fluent" => JapaneseLevel.Fluent,
                "japanese_level_native" => JapaneseLevel.Native,
                _ => JapaneseLevel.Unknown,
            },
            Enum("sponsors_visas") switch { "yes" => true, "no" => false, _ => null },
            Enum("candidate_location") switch { "anywhere" => true, "japan_only" => false, _ => null },
            Yen("salary_min"),
            Yen("salary_max"),
            Enum("seniority_level"),
            Enum("remote_level"),
            tags,
            DateTimeOffset.TryParse(Text("published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var posted) ? posted : null);
    }

    /// <summary>The offer's schema.org posting, as plain text; null when the page has none.</summary>
    internal static string? Posting(string html)
    {
        foreach (Match block in LinkedData().Matches(html))
        {
            try
            {
                using var json = JsonDocument.Parse(block.Groups[1].Value);
                if (json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("@type", out var type) && type.GetString() == "JobPosting"
                    && json.RootElement.TryGetProperty("description", out var description))
                {
                    return GmailMailbox.Plain(description.GetString() ?? string.Empty);
                }
            }
            catch (JsonException)
            {
                // Another block, or one this reader does not understand.
            }
        }

        return null;
    }

    [GeneratedRegex("""<script[^>]*id="__NUXT_DATA__"[^>]*>(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex Payload();

    [GeneratedRegex("""<script[^>]*type="application/ld\+json"[^>]*>(.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex LinkedData();
}
