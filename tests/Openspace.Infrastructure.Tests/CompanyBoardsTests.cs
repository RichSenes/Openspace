using System.Net;
using System.Text;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

/// <summary>Companies' job pages through made-up answers in each hiring service's own shape: nothing is fetched.</summary>
public sealed class CompanyBoardsTests
{
    private const string Greenhouse = """
        {"jobs":[
          {"id":1,"title":"Frontend Engineer","absolute_url":"https://job-boards.greenhouse.example/acme/jobs/1","location":{"name":"Hybrid"},
           "company_name":"Acme Pay","language":"en","first_published":"2026-03-01T09:00:00-05:00",
           "content":"&lt;p&gt;We build &amp;amp; ship.&lt;/p&gt;&lt;ul&gt;&lt;li&gt;React&lt;/li&gt;&lt;/ul&gt;"},
          {"id":2,"title":"セキュリティエンジニア","absolute_url":"https://job-boards.greenhouse.example/acme/jobs/2","location":{"name":"Tokyo"},
           "company_name":"Acme Pay","language":"ja","content":""}]}
        """;

    private const string Lever = """
        [{"text":"Backend Engineer","hostedUrl":"https://jobs.lever.example/acme/abc","createdAt":1772355600000,"workplaceType":"hybrid",
          "categories":{"location":"Tokyo","allLocations":["Tokyo","Kyoto"]},
          "descriptionPlain":"About us.","lists":[{"text":"What you will do","content":"<li>Write C#</li><li>Ship</li>"}],"additionalPlain":"Visa support.",
          "salaryRange":{"currency":"JPY","interval":"per-year-salary","min":6000000,"max":9000000}},
         {"text":"Designer","hostedUrl":"https://jobs.lever.example/acme/def","categories":{"location":"London"},"salaryRange":{"currency":"GBP","interval":"per-year-salary","min":50000,"max":60000}}]
        """;

    private const string Ashby = """
        {"jobs":[
          {"title":"Software Engineer","jobUrl":"https://jobs.ashby.example/acme/1","location":"Tokyo, Japan","secondaryLocations":[{"location":"Remote, Japan"}],
           "publishedAt":"2026-03-01T09:00:00.000+00:00","isListed":true,"workplaceType":"Remote","descriptionPlain":"Python and Django.",
           "compensation":{"summaryComponents":[{"compensationType":"Salary","interval":"1 YEAR","currencyCode":"JPY","minValue":7000000,"maxValue":10000000}]}},
          {"title":"Hidden Engineer","jobUrl":"https://jobs.ashby.example/acme/2","location":"Tokyo","isListed":false}]}
        """;

    private const string Workable = """
        {"name":"Acme Robotics","jobs":[
          {"title":"Robotics Software Engineer","url":"https://apply.workable.example/j/ABC","city":"Koto City","state":"Tokyo","country":"Japan",
           "published_on":"2026-03-01","telecommuting":false,"description":"<p>ROS and C++.</p>"}]}
        """;

    private static async Task<IReadOnlyList<JobOffer>> ReadAsync(string board, string host, string answer)
    {
        using var http = new HttpClient(new Answer(host, answer));
        return await new CompanyBoards(http).LatestAsync(board, default);
    }

