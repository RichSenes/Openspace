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
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-career-{Guid.NewGuid():N}");
    private readonly FakeBoard _board = new();
    private readonly FakeChat _chat = new();

    public JobOffersTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Profile, "A C# developer who likes clean code. Wants to live in Kyoto.");
    }

    private string Profile => Path.Combine(_root, "profile.md");

    private string Inbox => Path.Combine(_root, "inbox");

    private string Saved => Path.Combine(_root, "offers");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private JobOffers Kind() => new([_board], _chat, _h.Routines, _h.RoutineRequests);

    private Routine Create(string language = "English", string profile = "", int most = 8, string boards = "\"made-up\"")
    {
        var settings = $$"""
            {"title":"Offers","language":"{{language}}","channel":"CAREER","profile":{{JsonSerializer.Serialize(profile.Length > 0 ? profile : Profile)}},"inbox":{{JsonSerializer.Serialize(Inbox)}},"offers":{{JsonSerializer.Serialize(Saved)}},
             "boards":[{{boards}}],"visa":true,"japaneseUpTo":"conversational","minSalary":4500000,"cities":["Kyoto","Osaka"],"most":{{most}},"fit":3}
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
        Assert.Contains("kyo-labs-frontend-engineer", kyoto.Outcome, StringComparison.Ordinal);
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

    /// <summary>A company board, as its reader gives offers: Japanese and visas only in the text.</summary>
    private static JobOffer Posting(string slug, string title, string text, string place = "Hybrid") =>
        new("lever:acme", new Uri($"https://jobs.example/acme/{slug}"), "Acme", title, place,
            JapanPlaces.WrittenInJapanese(title) ? JapaneseLevel.Business : JapaneseLevel.Unknown, null, null, null, null,
            "", "hybrid", [], Harness.Start.AddHours(-1), InJapan: JapanPlaces.Mentions(place), Text: text);

    [Fact]
    public async Task On_a_company_board_the_text_says_the_japanese_and_the_visa_and_the_rules_hold_for_them_too()
    {
        var routine = Create(boards: "{\"board\":\"lever:acme\",\"allInJapan\":true}");
        _board.Company.AddRange([
            Posting("a", "Frontend Engineer", "We sponsor visas. React and TypeScript."),
            Posting("b", "Backend Engineer", "Go and Kubernetes."),
            Posting("c", "Full Stack Engineer", "No visa support. Vue."),
            Posting("d", "Mobile Engineer", "You need fluent Japanese."),
            Posting("e", "Account Executive", "Sell our product."),
            Posting("f", "Backend エンジニア開発チーム", "日本語のみ。"),
        ]);

        var run = await RunAsync(routine);

        Assert.Equal("2 new offers for you, of 4 judged; 4 of 6 fit your rules.", run.Summary);
        var text = _h.Outbox.Sent.Single().Text;
        Assert.Contains("Frontend Engineer — Acme (Hybrid)\nsalary not stated · Japanese not required · visa sponsored · 4/5", text, StringComparison.Ordinal);
        Assert.Contains("Backend Engineer — Acme (Hybrid)\nsalary not stated · Japanese not required · visa to check · 4/5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Full Stack", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mobile", text, StringComparison.Ordinal);
        Assert.Contains("We sponsor visas. React and TypeScript.", _chat.Asked.First(a => a.Contains("Frontend", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(SeenState.Skipped, _h.Routines.Seen.Single(s => s.Key == "role:acme|full-stack-engineer").State);
    }

    [Fact]
    public async Task Offers_outside_japan_and_boards_it_cannot_read_are_left_out()
    {
        var routine = Create(boards: "\"lever:acme\",\"teapot:acme\"");
        _board.Company.AddRange([Posting("a", "Frontend Engineer", "We sponsor visas.", place: "London, UK"), Posting("b", "Backend Engineer", "We sponsor visas.", place: "Tokyo, Japan")]);

        var run = await RunAsync(routine);

        Assert.Equal("1 new offers for you, of 1 judged; 1 of 2 fit your rules.", run.Summary);
        Assert.Contains("No reader for the board teapot:acme", run.Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JapaneseLevel.Conversational, true, true, 5_000_000L, true)]
    [InlineData(JapaneseLevel.Business, true, true, 5_000_000L, false)]
    [InlineData(JapaneseLevel.Unknown, true, null, null, true)]
    [InlineData(JapaneseLevel.None, null, true, 9_000_000L, true)]
    [InlineData(JapaneseLevel.None, false, true, 9_000_000L, false)]
    [InlineData(JapaneseLevel.None, true, false, 9_000_000L, false)]
    [InlineData(JapaneseLevel.None, true, true, 4_000_000L, false)]
    public void The_rules_ask_for_a_visa_little_japanese_and_enough_pay(JapaneseLevel japanese, bool? visa, bool? abroad, long? max, bool passes)
    {
        using var json = JsonDocument.Parse("""{"visa":true,"japaneseUpTo":"conversational","minSalary":4500000}""");
        var settings = JobSettings.Read(json.RootElement);

        Assert.Equal(passes, JobOffers.Passes(settings, Offer("x", "Developer", japanese: japanese, visa: visa, abroad: abroad, min: null, max: max)));
    }

    [Theory]
    [InlineData("Internship - AI Application Engineer")]
    [InlineData("Research Intern for Deep Generative Modeling")]
    [InlineData("エンジニア インターン")]
    public void Internships_are_left_out(string title)
    {
        using var json = JsonDocument.Parse("{}");

        Assert.False(JobOffers.Passes(JobSettings.Read(json.RootElement), Offer("x", title)));
        Assert.True(JobOffers.Passes(JobSettings.Read(json.RootElement), Offer("y", "International Software Engineer")));
    }

    private OfferInbox Inbox_() => new(_h.Routines, _h.RoutineRequests, _h.Clock);

    private const string Pasted = "Platform Engineer at Faraway Inc, Sapporo. You will run our Go services. Business Japanese needed. Apply now.";

    [Fact]
    public async Task An_offer_the_person_brings_is_judged_whatever_the_rules_and_leaves_the_inbox()
    {
        var routine = Create();
        await Inbox_().AddAsync(routine.Id, Pasted, "https://jobs.example/faraway/platform", default);
        Assert.True(_h.RoutineRequests.TryTake(out var asked));
        Assert.Equal(routine.Id, asked);
        var file = Assert.Single(Directory.GetFiles(Inbox));
        Assert.StartsWith("Link: https://jobs.example/faraway/platform\n\nPlatform Engineer", File.ReadAllText(file), StringComparison.Ordinal);

        var run = await RunAsync(routine);

        Assert.Equal("1 brought offers judged; 0 new offers for you, of 0 judged; 0 of 0 fit your rules.", run.Summary);
        Assert.Empty(Directory.GetFiles(Inbox));
        var proposal = Assert.Single(_h.Routines.Proposals);
        Assert.Equal("Platform Engineer — Faraway Inc, Sapporo", proposal.Title);
        Assert.Contains("business Japanese · visa to check · 2/5", proposal.Body, StringComparison.Ordinal);
        Assert.Contains(Pasted, _chat.Asked.Single(), StringComparison.Ordinal);
        Assert.StartsWith("Offers: 1 new offer for you\n\n1. Platform Engineer — Faraway Inc (Sapporo)", _h.Outbox.Sent.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_brought_offer_the_model_cannot_judge_waits_in_the_inbox()
    {
        var routine = Create();
        await Inbox_().AddAsync(routine.Id, Pasted, null, default);
        _chat.Fail = true;

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Single(Directory.GetFiles(Inbox));
        Assert.Empty(_h.Routines.Proposals);
    }

    [Theory]
    [InlineData("Too short.", null, "between")]
    [InlineData(Pasted, "ftp://jobs.example/x", "web address")]
    public async Task The_inbox_takes_only_a_whole_offer_and_a_web_link(string text, string? link, string error)
    {
        var routine = Create();

        var refused = await Assert.ThrowsAsync<Openspace.Domain.Common.DomainException>(() => Inbox_().AddAsync(routine.Id, text, link, default));

        Assert.Contains(error, refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Inbox));
    }

    [Fact]
    public async Task Only_a_job_offers_routine_takes_offers()
    {
        var paper = Routine.Create(_h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(6, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(paper);

        await Assert.ThrowsAsync<Openspace.Domain.Common.DomainException>(() => Inbox_().AddAsync(paper.Id, Pasted, null, default));
    }

    [Fact]
    public async Task Yes_saves_the_offer_in_its_own_folder_and_asks_the_cv_routines_to_run()
    {
        var routine = Create();
        var cv = Routine.Create(_h.Office, "CV", CvMaker.KindKey, RoutineSchedule.Daily(new TimeOnly(6, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(cv);
        Board();
        await RunAsync(routine);
        var kyoto = _h.Routines.Proposals.Single(p => p.Title.Contains("Kyo Labs", StringComparison.Ordinal));

        await Proposals().ApproveAsync(kyoto.Id, default);

        var offer = File.ReadAllText(Path.Combine(Saved, "kyo-labs-frontend-engineer", JobOffers.OfferFile));
        Assert.StartsWith("# Frontend Engineer\n\nCompany: Kyo Labs\nPlace: Kyoto\nLink: https://jobs.example/jobs/kyo-front?ref=list\nFacts: salary not stated", offer, StringComparison.Ordinal);
        Assert.Contains("Details of Frontend Engineer.", offer, StringComparison.Ordinal);
        Assert.True(_h.RoutineRequests.TryTake(out var asked));
        Assert.Equal(cv.Id, asked);
        Assert.EndsWith("its CV is on the way.", kyoto.Outcome, StringComparison.Ordinal);
    }

    private sealed class FakeBoard : IJobBoard
    {
        public List<JobOffer> Offers { get; } = [];

        /// <summary>A second board, of a company whose places say nothing ("Hybrid").</summary>
        public List<JobOffer> Company { get; } = [];

        public bool Reads(string board) => board is "made-up" or "lever:acme";

        public Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOffer>>(board == "made-up" ? [.. Offers] : [.. Company]);

        public Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken) => Task.FromResult(offer.Text ?? $"Details of {offer.Title}.");
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
            if (ask.User.StartsWith("A job offer the person found", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    fit = 2, why = "A stretch.", gaps = "Go.", japanese = "business", visa = "unknown", title = "Platform Engineer", company = "Faraway Inc", location = "Sapporo",
                }));
            }

            var title = ask.User.Split('\n')[1].Split(" at ")[0];
            var fit = title.Contains("Sales", StringComparison.Ordinal) ? 1 : title.Contains(".NET", StringComparison.Ordinal) ? 5 : 4;
            var japanese = ask.User.Contains("Japanese: Unknown", StringComparison.Ordinal) && ask.User.Contains("fluent Japanese", StringComparison.Ordinal) ? "business" : "none";
            var visa = ask.User.Contains("We sponsor visas.", StringComparison.Ordinal) ? "yes" : ask.User.Contains("No visa support.", StringComparison.Ordinal) ? "no" : "unknown";
            return Task.FromResult(JsonSerializer.Serialize(new { fit, why = $"It fits {title}.", gaps = "Some Japanese helps.", japanese, visa }));
        }
    }
}
