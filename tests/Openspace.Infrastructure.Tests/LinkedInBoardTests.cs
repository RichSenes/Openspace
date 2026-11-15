using System.Net;
using System.Text;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

/// <summary>LinkedIn's public job search through made-up pages in its own shape: nothing is fetched.</summary>
public sealed class LinkedInBoardTests
{
    private static string Card(long id, string title, string company, string place, string date) => $"""
        <li>
          <div class="base-card base-search-card job-search-card" data-entity-urn="urn:li:jobPosting:{id}" data-row="1">
            <a class="base-card__full-link" href="https://jp.linkedin.example/jobs/view/x-{id}?position=1&amp;trackingId=abc"><span class="sr-only">{title}</span></a>
            <div class="base-search-card__info">
              <h3 class="base-search-card__title">
                  {title}
              </h3>
              <h4 class="base-search-card__subtitle">
                <a class="hidden-nested-link" href="https://jp.linkedin.example/company/acme">  {company}  </a>
              </h4>
              <div class="base-search-card__metadata">
                <span class="job-search-card__location">
                  {place}
                </span>
                <time class="job-search-card__listdate" datetime="{date}">2 days ago</time>
              </div>
            </div>
          </div>
        </li>
        """;

    private const string Posting = """
        <section class="core-section-container">
          <div class="show-more-less-html__markup show-more-less-html__markup--clamp-after-5">
            <strong>About us</strong><br><br>We build trams &amp; more.<ul><li>C# and .NET</li><li>React</li></ul><p>Visa support.</p>
          </div>
          <ul class="description__job-criteria-list">
            <li class="description__job-criteria-item"><h3 class="description__job-criteria-subheader">Seniority level</h3>
              <span class="description__job-criteria-text description__job-criteria-text--criteria">Mid-Senior level</span></li>
            <li class="description__job-criteria-item"><h3 class="description__job-criteria-subheader">Employment type</h3>
              <span class="description__job-criteria-text description__job-criteria-text--criteria">Full-time</span></li>
          </ul>
        </section>
        """;

    [Theory]
    [InlineData("linkedin:full stack developer", "full stack developer", "Japan")]
    [InlineData("linkedin:react @ Osaka", "react", "Osaka")]
    [InlineData("LinkedIn:.NET@", ".NET", "Japan")]
    public void A_board_is_a_search_and_a_place_japan_unless_it_says(string board, string keywords, string place) =>
        Assert.Equal((keywords, place), LinkedInBoard.Query(board));

    [Fact]
    public void It_reads_only_searches_with_words()
    {
        var board = new LinkedInBoard(new HttpClient());
        Assert.True(board.Reads("linkedin:frontend"));
        Assert.False(board.Reads("linkedin:  "));
        Assert.False(board.Reads("japan-dev"));
    }

    [Fact]
    public void A_page_of_results_gives_offers_with_their_plain_public_link_and_whether_they_are_in_japan()
    {
        var html = Card(101, "Full Stack Engineer_Fully Remote", "Acme &amp; Co", "Osaka, Osaka, Japan", "2026-11-14")
            + Card(102, "Backend Engineer", "Faraway", "Berlin, Germany", "2026-11-13")
            + Card(103, "", "Nameless", "Tokyo", "2026-11-13")
            + Card(104, "フルスタックエンジニア（リモート可）", "Acme", "Tokyo, Japan", "2026-11-13");

        var offers = LinkedInBoard.Cards(html, "linkedin:full stack");

        Assert.Equal(3, offers.Count);
        Assert.Equal(JapaneseLevel.Business, offers[2].Japanese);
        var first = offers[0];
        Assert.Equal(("Full Stack Engineer_Fully Remote", "Acme & Co", "Osaka"), (first.Title, first.Company, first.Location));
        Assert.Equal(new Uri("https://www.linkedin.com/jobs/view/101/"), first.Url);
        Assert.Equal(("linkedin:full stack", "remote", true), (first.Board, first.Remote, first.InJapan));
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), first.Posted);
        Assert.Equal(JapaneseLevel.Unknown, first.Japanese);
        Assert.False(offers[1].InJapan);
    }

    [Fact]
    public void An_offer_s_page_gives_what_linkedin_says_of_it_and_its_description_as_lines()
    {
        var text = LinkedInBoard.Details(Posting);

        Assert.StartsWith("Seniority level: Mid-Senior level\nEmployment type: Full-time\n\nAbout us\nWe build trams & more.\nC# and .NET\nReact\nVisa support.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_reads_pages_of_a_search_in_japan_until_one_is_short_and_each_offer_from_its_id()
    {
        var full = string.Concat(Enumerable.Range(1, 25).Select(i => Card(i, $"Engineer {i}", "Acme", "Tokyo, Japan", "2026-11-14")));
        var answers = new Answers(url => url.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)
            ? (url.Query.Contains("start=0", StringComparison.Ordinal) ? full : Card(200, "Last Engineer", "Acme", "Kyoto, Japan", "2026-11-12"))
            : Posting);
        var board = new LinkedInBoard(new HttpClient(answers)) { Pause = TimeSpan.Zero };

        var offers = await board.LatestAsync("linkedin:full stack@Kyoto", default);
        var details = await board.DetailsAsync(offers[^1], default);

        Assert.Equal(26, offers.Count);
        Assert.Equal(3, answers.Asked.Count);
        Assert.Contains("keywords=full%20stack&location=Kyoto&f_TPR=r604800&start=25", answers.Asked[1].Query, StringComparison.Ordinal);
        Assert.Equal("https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/200", answers.Asked[2].GetLeftPart(UriPartial.Path));
        Assert.Contains("Visa support.", details, StringComparison.Ordinal);
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
