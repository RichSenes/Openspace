using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

/// <summary>The Japan Dev reader on made-up pages packed the way the site packs them: nothing is fetched from the site.</summary>
public sealed class JapanDevBoardTests
{
    private static JsonObject Hit(string slug, string title, string japanese, string visa, string location, object? salaryMin = null, bool internship = false) => new()
    {
        ["id"] = "1",
        ["title"] = title,
        ["slug"] = slug,
        ["company_name"] = "Acme Japan",
        ["company"] = new JsonObject { ["name"] = "Acme Japan", ["slug"] = "acme" },
        ["location"] = "Kyoto",
        ["japanese_level_enum"] = japanese,
        ["sponsors_visas"] = visa,
        ["candidate_location"] = location,
        ["salary_min"] = salaryMin is null ? null : JsonValue.Create(salaryMin),
        ["salary_max"] = salaryMin is null ? null : 9_000_000,
        ["seniority_level"] = "seniority_level_mid_level",
        ["remote_level"] = "remote_level_partial",
        ["skill_names"] = new JsonArray("Web / Full-stack", "C#"),
        ["job_type_names"] = new JsonArray("Engineering"),
        ["is_internship"] = internship,
        ["published_at"] = "2026-03-01T09:00:00.000Z",
    };

    /// <summary>A list page whose payload holds three made-up jobs, one of them an internship, as the site's Nuxt packs them.</summary>
    private static string ListPage()
    {
        var state = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["jobs"] = new JsonObject
                {
                    ["hits"] = new JsonArray(
                        Hit("acme-dotnet-a1", "C# Developer", "japanese_level_not_required", "sponsors_visas_yes", "candidate_location_anywhere", 6_000_000),
                        Hit("acme-front-b2", "Frontend Engineer & Designer", "japanese_level_business_level", "sponsors_visas_unknown", "candidate_location_japan_only"),
                        Hit("acme-intern-c3", "Intern", "japanese_level_not_required", "sponsors_visas_yes", "candidate_location_anywhere", internship: true)),
                },
            },
        };
        return $"""<html><body><script type="application/json" data-nuxt-data="nuxt-app" id="__NUXT_DATA__">{Pack(state)}</script></body></html>""";
    }

    /// <summary>
    /// Packs a value the way devalue does: one flat array, every value an index into it. The root
    /// is wrapped as shallow-reactive and lists of objects as reactive, as the site's are.
    /// </summary>
    private static string Pack(JsonNode root)
    {
        var flat = new JsonArray { new JsonArray("ShallowReactive", 1) };
        int Add(JsonNode? node, bool wrap = true)
        {
            var at = flat.Count;
            flat.Add(null);
            flat[at] = node switch
            {
                JsonArray list when wrap && list.Count > 0 && list[0] is JsonObject => new JsonArray("Reactive", Add(list, wrap: false)),
                JsonArray list => new JsonArray([.. list.Select(i => (JsonNode?)JsonValue.Create(Add(i)))]),
                JsonObject map => new JsonObject(map.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(Add(p.Value))))),
                _ => node?.DeepClone(),
            };
            return at;
        }

        Add(root);
        return flat.ToJsonString();
    }

    [Fact]
    public void The_list_page_s_payload_becomes_offers_without_internships()
    {
        var offers = JapanDevBoard.Offers(ListPage());

        Assert.Equal(2, offers.Count);
        var first = offers[0];
        Assert.Equal(new Uri("https://japan-dev.com/jobs/acme/acme-dotnet-a1"), first.Url);
        Assert.Equal("japan-dev", first.Board);
        Assert.Equal("Acme Japan", first.Company);
        Assert.Equal("C# Developer", first.Title);
        Assert.Equal("Kyoto", first.Location);
        Assert.Equal(JapaneseLevel.None, first.Japanese);
        Assert.True(first.SponsorsVisa);
        Assert.True(first.FromAbroad);
        Assert.Equal(6_000_000, first.SalaryMin);
        Assert.Equal(9_000_000, first.SalaryMax);
        Assert.Equal("mid_level", first.Seniority);
        Assert.Equal("partial", first.Remote);
        Assert.Equal(["Web / Full-stack", "C#", "Engineering"], first.Tags);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero), first.Posted);

        var second = offers[1];
        Assert.Equal(JapaneseLevel.Business, second.Japanese);
        Assert.Null(second.SponsorsVisa);
        Assert.False(second.FromAbroad);
        Assert.Null(second.SalaryMin);
    }

    [Fact]
    public void A_page_without_a_payload_or_with_a_broken_one_gives_no_offers()
    {
        Assert.Empty(JapanDevBoard.Offers("<html><body>Maintenance</body></html>"));
        Assert.Empty(JapanDevBoard.Offers("""<script id="__NUXT_DATA__">[[1,2],[0]]</script>"""));
    }

    [Fact]
    public async Task An_offer_s_text_comes_from_its_schema_org_posting_as_plain_text()
    {
        var page = """
            <html><head>
            <script type="application/ld+json">{"@type":"Organization","name":"Acme"}</script>
            <script type="application/ld+json">{"@type":"JobPosting","title":"C# Developer","description":"<p><strong>About us</strong><br>We build &amp; ship.</p><ul><li>C#</li></ul>"}</script>
            </head></html>
            """;
        using var http = new HttpClient(new Pages(new() { ["/jobs/acme/acme-dotnet-a1"] = page, ["/jobs"] = ListPage() }));
        var board = new JapanDevBoard(http);

        var offers = await board.LatestAsync(default);
        var text = await board.DetailsAsync(offers[0], default);

        Assert.Equal(2, offers.Count);
        Assert.Equal("About us\nWe build & ship.\nC#", text);
    }

    private sealed class Pages(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsolutePath, out var page)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page, Encoding.UTF8, "text/html") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
