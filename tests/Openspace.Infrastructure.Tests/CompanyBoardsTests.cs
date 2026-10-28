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

    [Theory]
    [InlineData("japan-dev", false)]
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

    /// <summary>Answers one host only, as the service would: anything else is not found.</summary>
    private sealed class Answer(string host, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.Host == host
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
