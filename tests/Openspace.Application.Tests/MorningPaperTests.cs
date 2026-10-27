using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

public sealed class MorningPaperTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-paper-{Guid.NewGuid():N}");
    private readonly FakeFeeds _feeds = new();
    private readonly FakeDecider _decider = new();
    private readonly FakeChat _chat = new();
    private readonly FakePrinter _printer = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MorningPaper Paper() => new(_chat, _decider, _feeds, _printer);

    private Routine Create(int perSection = 2, string? sections = null)
    {
        var settings = $$"""
            {"title":"Daily Bugle","subtitle":"デイリー","language":"English","folder":{{JsonSerializer.Serialize(_folder)}},
             "reader":"Likes cats and trains.","rules":["Be kind."],"perSection":{{perSection}},
             "sections":{{sections ?? """[{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"},{"name":"Courier","url":"https://courier.example/feed"}]}]"""}}}
            """;
        var routine = Routine.Create(_h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(6, 30)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, [Paper()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.OrderBy(r => r.StartedAt).Last();
    }

    private FeedItem Item(string source, string title, int hoursAgo = 2, string summary = "Something happened.") =>
        new(source, title, new Uri($"https://{source.ToLowerInvariant()}.example/{Uri.EscapeDataString(title)}"), _h.Clock.GetUtcNow().AddHours(-hoursAgo), summary);

    [Fact]
    public async Task It_prints_a_numbered_paper_keeps_it_and_delivers_it()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "New tram line"), Item("Gazette", "Old news", hoursAgo: 50)];
        _feeds.Items["https://courier.example/feed"] = [Item("Courier", "Cat show on Sunday")];

        var run = await RunAsync(routine);

        Assert.Equal((RoutineRunStatus.Succeeded, "N. 1: 2 articles in 1 sections."), (run.Status, run.Summary));
        var pdf = Assert.Single(Directory.GetFiles(_folder, "*.pdf"));
        Assert.EndsWith("Daily-Bugle-N001-2015-06-24.pdf", pdf, StringComparison.Ordinal);
        var html = await File.ReadAllTextAsync(Path.ChangeExtension(pdf, ".html"));
        Assert.Contains("N. 1", html, StringComparison.Ordinal);
        Assert.Contains("デイリー", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://courier.example/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Old news", _chat.Prompts[0], StringComparison.Ordinal);
        var (_, caption, file) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(pdf, file);
        Assert.StartsWith("Daily Bugle · N. 1 · Wednesday 24 June 2015", caption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task News_already_printed_never_comes_back_and_the_next_issue_counts_on()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "New tram line")];
        await RunAsync(routine);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        var nothing = await RunAsync(routine);
        Assert.Equal("Nothing new to print today.", nothing.Summary);

        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "New tram line"), Item("Gazette", "Bridge reopens")];
        var second = await RunAsync(routine);

        Assert.StartsWith("N. 2:", second.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("New tram line", _chat.Prompts[^2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_markers_leave_the_text_and_one_outlet_s_items_are_numbered()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "New tram line"), Item("Gazette", "Bridge reopens")];
        _chat.MergeAll = true;

        await RunAsync(routine);

        var html = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_folder, "*.html")));
        Assert.DoesNotContain("[1]", html, StringComparison.Ordinal);
        Assert.Contains("Gazette (<a href=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_feed_that_fails_is_logged_and_the_others_go_on()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "New tram line")];
        _feeds.Failing.Add("https://courier.example/feed");

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("Courier: could not be read (Server down.)", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_decider_keeps_what_the_reader_cares_about()
    {
        var routine = Create(perSection: 1);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tax forms"), Item("Gazette", "Cat cafe opens"), Item("Gazette", "Parking rules")];

        await RunAsync(routine);

        Assert.Contains("Cat cafe opens", _chat.Prompts[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Tax forms", _chat.Prompts[0], StringComparison.Ordinal);
        Assert.Equal(3, _decider.Asked);
    }

    [Fact]
    public async Task Text_from_feeds_and_model_is_escaped_on_the_page()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "<script>alert(1)</script>")];
        _chat.Headline = "<img src=x onerror=alert(1)>";

        await RunAsync(routine);

        var html = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_folder, "*.html")));
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_paper_without_sections_says_what_is_missing()
    {
        var run = await RunAsync(Create(sections: "[]"));

        Assert.Equal((RoutineRunStatus.Failed, "The paper has no sections: add some, each with its feeds."), (run.Status, run.Summary));
    }

    [Fact]
    public void The_next_number_follows_the_highest_kept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Daily-Bugle-N007-2015-06-20.pdf"), "");
        File.WriteAllText(Path.Combine(_folder, "Daily-Bugle-N012-2015-06-23.pdf"), "");
        File.WriteAllText(Path.Combine(_folder, "notes.pdf"), "");

        Assert.Equal(13, MorningPaper.NextNumber(_folder));
    }

    private sealed class FakeFeeds : IFeedReader
    {
        public Dictionary<string, List<FeedItem>> Items { get; } = [];

        public HashSet<string> Failing { get; } = [];

        public Task<IReadOnlyList<FeedItem>> ReadAsync(Uri feed, string source, CancellationToken cancellationToken) =>
            Failing.Contains(feed.ToString())
                ? throw new HttpRequestException("Server down.")
                : Task.FromResult<IReadOnlyList<FeedItem>>(Items.GetValueOrDefault(feed.ToString()) ?? []);
    }

    private sealed class FakeDecider : IDecider
    {
        public int Asked { get; private set; }

        public bool Available => true;

        public Task<IReadOnlyDictionary<string, Decision>> DecideAsync(string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
        {
            Asked++;
            var likes = state.Contains("Cat", StringComparison.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, Decision>>(new Dictionary<string, Decision> { ["interesting"] = new(likes ? "true" : "false", 0.9, false) });
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Prompts { get; } = [];

        public string? Headline { get; set; }

        /// <summary>One article from all the section's news.</summary>
        public bool MergeAll { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Prompts.Add(ask.User);
            if (ask.User.StartsWith("Today's headlines", StringComparison.Ordinal))
            {
                return Task.FromResult("""{"title":"A calm morning","text":"Coffee first."}""");
            }

            var count = ask.User.Split('\n').Count(l => l.StartsWith('['));
            if (MergeAll)
            {
                return Task.FromResult(JsonSerializer.Serialize(new { articles = new[] { new { headline = "Town news", text = "All at once. [1, 2]", sources = Enumerable.Range(1, count).ToArray() } } }));
            }

            var articles = Enumerable.Range(1, count).Select(n => new { headline = Headline ?? $"Story {n}", text = $"It happened. [{n}]", sources = new[] { n, 99 } });
            return Task.FromResult(JsonSerializer.Serialize(new { articles }));
        }
    }

    private sealed class FakePrinter : IPdfPrinter
    {
        public Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken) => Task.FromResult("%PDF-1.7"u8.ToArray());
    }
}