    [Fact]
    public async Task Greenhouse_gives_the_text_and_tells_postings_written_in_japanese()
    {
        var offers = await ReadAsync("greenhouse:acme", "boards-api.greenhouse.io", Greenhouse);

        Assert.Equal(2, offers.Count);
        var front = offers[0];
        Assert.Equal("greenhouse:acme", front.Board);
        Assert.Equal("Acme Pay", front.Company);
        Assert.Equal(new Uri("https://job-boards.greenhouse.example/acme/jobs/1"), front.Url);
        Assert.Equal("We build & ship.\nReact", front.Text);
        Assert.Equal(JapaneseLevel.Unknown, front.Japanese);
        Assert.Null(front.SponsorsVisa);
        Assert.False(front.InJapan);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.Zero), front.Posted);
        Assert.Equal(JapaneseLevel.Business, offers[1].Japanese);
        Assert.True(offers[1].InJapan);
    }

    [Fact]
    public async Task Lever_joins_the_parts_of_the_text_and_keeps_only_yearly_salaries_in_yen()
    {
        var offers = await ReadAsync("lever:acme", "api.lever.co", Lever);

        var back = offers[0];
        Assert.Equal("Tokyo, Kyoto", back.Location);
        Assert.True(back.InJapan);
        Assert.Equal("About us.\n\nWhat you will do\nWrite C#\nShip\n\nVisa support.", back.Text);
        Assert.Equal((6_000_000L, 9_000_000L), (back.SalaryMin!.Value, back.SalaryMax!.Value));
        Assert.Equal("hybrid", back.Remote);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1772355600000), back.Posted);
        Assert.False(offers[1].InJapan);
        Assert.Null(offers[1].SalaryMin);
    }

    [Fact]
    public async Task Ashby_gives_places_and_pay_and_leaves_out_unlisted_jobs()
    {
        var offer = Assert.Single(await ReadAsync("ashby:acme", "api.ashbyhq.com", Ashby));

        Assert.Equal("Tokyo, Japan, Remote, Japan", offer.Location);
        Assert.Equal(10_000_000, offer.SalaryMax);
        Assert.Equal("Python and Django.", offer.Text);
        Assert.Equal("remote", offer.Remote);
    }

    [Fact]
    public async Task Workable_names_the_company_and_the_place_from_its_parts()
    {
        var offer = Assert.Single(await ReadAsync("workable:acme", "apply.workable.com", Workable));

        Assert.Equal("Acme Robotics", offer.Company);
        Assert.Equal("Koto City, Tokyo, Japan", offer.Location);
        Assert.Equal("ROS and C++.", offer.Text);
        Assert.Equal("ROS and C++.", await new CompanyBoards(new HttpClient()).DetailsAsync(offer, default));
    }

    [Fact]
    public async Task Workday_lists_page_by_page_and_reads_each_offer_s_text_when_asked()
    {
        var site = new WorkdaySite();
        using var http = new HttpClient(site);
        var board = new CompanyBoards(http);

        var offers = await board.LatestAsync("workday:acme.wd3/AcmeJapan", default);
        var text = await board.DetailsAsync(offers[0], default);

        Assert.Equal(21, offers.Count);
        var first = offers[0];
        Assert.Equal("workday:acme.wd3/AcmeJapan", first.Board);
        Assert.Equal(new Uri("https://acme.wd3.myworkdayjobs.com/AcmeJapan/job/Tokyo/Software-Engineer-0_JR-0"), first.Url);
        Assert.Equal("Acme Group", first.Company);
        Assert.Equal("Tokyo, Japan", first.Location);
        Assert.True(first.InJapan);
        Assert.Null(first.Text);
        Assert.Equal("We build things.\nC# and TypeScript.", text);
        Assert.Equal(["/wday/cxs/acme/AcmeJapan/jobs offset 0", "/wday/cxs/acme/AcmeJapan/jobs offset 20", "/wday/cxs/acme/AcmeJapan/job/Tokyo/Software-Engineer-0_JR-0"], site.Asked);
    }

    [Fact]
    public async Task A_workday_board_written_wrong_says_how_to_write_it()
    {
        using var http = new HttpClient(new WorkdaySite());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => new CompanyBoards(http).LatestAsync("workday:acme", default));

        Assert.Contains("tenant.pod/site", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("japan-dev", false)]
    [InlineData("workday:acme.wd1/AcmeCareers", true)]
    [InlineData("lever:acme", true)]
    [InlineData("greenhouse:acme", true)]
    [InlineData("teapot:acme", false)]
    [InlineData("lever:", false)]
    public void It_reads_only_the_services_it_knows(string board, bool reads) =>
        Assert.Equal(reads, new CompanyBoards(new HttpClient()).Reads(board));

    [Theory]
    [InlineData("AD/ADAS SIL環境開発エンジニア", true)]
    [InlineData("Backend Engineer / バックエンドエンジニア", false)]
    [InlineData("Senior .NET Developer", false)]
    public void A_title_mostly_in_japanese_is_for_people_who_read_it(string title, bool japanese) =>
        Assert.Equal(japanese, JapanPlaces.WrittenInJapanese(title));

    /// <summary>A made-up Workday site: 21 jobs, twenty a page, and each job's own data.</summary>
    private sealed class WorkdaySite : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (request.Method == HttpMethod.Post)
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                Asked.Add($"{path} offset {offset}");
                var jobs = Enumerable.Range(offset, Math.Max(0, Math.Min(20, 21 - offset))).Select(i => $$"""
                    {"title":"Software Engineer {{i}}","externalPath":"/job/Tokyo/Software-Engineer-{{i}}_JR-{{i}}","locationsText":"Tokyo","bulletFields":["JR-{{i}}","Japan","Acme Group"]}
                    """);
                json = $$"""{"total":21,"jobPostings":[{{string.Join(',', jobs)}}]}""";
            }
            else
            {
                Asked.Add(path);
                json = """{"jobPostingInfo":{"title":"Software Engineer 0","jobDescription":"<p>We build things.</p><p>C# and TypeScript.</p>"}}""";
            }

            return request.RequestUri.Host == "acme.wd3.myworkdayjobs.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Answers one host only, as the service would: anything else is not found.</summary>
    private sealed class Answer(string host, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.Host == host
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
