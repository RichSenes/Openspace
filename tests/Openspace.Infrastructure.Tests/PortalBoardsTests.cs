using System.Net;
using System.Text;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

/// <summary>TokyoDev's sitemap and Daijob's search through made-up pages in their own shape: nothing is fetched.</summary>
public sealed class PortalBoardsTests
{
    private const string Sitemap = """
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
          <url><loc>https://www.tokyodev.com/jobs/no-japanese-required</loc></url>
          <url><loc>https://www.tokyodev.com/companies/acme-labs/jobs/senior-full-stack-engineer</loc><lastmod>2026-11-15T11:08:19+09:00</lastmod></url>
          <url><loc>https://www.tokyodev.com/companies/kyo/jobs/frontend-engineer</loc></url>
          <url><loc>https://www.tokyodev.com/companies/kyo</loc></url>
        </urlset>
        """;

    private static string DaijobCard(int id, string title, string company, string places, string salary, string japanese) => $"""
        <div class="job-card-wrap"><article class="job-card">
          <header class="job-card__header"><div class="job-card__header-info">
            <div class="fw-bolder fs-14px lh-sm"> <a href="/en/jobs/detail/{id}">{company}</a> </div></div></header>
          <div class="job-card__body">
            <h2 class="job-card__title fw-semibold"> <a href="/en/jobs/detail/{id}" id="_job">{title}</a> </h2>
            <div class="job-card__detail"><dl>
              <dt> <span>Location</span> </dt> <dd> <p class="mb-0">{places}</p> </dd>
              <dt> <span>Salary</span> </dt> <dd> <p class="mb-0"> <a href="/x">{salary}</a> </p> </dd>
              <dt> <span>Japanese Level</span> </dt> <dd> <a href="/y"> {japanese} </a> </dd>
            </dl></div></div>
        </article></div>
        """;

    [Fact]
    public void TokyoDev_s_offers_come_from_its_sitemap_with_company_and_title_from_their_address()
    {
        var offers = TokyoDevBoard.Offers(Sitemap);

        Assert.Equal(2, offers.Count);
        Assert.Equal(("Senior full stack engineer", "Acme Labs", "Japan", true), (offers[0].Title, offers[0].Company, offers[0].Location, offers[0].InJapan));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 11, 8, 19, TimeSpan.FromHours(9)), offers[0].Posted);
        Assert.Equal(new Uri("https://www.tokyodev.com/companies/kyo/jobs/frontend-engineer"), offers[1].Url);
    }

    [Fact]
    public async Task TokyoDev_s_text_is_never_opened_and_the_judge_is_told_so()
    {
        var board = new TokyoDevBoard(new HttpClient(new Fails()));

        Assert.True(board.Reads("TokyoDev"));
        Assert.Equal(TokyoDevBoard.Unread, await board.DetailsAsync(TokyoDevBoard.Offers(Sitemap)[0], default));
    }

    [Fact]
    public void Daijob_s_cards_give_the_place_the_salary_and_the_japanese_asked_for()
    {
        var html = DaijobCard(1, "Full Stack Engineer &amp; Lead", "Acme K.K.", """<a href="/a">Asia</a> <a href="/b">Japan</a> <a href="/c">Tokyo</a> <a href="/d">Shibuya</a>""", "JPY - Japanese Yen JPY 6,500K - JPY 9000K", "Daily Conversation(JLPT Level 3 or N3)")
            + DaijobCard(2, "Support", "Far Away", """<a href="/a">Asia</a> <a href="/m">Malaysia</a>""", "MYR 5K", "Not Required");

        var offers = DaijobBoard.Cards(html, "daijob:engineer");

        var first = offers[0];
        Assert.Equal(("Full Stack Engineer & Lead", "Acme K.K.", "Tokyo, Shibuya"), (first.Title, first.Company, first.Location));
        Assert.Equal((6_500_000L, 9_000_000L), (first.SalaryMin!.Value, first.SalaryMax!.Value));
        Assert.Equal(JapaneseLevel.Conversational, first.Japanese);
        Assert.Equal(new Uri("https://www.daijob.com/en/jobs/detail/1"), first.Url);
        Assert.True(first.InJapan);
        Assert.False(offers[1].InJapan);
        Assert.Null(offers[1].SalaryMin);
        Assert.Equal(JapaneseLevel.None, offers[1].Japanese);
    }

    [Theory]
    [InlineData("Business Level(JLPT Level 2 or N2)", JapaneseLevel.Business)]
    [InlineData("Fluent", JapaneseLevel.Fluent)]
    [InlineData("Native Level(JLPT Level 1 or N1)", JapaneseLevel.Fluent)]
    [InlineData("Basic", JapaneseLevel.Basic)]
    [InlineData("Ask the recruiter", JapaneseLevel.Unknown)]
    public void Daijob_s_words_for_japanese_are_levels(string said, JapaneseLevel level) => Assert.Equal(level, DaijobBoard.Level(said));

    [Fact]
    public async Task Daijob_searches_in_english_and_reads_each_offer_s_posting()
    {
        var answers = new Answers(url => url.AbsolutePath.Contains("search_result", StringComparison.Ordinal)
            ? url.Query.Contains("page=1", StringComparison.Ordinal) ? DaijobCard(7, "Backend Engineer", "Acme", "<a href=\"/c\">Japan</a> <a href=\"/c\">Osaka</a>", "JPY 7000K", "Not Required") : ""
            : """<script type="application/ld+json">{"@type":"JobPosting","description":"&lt;p&gt;C# and visa support.&lt;/p&gt;"}</script>""");
        var board = new DaijobBoard(new HttpClient(answers)) { Pause = TimeSpan.Zero };

        var offers = await board.LatestAsync("daijob:backend engineer", default);
        var text = await board.DetailsAsync(offers[0], default);

        Assert.Single(offers);
        Assert.Contains("keyword=backend%20engineer&job_post_language=1&page=1", answers.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains("C# and visa support.", text, StringComparison.Ordinal);
    }

    private sealed class Fails : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nothing is fetched.");
    }

    private sealed class Answers(Func<Uri, string> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "text/html") });
        }
    }
}
