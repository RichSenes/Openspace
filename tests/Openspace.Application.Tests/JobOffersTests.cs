using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The job offers routine on a made-up board and profile: no real site, no real model.</summary>
public sealed class JobOffersTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly string _profile = Path.Combine(Path.GetTempPath(), $"openspace-profile-{Guid.NewGuid():N}.md");
    private readonly FakeBoard _board = new();
    private readonly FakeChat _chat = new();

    public JobOffersTests() => File.WriteAllText(_profile, "A C# developer who likes clean code. Wants to live in Kyoto.");

    public void Dispose() => File.Delete(_profile);

    private JobOffers Kind() => new([_board], _chat);

    private Routine Create(string language = "English", string profile = "", int most = 8)
    {
        var settings = $$"""
            {"title":"Offers","language":"{{language}}","channel":"CAREER","profile":{{JsonSerializer.Serialize(profile.Length > 0 ? profile : _profile)}},
             "boards":["made-up"],"visa":true,"japaneseUpTo":"conversational","minSalary":4500000,"cities":["Kyoto","Osaka"],"most":{{most}},"fit":3}
            """;
        var routine = Routine.Create(_h.Office, "Offers", "job-offers", RoutineSchedule.Daily(new TimeOnly(8, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [Kind()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.OrderBy(r => r.StartedAt).Last();
    }

    private ProposalCommands Proposals() => new(_h.Routines, [Kind()], _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<ProposalCommands>.Instance);

    private static JobOffer Offer(
        string slug, string title, string company = "Acme", string location = "Tokyo", JapaneseLevel japanese = JapaneseLevel.None,
        bool? visa = true, bool? abroad = true, long? min = 6_000_000, long? max = 8_000_000, int hoursAgo = 1) =>
        new("made-up", new Uri($"https://jobs.example/jobs/{slug}?ref=list"), company, title, location, japanese, visa, abroad, min, max,
            "mid_level", "partial", ["Web / Full-stack"], Harness.Start.AddHours(-hoursAgo));

    /// <summary>Two good offers (one in Kyoto), one that fits poorly, and four the person's rules leave out.</summary>
    private void Board()
    {
        _board.Offers.AddRange([
            Offer("acme-dotnet", "Senior .NET Developer", hoursAgo: 1),
            Offer("kyo-front", "Frontend Engineer", company: "Kyo Labs", location: "Kyoto", min: null, max: null, hoursAgo: 2),
            Offer("acme-sales", "Sales Engineer", hoursAgo: 3),
            Offer("acme-local", "Backend Developer (in Japan only)", abroad: false, hoursAgo: 4),
            Offer("acme-novisa", "Backend Developer (no visa)", visa: false, hoursAgo: 5),
            Offer("acme-fluent", "Full-stack Developer (fluent)", japanese: JapaneseLevel.Business, hoursAgo: 6),
            Offer("acme-cheap", "Junior Developer", min: 3_000_000, max: 4_000_000, hoursAgo: 7),
        ]);
    }

    [Fact]
    public async Task It_keeps_what_fits_the_person_s_rules_and_sends_the_good_ones_the_preferred_city_first()
    {
        var routine = Create();
        Board();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("2 new offers for you, of 3 judged; 3 of 7 fit your rules.", run.Summary);
        Assert.Equal(3, _chat.Asked.Count);
        Assert.All(_chat.Systems, s => Assert.Contains("A C# developer who likes clean code.", s, StringComparison.Ordinal));
        Assert.Contains("Details of Senior .NET Developer.", _chat.Asked[0], StringComparison.Ordinal);
        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(
            """
            Offers: 2 new offers for you

            1. Frontend Engineer — Kyo Labs (Kyoto)
            salary not stated · Japanese not required · visa sponsored · 4/5
            It fits Frontend Engineer.
            To fill in: Some Japanese helps.
            https://jobs.example/jobs/kyo-front?ref=list

            2. Senior .NET Developer — Acme (Tokyo)
            ¥6–8M · Japanese not required · visa sponsored · 5/5
            It fits Senior .NET Developer.
            To fill in: Some Japanese helps.
            https://jobs.example/jobs/acme-dotnet?ref=list

            In Openspace, for each: yes if it interests you, no and it never comes back.
            """.ReplaceLineEndings("\n"),
            text);
        Assert.Equal(2, _h.Routines.Proposals.Count);
        Assert.All(_h.Routines.Proposals, p => Assert.Equal(ProposalStatus.Pending, p.Status));
    }

    [Fact]
    public async Task An_offer_comes_once_even_posted_again_under_another_link_and_back_only_when_it_changes()
    {
        var routine = Create();
        Board();
        await RunAsync(routine);

        _board.Offers.Add(Offer("acme-dotnet-2", "Senior .NET Developer", hoursAgo: 0));
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await RunAsync(routine);
        Assert.Equal("No new offers for you: 4 of 8 fit your rules, 0 new ones judged.", second.Summary);

        _board.Offers[0] = Offer("acme-dotnet", "Senior .NET Developer", max: 10_000_000);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var third = await RunAsync(routine);

        Assert.Equal("1 new offers for you, of 1 judged; 4 of 8 fit your rules.", third.Summary);
        Assert.Contains("¥6–10M", _h.Outbox.Sent[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_drops_an_offer_for_good_and_yes_marks_it_taken_up()
    {
        var routine = Create();
        Board();
        await RunAsync(routine);
        var kyoto = _h.Routines.Proposals.Single(p => p.Title.Contains("Kyo Labs", StringComparison.Ordinal));
        var acme = _h.Routines.Proposals.Single(p => p.Title.Contains("Acme", StringComparison.Ordinal));

        await Proposals().ApproveAsync(kyoto.Id, default);
        await Proposals().RejectAsync(acme.Id, default);
        _board.Offers[0] = Offer("acme-dotnet", "Senior .NET Developer", max: 12_000_000);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var next = await RunAsync(routine);

        Assert.Equal(ProposalStatus.Done, kyoto.Status);
        Assert.Contains("https://jobs.example/jobs/kyo-front", kyoto.Outcome, StringComparison.Ordinal);
        Assert.Equal(SeenState.Taken, _h.Routines.Seen.Single(s => s.Key == "job:jobs.example/jobs/kyo-front").State);
        Assert.Equal(SeenState.Dismissed, _h.Routines.Seen.Single(s => s.Key == "role:acme|senior-net-developer").State);
        Assert.Equal("No new offers for you: 3 of 7 fit your rules, 0 new ones judged.", next.Summary);
    }

    [Fact]
    public async Task A_poor_fit_is_judged_once_and_never_sent()
    {
        var routine = Create();
        _board.Offers.Add(Offer("acme-sales", "Sales Engineer"));

        var first = await RunAsync(routine);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await RunAsync(routine);

        Assert.Equal("No new offers for you: 1 of 1 fit your rules, 1 new ones judged.", first.Summary);
        Assert.Equal("No new offers for you: 1 of 1 fit your rules, 0 new ones judged.", second.Summary);
        Assert.Single(_chat.Asked);
        Assert.Empty(_h.Outbox.Sent);
    }

    [Fact]
    public async Task A_silent_model_fails_the_run_and_the_offers_stay_new()
    {
        var routine = Create();
        Board();
        _chat.Fail = true;

        var first = await RunAsync(routine);
        _chat.Fail = false;
        _h.Clock.Advance(TimeSpan.FromHours(1));
        var second = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Failed, first.Status);
        Assert.Contains("could not judge any offer", first.Summary, StringComparison.Ordinal);
        Assert.Equal("2 new offers for you, of 3 judged; 3 of 7 fit your rules.", second.Summary);
    }

    [Fact]
    public async Task Each_run_judges_at_most_a_few_the_newest_first()
    {
        var routine = Create(most: 1);
        Board();

        await RunAsync(routine);

        Assert.Contains("Senior .NET Developer", Assert.Single(_chat.Asked), StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_speaks_the_person_s_language()
    {
        var routine = Create(language: "Italian");
        Board();

        await RunAsync(routine);

        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.StartsWith("Offers: 2 offerte nuove per te", text, StringComparison.Ordinal);
        Assert.Contains("stipendio non indicato · giapponese non richiesto · visto sponsorizzato · 4/5", text, StringComparison.Ordinal);
        Assert.Contains("Da colmare: Some Japanese helps.", text, StringComparison.Ordinal);
        Assert.Contains("Write in Italian", _chat.Systems[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_profile_it_says_where_to_write_one()
    {
        var run = await RunAsync(Create(profile: "/nowhere/profile.md"));

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("No profile at /nowhere/profile.md", run.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JapaneseLevel.Conversational, true, true, 5_000_000L, true)]
    [InlineData(JapaneseLevel.Business, true, true, 5_000_000L, false)]
    [InlineData(JapaneseLevel.Unknown, true, null, null, true)]
    [InlineData(JapaneseLevel.None, null, true, 9_000_000L, false)]
    [InlineData(JapaneseLevel.None, true, true, 4_000_000L, false)]
    public void The_rules_ask_for_a_visa_little_japanese_and_enough_pay(JapaneseLevel japanese, bool? visa, bool? abroad, long? max, bool passes)
    {
        using var json = JsonDocument.Parse("""{"visa":true,"japaneseUpTo":"conversational","minSalary":4500000}""");
        var settings = JobSettings.Read(json.RootElement);

        Assert.Equal(passes, JobOffers.Passes(settings, Offer("x", "Developer", japanese: japanese, visa: visa, abroad: abroad, min: null, max: max)));
    }

    private sealed class FakeBoard : IJobBoard
    {
        public List<JobOffer> Offers { get; } = [];

        public string Key => "made-up";

        public string Name => "Made-up board";

        public Task<IReadOnlyList<JobOffer>> LatestAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobOffer>>([.. Offers]);

        public Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken) => Task.FromResult($"Details of {offer.Title}.");
    }

    /// <summary>Sales jobs fit poorly; .NET ones best; the rest well.</summary>
    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public List<string> Systems { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The model is busy.");
            }

            Asked.Add(ask.User);
            Systems.Add(ask.System);
            var title = ask.User.Split('\n')[1].Split(" at ")[0];
            var fit = title.Contains("Sales", StringComparison.Ordinal) ? 1 : title.Contains(".NET", StringComparison.Ordinal) ? 5 : 4;
            return Task.FromResult(JsonSerializer.Serialize(new { fit, why = $"It fits {title}.", gaps = "Some Japanese helps." }));
        }
    }
}
