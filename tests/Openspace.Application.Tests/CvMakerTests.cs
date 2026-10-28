using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The CV routine on a made-up person: invented profile, invented data, no real model, no real printer.</summary>
public sealed class CvMakerTests : IDisposable
{
    private const string Personal = """
        {"name":"Ada Example","nameKana":"エイダ・イグザンプル","birthDate":"1990-06-30","address":"Example Street 1, Rome","addressKana":"",
         "email":"ada@example.com","phone":"+39 000 000 0000","location":"Rome, Italy","links":["ada.example"],"photo":"face.png"}
        """;

    private readonly Harness _h = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-cv-{Guid.NewGuid():N}");
    private readonly FakeChat _chat = new();
    private readonly PagePrinter _printer = new();

    public CvMakerTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "profile.md"), "Ada writes C# and TypeScript. Software developer at Example S.r.l. since July 2025.");
        File.WriteAllText(Path.Combine(_root, "personal.json"), Personal);
        File.WriteAllBytes(Path.Combine(_root, "face.png"), [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Offers => Path.Combine(_root, "offers");

    private Routine Create(string languages = "\"English\",\"Italian\",\"Japanese\"")
    {
        string P(string name) => JsonSerializer.Serialize(Path.Combine(_root, name));
        var settings = $$"""
            {"title":"CV","channel":"CAREER","folder":{{P("")}},"profile":{{P("profile.md")}},"personal":{{P("personal.json")}},"offers":{{P("offers")}},
             "languages":[{{languages}}],"japaneseForms":true,"tailoredLanguage":"English"}
            """;
        var routine = Routine.Create(_h.Office, "CV", CvMaker.KindKey, RoutineSchedule.Daily(new TimeOnly(10, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [new CvMaker(_chat, _printer)], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.OrderBy(r => r.StartedAt).Last();
    }

    private void Offer(string folder, string text)
    {
        Directory.CreateDirectory(Path.Combine(Offers, folder));
        File.WriteAllText(Path.Combine(Offers, folder, JobOffers.OfferFile), text);
    }

    [Fact]
    public async Task A_first_run_makes_the_whole_set_in_a_numbered_folder_and_sends_it()
    {
        var routine = Create();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        var folder = Path.Combine(_root, "v1-2015-06-24");
        Assert.Equal($"New set in {folder}.", run.Summary);
        Assert.Equal(
            ["CV_Ada-Example_en.pdf", "CV_Ada-Example_it.pdf", "CV_Ada-Example_ja.pdf", "履歴書_Ada-Example.pdf", "職務経歴書_Ada-Example.pdf"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(6, _h.Outbox.Sent.Count);
        Assert.Equal($"CV: v1, 5 files in {folder}", _h.Outbox.Sent[0].Text);

        var english = _printer.Pages[0];
        Assert.Contains("<h1>Ada Example</h1>", english, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", english, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/png;base64,iVBORwECAw==\"", english, StringComparison.Ordinal);
        Assert.Contains("<h2>Experience</h2>", english, StringComparison.Ordinal);
        Assert.Contains("<h2>Esperienza</h2>", _printer.Pages[1], StringComparison.Ordinal);
        Assert.Contains("lang=\"ja\"", _printer.Pages[2], StringComparison.Ordinal);
        Assert.Contains("1990年6月30日生（満 24 歳）", _printer.Pages[3], StringComparison.Ordinal);
        Assert.Contains("現在に至る", _printer.Pages[3], StringComparison.Ordinal);
        Assert.Contains("■ 職務要約", _printer.Pages[4], StringComparison.Ordinal);

        Assert.All(_chat.Systems, s => Assert.Contains("Ada writes C# and TypeScript.", s, StringComparison.Ordinal));
        Assert.All(_chat.Systems, s => Assert.DoesNotContain("+39 000", s, StringComparison.Ordinal));
        Assert.Contains("Write in Italian", _chat.Systems[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_changed_makes_nothing_and_a_new_profile_makes_the_next_version()
    {
        var routine = Create("\"English\"");
        await RunAsync(routine);
        _h.Clock.Advance(TimeSpan.FromDays(14));

        var same = await RunAsync(routine);
        File.AppendAllText(Path.Combine(_root, "profile.md"), " Now also writes Go.");
        _h.Clock.Advance(TimeSpan.FromDays(14));
        var changed = await RunAsync(routine);

        Assert.Equal("Nothing new: the profile has not changed and no offer waits for a CV.", same.Summary);
        Assert.Equal($"New set in {Path.Combine(_root, "v2-2015-07-22")}.", changed.Summary);
        Assert.True(Directory.Exists(Path.Combine(_root, "v1-2015-06-24")));
    }

    [Fact]
    public async Task Each_saved_offer_gets_its_own_cv_once()
    {
        var routine = Create("\"English\"");
        await RunAsync(routine);
        Offer("kyo-labs-frontend-engineer", "# Frontend Engineer\n\nCompany: Kyo Labs\n\nReact and TypeScript, Kyoto.");
        Offer("empty-folder", "");
        File.Delete(Path.Combine(Offers, "empty-folder", JobOffers.OfferFile));

        var run = await RunAsync(routine);
        var again = await RunAsync(routine);

        Assert.Equal("1 CVs made for offers.", run.Summary);
        Assert.True(File.Exists(Path.Combine(Offers, "kyo-labs-frontend-engineer", "CV_Ada-Example.pdf")));
        Assert.Contains("React and TypeScript, Kyoto.", _chat.Asked[^1], StringComparison.Ordinal);
        Assert.Equal("CV: kyo-labs-frontend-engineer\nIt puts React first.", _h.Outbox.Sent[^1].Text);
        Assert.Equal("Nothing new: the profile has not changed and no offer waits for a CV.", again.Summary);
    }

    [Fact]
    public async Task Without_personal_data_it_leaves_a_form_to_fill_and_says_so()
    {
        File.Delete(Path.Combine(_root, "personal.json"));

        var run = await RunAsync(Create());

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("Fill in your name and contacts", run.Summary, StringComparison.Ordinal);
        Assert.Contains("\"nameKana\"", File.ReadAllText(Path.Combine(_root, "personal.json")), StringComparison.Ordinal);
        Assert.Empty(_chat.Asked);
    }

    [Fact]
    public async Task A_missing_photo_leaves_the_pages_without_it()
    {
        File.Delete(Path.Combine(_root, "face.png"));

        var run = await RunAsync(Create("\"English\""));

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("The photo face.png is not there", run.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", _printer.Pages[0], StringComparison.Ordinal);
        Assert.Contains("写真", _printer.Pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_silent_model_leaves_no_half_set_and_tries_again_next_time()
    {
        var routine = Create("\"English\"");
        _chat.Fail = true;

        var failed = await RunAsync(routine);
        _chat.Fail = false;
        var next = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Failed, failed.Status);
        Assert.StartsWith("New set in", next.Summary, StringComparison.Ordinal);
        Assert.Single(Directory.GetDirectories(_root, "v*"));
    }

    [Fact]
    public void Personal_data_must_name_someone()
    {
        Assert.Throws<InvalidOperationException>(() => PersonalData.Read(PersonalData.Template));
        Assert.Equal("Ada-Example", PersonalData.Read(Personal).FileName);
        Assert.Equal(new DateOnly(1990, 6, 30), PersonalData.Read(Personal).Birth);
    }

    /// <summary>Writes each kind of page from what the request asks; the tailored CV says what it puts first.</summary>
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
            if (ask.User.Contains("rirekisho", StringComparison.Ordinal))
            {
                return Task.FromResult("""
                    {"education":[{"year":2015,"month":3,"text":"例大学 卒業"}],"work":[{"year":2025,"month":7,"text":"例株式会社 入社"},{"year":0,"month":0,"text":"現在に至る"}],
                     "licenses":[],"motivation":"貢献したいです。","wishes":"ビザのスポンサーをお願いします。"}
                    """);
            }

            if (ask.User.Contains("shokumu", StringComparison.Ordinal))
            {
                return Task.FromResult("""{"summary":"開発を担当。","jobs":[],"projects":[],"skills":[{"group":"言語","items":["C#"]}],"pr":"頑張ります。"}""");
            }

            var focus = ask.User.Contains("job offer", StringComparison.Ordinal) ? "It puts React first." : "";
            return Task.FromResult($$"""
                {"headline":"Developer","summary":"Writes C#.",
                 "experience":[{"role":"Software Developer","company":"Example S.r.l.","place":"Rome","period":"2025 – now","points":["C#"]}],
                 "projects":[],"education":[],"skills":[{"group":"Languages","items":["C#","TypeScript"]}],
                 "languages":[{"name":"Italian","level":"native"}],"certifications":[],"interests":"","focus":"{{focus}}"}
                """);
        }
    }

    /// <summary>Keeps the pages it is given, in order, and prints nothing.</summary>
    private sealed class PagePrinter : IPdfPrinter
    {
        public List<string> Pages { get; } = [];

        public Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken)
        {
            Pages.Add(html);
            return Task.FromResult("%PDF-1.7"u8.ToArray());
        }
    }
}
