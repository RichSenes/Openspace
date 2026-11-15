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

    private JobOffers Kind() => new([_board], _chat, _h.Routines, _h.RoutineRequests) { RetryAfter = TimeSpan.Zero };

    private Routine Create(string language = "English", string profile = "", int most = 8, string boards = "\"made-up\"", string extra = "")
    {
        var settings = $$"""
            {{{extra}}"title":"Offers","language":"{{language}}","channel":"CAREER","profile":{{JsonSerializer.Serialize(profile.Length > 0 ? profile : Profile)}},"inbox":{{JsonSerializer.Serialize(Inbox)}},"offers":{{JsonSerializer.Serialize(Saved)}},
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
        Assert.Empty(_h.Outbox.Sent);
        Assert.Equal(3, _h.Outbox.Posted.Count);
        Assert.Equal("💼 Offers · 2 new offers for you\nAnswer with the buttons under each offer: interested and its CV follows, no and it never comes back.", _h.Outbox.Posted[0].Message.Plain);
        Assert.Null(_h.Outbox.Posted[0].Message.Buttons);
        var kyoto = _h.Routines.Proposals.Single(p => p.Title.Contains("Kyo Labs", StringComparison.Ordinal));
        Assert.Equal(2, kyoto.MessageId);
        Assert.Equal(
            [new OutboxButton("✅ I'm interested", $"p:{kyoto.Id.Value:N}:y"), new OutboxButton("❌ No", $"p:{kyoto.Id.Value:N}:n")],
            _h.Outbox.Posted[1].Message.Buttons);
        Assert.Equal(
            """
            <b>Frontend Engineer</b>
            Kyo Labs · Kyoto

            ★★★★☆ · salary not stated
            🇯🇵 Japanese not required · 🛂 Visa sponsored

            👍 It fits Frontend Engineer.
            ⚠️ Some Japanese helps.

            <a href="https://jobs.example/jobs/kyo-front?ref=list">Open the offer</a>
            """.ReplaceLineEndings("\n").TrimEnd(),
            _h.Outbox.Posted[1].Message.Html);
        Assert.StartsWith("Senior .NET Developer\nAcme · Tokyo\n\n★★★★★ · ¥6–8M", _h.Outbox.Posted[2].Message.Plain, StringComparison.Ordinal);
        Assert.EndsWith("Open the offer: https://jobs.example/jobs/acme-dotnet?ref=list", _h.Outbox.Posted[2].Message.Plain, StringComparison.Ordinal);
        Assert.Contains(
            """
            👍 It fits Senior .NET Developer.

            <blockquote expandable><b>The role in detail</b>
            📋 What you would do: You would build the booking API.

            ✅ You already have
            • C# and .NET
            • SQL

            🧩 You lack
            • Kubernetes

            📈 How to fill the gap
            • Deploy a small app on a cluster.

            ❓ To clarify
            • Whether remote work is possible.</blockquote>
            """.ReplaceLineEndings("\n").TrimEnd(),
            _h.Outbox.Posted[2].Message.Html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("⚠️", _h.Outbox.Posted[2].Message.Html, StringComparison.Ordinal);
        Assert.Contains("✅ You already have\n• C# and .NET", _h.Routines.Proposals.Single(p => p.Title.Contains(".NET", StringComparison.Ordinal)).Body, StringComparison.Ordinal);
        Assert.Contains("\"have\"", _chat.Asked[0], StringComparison.Ordinal);
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
        Assert.Contains("¥6–10M", _h.Outbox.Posted[^1].Message.Plain, StringComparison.Ordinal);
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
    public async Task Each_run_judges_at_most_a_few_those_the_desk_picks_from_every_new_title()
    {
        var routine = Create(most: 1);
        Board();

        var run = await RunAsync(routine);

        Assert.Contains("Frontend Engineer at Kyo Labs", Assert.Single(_chat.Asked), StringComparison.Ordinal);
        var desk = Assert.Single(_chat.Desks);
        Assert.Contains("[1] Senior .NET Developer — Acme, Tokyo (Web / Full-stack)", desk, StringComparison.Ordinal);
        Assert.Contains("[3] Sales Engineer — Acme, Tokyo", desk, StringComparison.Ordinal);
        Assert.Contains("Pick the 1 that most likely fit the person", desk, StringComparison.Ordinal);
        Assert.Contains("7 offers read, 3 fit your rules, 3 new, 1 to judge.", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_desk_the_newest_are_judged()
    {
        var routine = Create(most: 1);
        Board();
        _chat.DeskFails = true;

        var run = await RunAsync(routine);

        Assert.Contains("Senior .NET Developer", Assert.Single(_chat.Asked), StringComparison.Ordinal);
        Assert.Contains("The desk could not choose (The desk is away.): the newest go on.", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_desk_and_the_judge_learn_from_the_person_s_answers_and_what_they_do_not_want()
    {
        var routine = Create(most: 1, extra: "\"avoid\":[\"Go\",\"QA\"],");
        Board();
        await RunAsync(routine);
        var first = _h.Routines.Proposals.Single();
        await Proposals().RejectAsync(first.Id, default);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        _chat.Systems.Clear();
        await RunAsync(routine);

        var system = _chat.Systems.Last();
        Assert.Contains("The person does not want: Go; QA.", system, StringComparison.Ordinal);
        Assert.Contains("Offers they said no to:\n- Frontend Engineer — Kyo Labs", system, StringComparison.Ordinal);
        Assert.Contains("fits 2 at most", _chat.Asked.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_board_that_cannot_be_read_is_read_again_once()
    {
        var routine = Create();
        Board();
        _board.FailOnce = true;

        var run = await RunAsync(routine);

        Assert.Contains("made-up could not be read (Network down.): trying again shortly.", run.Log, StringComparison.Ordinal);
        Assert.Equal("2 new offers for you, of 3 judged; 3 of 7 fit your rules.", run.Summary);
    }

    [Fact]
    public async Task Offer_text_is_escaped_and_the_link_names_a_known_board()
    {
        var routine = Create(language: "Italian");
        _board.Offers.Add(Offer("x", "C# & \"Rust\" Developer", company: "R&D <Lab>", location: "Osaka") with { Url = new Uri("https://www.linkedin.com/jobs/view/1/") });

        await RunAsync(routine);

        var html = _h.Outbox.Posted[1].Message.Html;
        Assert.StartsWith("<b>C# &amp; &quot;Rust&quot; Developer</b>\nR&amp;D &lt;Lab&gt; · Osaka", html, StringComparison.Ordinal);
        Assert.EndsWith("<a href=\"https://www.linkedin.com/jobs/view/1/\">Apri l'offerta su LinkedIn</a>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Tokyo, Tokyo, Japan", "Tokyo")]
    [InlineData("Minato-ku, Tokyo, Japan", "Minato-ku, Tokyo")]
    [InlineData("Japan", "Japan")]
    [InlineData("Hybrid", "Hybrid")]
    public void A_place_says_each_part_once_and_the_country_only_alone(string place, string tidy) => Assert.Equal(tidy, JapanPlaces.Tidy(place));

    [Theory]
    [InlineData(5, "★★★★★")]
    [InlineData(3, "★★★☆☆")]
    [InlineData(1, "★☆☆☆☆")]
    public void The_fit_shows_as_stars(int fit, string stars) => Assert.Equal(stars, JobOffers.Stars(fit));

    [Fact]
    public async Task It_speaks_the_person_s_language()
    {
        var routine = Create(language: "Italian");
        Board();

        await RunAsync(routine);

        var posted = _h.Outbox.Posted.Select(p => p.Message.Plain).ToList();
        Assert.Equal("💼 Offers · 2 offerte nuove per te\nRispondi con i bottoni sotto ogni offerta: «mi interessa» e arriva il CV, «no» e non torna più.", posted[0]);
        Assert.Equal(["✅ Mi interessa", "❌ No"], _h.Outbox.Posted[1].Message.Buttons!.Select(b => b.Text));
        Assert.Contains("Kyo Labs · Kyoto\n\n★★★★☆ · stipendio non indicato\n🇯🇵 Giapponese non richiesto · 🛂 Visto sponsorizzato", posted[1], StringComparison.Ordinal);
        Assert.Contains("Il ruolo nel dettaglio\n📋 Cosa faresti: You would build the booking API.\n\n✅ Hai già", posted[2], StringComparison.Ordinal);
        Assert.EndsWith("Apri l'offerta: https://jobs.example/jobs/kyo-front?ref=list", posted[1], StringComparison.Ordinal);
        Assert.Contains("Write every text in Italian, whatever the language of the offer", _chat.Asked[0], StringComparison.Ordinal);
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
        var text = string.Join("\n\n", _h.Outbox.Posted.Select(p => p.Message.Plain));
        Assert.Contains("Frontend Engineer\nAcme · Hybrid\n\n★★★★☆ · salary not stated\n🇯🇵 Japanese not required · 🛂 Visa sponsored", text, StringComparison.Ordinal);
        Assert.Contains("Backend Engineer\nAcme · Hybrid\n\n★★★★☆ · salary not stated\n🇯🇵 Japanese not required · 🛂 Visa to check", text, StringComparison.Ordinal);
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
        Assert.StartsWith("💼 Offers · 1 new offer for you", _h.Outbox.Posted[0].Message.Plain, StringComparison.Ordinal);
        Assert.StartsWith("<b>Platform Engineer</b>\nFaraway Inc · Sapporo", _h.Outbox.Posted[1].Message.Html, StringComparison.Ordinal);
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
    public async Task The_pay_the_description_states_is_read_as_yen_a_year_and_the_rules_hold_for_it()
    {
        var routine = Create();
        _board.Offers.AddRange([
            Offer("paid", "Frontend Engineer", company: "Kyo Labs", min: null, max: null) with { Text = "React. Salary: 600万円〜900万円 per year." },
            Offer("cheap", "Backend Developer", company: "Low Pay", min: null, max: null) with { Text = "C#. 月給30万円." },
        ]);

        var run = await RunAsync(routine);

        var posted = string.Join("\n", _h.Outbox.Posted.Select(p => p.Message.Plain));
        Assert.Contains("Kyo Labs · Tokyo\n\n★★★★☆ · ¥6–9M", posted, StringComparison.Ordinal);
        Assert.DoesNotContain("Low Pay", posted, StringComparison.Ordinal);
        Assert.Contains("\"salaryUnit\"", _chat.Asked[0], StringComparison.Ordinal);
        Assert.Equal("1 new offers for you, of 2 judged; 2 of 2 fit your rules.", run.Summary);
    }

    [Fact]
    public async Task Yes_keeps_the_role_in_detail_in_the_offer_s_file_for_its_cv()
    {
        var routine = Create();
        Board();
        await RunAsync(routine);
        var dotnet = _h.Routines.Proposals.Single(p => p.Title.Contains(".NET", StringComparison.Ordinal));

        await Proposals().ApproveAsync(dotnet.Id, default);

        var offer = File.ReadAllText(Path.Combine(Saved, "acme-senior-net-developer", JobOffers.OfferFile));
        Assert.Contains("\n## How the person fits\n\n📋 What you would do: You would build the booking API.", offer, StringComparison.Ordinal);
        Assert.Contains("🧩 You lack\n• Kubernetes", offer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_long_detail_keeps_fewer_points_so_the_message_fits_telegram()
    {
        var routine = Create();
        Board();
        _chat.Wordy = true;

        await RunAsync(routine);

        var html = _h.Outbox.Posted.Single(p => p.Message.Html.Contains(".NET", StringComparison.Ordinal)).Message.Html;
        Assert.True(html.Length <= 3900, $"{html.Length} characters");
        Assert.Contains("<blockquote expandable>", html, StringComparison.Ordinal);
        Assert.Contains("• Point 1 ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("• Point 6 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yes_keeps_the_offer_s_message_in_its_folder_for_the_cv_to_answer()
    {
        var routine = Create();
        Board();
        await RunAsync(routine);
        var kyoto = _h.Routines.Proposals.Single(p => p.Title.Contains("Kyo Labs", StringComparison.Ordinal));

        await Proposals().ApproveAsync(kyoto.Id, default);

        Assert.Equal("2", File.ReadAllText(Path.Combine(Saved, "kyo-labs-frontend-engineer", JobOffers.MessageFile)));
    }

    [Fact]
    public async Task In_the_chat_yes_and_no_answer_the_proposal_and_a_second_press_is_told_it_is_decided()
    {
        var routine = Create(language: "Italian");
        Board();
        await RunAsync(routine);
        var kyoto = _h.Routines.Proposals.Single(p => p.Title.Contains("Kyo Labs", StringComparison.Ordinal));
        var dotnet = _h.Routines.Proposals.Single(p => p.Title.Contains(".NET", StringComparison.Ordinal));
        var answers = new ChatAnswers(_h.Routines, [Kind()], Proposals(), new OfferInbox(_h.Routines, _h.RoutineRequests, _h.Clock));

        var yes = await answers.AnswerAsync($"p:{kyoto.Id.Value:N}:y", default);
        var no = await answers.AnswerAsync($"p:{dotnet.Id.Value:N}:n", default);
        var again = await answers.AnswerAsync($"p:{kyoto.Id.Value:N}:n", default);

        Assert.Equal(new ChatAnswer("Segnata: ti interessa. Il CV arriverà in risposta qui.", "✅ Ti interessa"), yes);
        Assert.Equal(ProposalStatus.Done, kyoto.Status);
        Assert.True(File.Exists(Path.Combine(Saved, "kyo-labs-frontend-engineer", JobOffers.OfferFile)));
        Assert.Equal(new ChatAnswer("Scartata: non tornerà.", "❌ Scartata"), no);
        Assert.Equal(ProposalStatus.Rejected, dotnet.Status);
        Assert.Equal(new ChatAnswer("Già decisa.", "✅ Ti interessa"), again);
        Assert.Equal(ProposalStatus.Done, kyoto.Status);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("p:not-a-proposal:y")]
    [InlineData("p:0123456789abcdef0123456789abcdef:maybe")]
    public async Task What_is_not_an_answer_to_a_proposal_is_left_alone(string data) =>
        Assert.Null(await new ChatAnswers(_h.Routines, [Kind()], Proposals(), new OfferInbox(_h.Routines, _h.RoutineRequests, _h.Clock)).AnswerAsync(data, default));

    [Fact]
    public async Task A_proposal_no_longer_there_is_said_so() =>
        Assert.Equal(
            "This proposal is no longer in Openspace.",
            (await new ChatAnswers(_h.Routines, [Kind()], Proposals(), new OfferInbox(_h.Routines, _h.RoutineRequests, _h.Clock)).AnswerAsync($"p:{Guid.NewGuid():N}:y", default))!.Notice);

    [Fact]
    public async Task An_offer_pasted_in_the_chat_goes_to_the_inbox_of_that_bot_s_routine_and_is_judged()
    {
        var routine = Create(language: "Italian");
        var answers = new ChatAnswers(_h.Routines, [Kind()], Proposals(), new OfferInbox(_h.Routines, _h.RoutineRequests, _h.Clock));
        var pasted = "Senior C# Developer at Kyo Labs, Kyoto. We build booking software with .NET and React; visa sponsored. https://jobs.example/kyo/42.";

        var received = await answers.OfferAsync("career", pasted, default);
        var tooShort = await answers.OfferAsync("CAREER", "https://jobs.example/kyo/42", default);
        var nobody = await answers.OfferAsync("PAPERBOY", pasted, default);

        Assert.Equal("📥 Ricevuta: la giudico e torna qui con i suoi bottoni.", received);
        Assert.StartsWith("Incolla tutta l'offerta", tooShort, StringComparison.Ordinal);
        Assert.Null(nobody);
        var file = Assert.Single(Directory.GetFiles(Inbox));
        Assert.StartsWith("Link: https://jobs.example/kyo/42\n\nSenior C# Developer", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.True(_h.RoutineRequests.TryTake(out var asked));
        Assert.Equal(routine.Id, asked);
    }

    [Fact]
    public async Task Only_the_bots_of_enabled_routines_that_ask_in_the_chat_are_listened_to()
    {
        Create();
        var paused = Routine.Create(_h.Office, "Old offers", "job-offers", RoutineSchedule.Daily(new TimeOnly(8, 0)), """{"channel":"OLD"}""", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        paused.TurnOn(false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(paused);
        _h.Routines.Add(Routine.Create(_h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(6, 0)), """{"channel":"PAPERBOY"}""", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc));

        var channels = await new ChatAnswers(_h.Routines, [Kind()], Proposals(), new OfferInbox(_h.Routines, _h.RoutineRequests, _h.Clock)).ChannelsAsync(default);

        Assert.Equal(["CAREER"], channels);
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

        /// <summary>The first read fails, as when the network drops for a moment.</summary>
        public bool FailOnce { get; set; }

        public bool Reads(string board) => board is "made-up" or "lever:acme";

        public Task<IReadOnlyList<JobOffer>> LatestAsync(string board, CancellationToken cancellationToken)
        {
            if (FailOnce)
            {
                FailOnce = false;
                throw new HttpRequestException("Network down.");
            }

            return Task.FromResult<IReadOnlyList<JobOffer>>(board == "made-up" ? [.. Offers] : [.. Company]);
        }

        public Task<string> DetailsAsync(JobOffer offer, CancellationToken cancellationToken) => Task.FromResult(offer.Text ?? $"Details of {offer.Title}.");
    }

    /// <summary>Sales jobs fit poorly; .NET ones best; the rest well.</summary>
    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public List<string> Systems { get; } = [];

        /// <summary>What the desk was asked.</summary>
        public List<string> Desks { get; } = [];

        public bool DeskFails { get; set; }

        private static readonly string[] Have = ["C# and .NET", " ", "- SQL"];

        private static readonly string[] Missing = ["Kubernetes"];

        private static readonly string[] Improve = ["Deploy a small app on a cluster."];

        private static readonly string[] Ask = ["Whether remote work is possible."];

        /// <summary>The role in detail with six long points per list.</summary>
        public bool Wordy { get; set; }

        public bool Fail { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The model is busy.");
            }

            Systems.Add(ask.System);
            if (ask.User.StartsWith("The desk of job offers", StringComparison.Ordinal))
            {
                Desks.Add(ask.User);
                if (DeskFails)
                {
                    throw new InvalidOperationException("The desk is away.");
                }

                var lines = ask.User.Split('\n').Where(l => l.StartsWith('[')).ToList();

                // The desk keeps away sales and picks backwards, to show its order wins over the newest.
                var picks = lines.Where(l => !l.Contains("Sales", StringComparison.Ordinal))
                    .Select(l => int.Parse(l[1..l.IndexOf(']', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture)).Reverse();
                return Task.FromResult(JsonSerializer.Serialize(new { picks }));
            }

            Asked.Add(ask.User);
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
            // .NET offers come with the role in detail, the others without, to show both messages.
            var detailed = title.Contains(".NET", StringComparison.Ordinal);
            var many = Enumerable.Range(1, 6).Select(n => $"Point {n} " + new string('x', 170)).ToArray();
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                fit, why = $"It fits {title}.", gaps = "Some Japanese helps.", japanese, visa,
                salaryLow = ask.User.Contains("600万円", StringComparison.Ordinal) ? 600 : ask.User.Contains("月給", StringComparison.Ordinal) ? 30 : 0,
                salaryHigh = ask.User.Contains("600万円", StringComparison.Ordinal) ? 900 : 0,
                salaryUnit = ask.User.Contains("万円", StringComparison.Ordinal) ? "man-yen" : "yen",
                salaryPer = ask.User.Contains("月給", StringComparison.Ordinal) ? "month" : "year",
                role = detailed ? "You would build the booking API." : "",
                have = detailed ? Wordy ? many : Have : [],
                missing = detailed ? Wordy ? many : Missing : [],
                improve = detailed ? Wordy ? many : Improve : [],
                ask = detailed ? Wordy ? many : Ask : [],
            }));
        }
    }
}
