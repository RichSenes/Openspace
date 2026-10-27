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
    private readonly FakeImages _images = new();
    private readonly FakeKanjiBook _kanji = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private MorningPaper Paper() => new(_chat, _decider, _feeds, _printer, _images, _kanji);

    private Routine Create(int perSection = 2, string? sections = null, string goodNews = "[]", string kanji = "{}", string language = "English")
    {
        var settings = $$"""
            {"title":"Daily Bugle","subtitle":"デイリー","language":"{{language}}","folder":{{JsonSerializer.Serialize(_folder)}},"kanji":{{kanji}},
             "reader":"Likes cats and trains.","rules":["Be kind."],"perSection":{{perSection}},"goodNews":{{goodNews}},
             "sections":{{sections ?? """[{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"},{"name":"Courier","url":"https://courier.example/feed"}]}]"""}}}
            """;
        var routine = Routine.Create(_h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(6, 30)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [Paper()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.OrderBy(r => r.StartedAt).Last();
    }

    private string Html() => File.ReadAllText(Assert.Single(Directory.GetFiles(_folder, "*.html")));

    private FeedItem Item(string source, string title, int hoursAgo = 2, string summary = "Something happened.", Uri? image = null) =>
        new(source, title, new Uri($"https://{source.ToLowerInvariant()}.example/{Uri.EscapeDataString(title)}"), _h.Clock.GetUtcNow().AddHours(-hoursAgo), summary, image);

    [Fact]
    public async Task It_prints_a_numbered_paper_with_a_front_page_keeps_it_and_delivers_it()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown"), Item("Gazette", "Old news", hoursAgo: 50)];
        _feeds.Items["https://courier.example/feed"] = [Item("Courier", "Cat show on Sunday")];

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.StartsWith("N. 1:", run.Summary, StringComparison.Ordinal);
        var pdf = Assert.Single(Directory.GetFiles(_folder, "*.pdf"));
        Assert.EndsWith("Daily-Bugle-N001-2015-06-24.pdf", pdf, StringComparison.Ordinal);
        var html = Html();
        Assert.True(html.IndexOf("class=\"issue\"", StringComparison.Ordinal) < html.IndexOf("<h1>", StringComparison.Ordinal), "The issue line comes before the title.");
        Assert.Contains("class=\"lead\"", html, StringComparison.Ordinal);
        Assert.Contains("In this issue", html, StringComparison.Ordinal);
        Assert.Contains("デイリー", html, StringComparison.Ordinal);
        Assert.Contains("Written by a local model", html, StringComparison.Ordinal);
        Assert.DoesNotContain(_chat.Prompts, p => p.Contains("Old news", StringComparison.Ordinal));
        var (_, caption, file) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(pdf, file);
        Assert.StartsWith("Daily Bugle · N. 1 · Wednesday 24 June 2015", caption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_most_important_story_opens_the_paper_with_its_picture()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Cat show on Sunday"), Item("Gazette", "Big strike stops the city", image: new Uri("https://img.example/strike.jpg"))];

        await RunAsync(routine);

        var html = Html();
        var lead = html[html.IndexOf("class=\"lead\"", StringComparison.Ordinal)..html.IndexOf("class=\"front-row\"", StringComparison.Ordinal)];
        Assert.Contains("About Big strike stops the city", lead, StringComparison.Ordinal);
        Assert.Contains("src=\"data:image/jpeg;base64,", lead, StringComparison.Ordinal);
        Assert.Contains("Photo: Gazette", lead, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_picture_that_is_not_a_landscape_photo_is_left_out()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city", image: new Uri("https://img.example/tall-report-preview.png"))];

        await RunAsync(routine);

        var html = Html();
        var lead = html[html.IndexOf("class=\"lead\"", StringComparison.Ordinal)..html.IndexOf("class=\"front-row\"", StringComparison.Ordinal)];
        Assert.DoesNotContain("<figure>", lead, StringComparison.Ordinal);
        Assert.Contains("About Big strike stops the city", lead, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_story_told_by_two_outlets_is_one_article_citing_both()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Downtown tram line opens today")];
        _feeds.Items["https://courier.example/feed"] = [Item("Courier", "Tram line opens downtown"), Item("Courier", "Cat show on Sunday")];

        await RunAsync(routine);

        var lead = _chat.Prompts.Single(p => p.StartsWith("For the front page", StringComparison.Ordinal));
        Assert.Contains("Gazette: Downtown tram line opens today", lead, StringComparison.Ordinal);
        Assert.Contains("Courier: Tram line opens downtown", lead, StringComparison.Ordinal);
        Assert.Contains(">Gazette</a>, <a", Html(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opinions_are_left_out_and_good_news_gets_its_box()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Opinion: why trams matter"), Item("Gazette", "Big strike stops the city")];
        _feeds.Items["https://courier.example/feed"] = [Item("Courier", "Old bridge reopens after repairs")];

        await RunAsync(routine);

        var html = Html();
        Assert.DoesNotContain("Opinion", string.Join("\n", _chat.Prompts), StringComparison.Ordinal);
        var box = html[html.IndexOf("class=\"good\"", StringComparison.Ordinal)..];
        Assert.Contains("The good news", box, StringComparison.Ordinal);
        Assert.Contains("About Old bridge reopens after repairs", box, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Good_news_comes_only_from_the_sections_named_for_it()
    {
        var routine = Create(
            sections: """
                [{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
                 {"name":"Tech","about":"Gadgets.","feeds":[{"name":"Courier","url":"https://courier.example/feed"}]}]
                """,
            goodNews: """["City"]""");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city")];
        _feeds.Items["https://courier.example/feed"] = [Item("Courier", "Phone maker reopens its shop")];

        await RunAsync(routine);

        Assert.DoesNotContain("class=\"good\"", Html(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_heading_travels_with_its_first_story()
    {
        var routine = Create(perSection: 3);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Cat cafe opens"), Item("Gazette", "Tram line opens")];

        await RunAsync(routine);

        var html = Html();
        var opener = html[html.IndexOf("<div class=\"opener\"><h2>City</h2>", StringComparison.Ordinal)..];
        Assert.Matches("^<div class=\"opener\"><h2>City</h2>\\s*<article class=\"first\"><h3>About", opener);
        Assert.Contains("<p class=\"kicker\">Front page</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_decider_keeps_what_the_reader_cares_about()
    {
        var routine = Create(perSection: 1);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tax forms"), Item("Gazette", "Cat cafe opens"), Item("Gazette", "Parking rules")];

        await RunAsync(routine);

        Assert.Equal(3, _decider.Asked);
        Assert.Contains("About Cat cafe opens", Html(), StringComparison.Ordinal);
        Assert.DoesNotContain("Tax forms", string.Join("\n", _chat.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task News_already_printed_never_comes_back_and_the_next_issue_counts_on()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];
        await RunAsync(routine);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        var nothing = await RunAsync(routine);
        Assert.Equal("Nothing new to print today.", nothing.Summary);

        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown"), Item("Gazette", "Bridge reopens")];
        var second = await RunAsync(routine);

        Assert.StartsWith("N. 2:", second.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Tram line opens downtown", _chat.Prompts[^2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_feed_that_fails_is_logged_and_the_others_go_on()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];
        _feeds.Failing.Add("https://courier.example/feed");

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("Courier: could not be read (Server down.)", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_markers_leave_the_text_and_one_outlet_s_items_are_numbered()
    {
        var routine = Create(perSection: 3);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Tram line opens"), Item("Gazette", "Bridge closes")];
        _chat.MergeAll = true;

        await RunAsync(routine);

        var html = Html();
        Assert.DoesNotContain("[1", html, StringComparison.Ordinal);
        Assert.Contains("Gazette (<a href=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_from_feeds_and_model_is_escaped_on_the_page()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "<script>alert(1)</script>")];
        _chat.Headline = "<img src=x onerror=alert(1)>";

        await RunAsync(routine);

        var html = Html();
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_paper_without_sections_says_what_is_missing()
    {
        var run = await RunAsync(Create(sections: "[]"));

        Assert.Equal((RoutineRunStatus.Failed, "The paper has no sections: add some, each with its feeds."), (run.Status, run.Summary));
    }

    [Fact]
    public async Task The_kanji_of_the_day_closes_the_paper_with_its_strokes_words_and_sentences()
    {
        var routine = Create(kanji: """{"enabled":true}""");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        var run = await RunAsync(routine);

        Assert.EndsWith(", kanji 日.", run.Summary, StringComparison.Ordinal);
        var html = Html();
        var box = html[html.IndexOf("<section class=\"kanji\">", StringComparison.Ordinal)..];
        Assert.Contains("<div class=\"glyph\" lang=\"ja\">日</div>", box, StringComparison.Ordinal);
        Assert.Contains("day, sun", box, StringComparison.Ordinal);
        Assert.Contains("A window with the sun in it.", box, StringComparison.Ordinal);
        Assert.Equal(2, box.Split("<svg ").Length - 1);
        Assert.Contains("<ruby>毎<b>日</b><rt>まいにち</rt></ruby></span> each day", box, StringComparison.Ordinal);
        Assert.Contains("<ruby>毎<rt>まい</rt><b>日</b><rt>にち</rt></ruby>", box, StringComparison.Ordinal);
        Assert.Contains("I walk every day.", box, StringComparison.Ordinal);
        Assert.DoesNotContain("I walk each day.", box, StringComparison.Ordinal);
        Assert.Contains("Tatoeba", box, StringComparison.Ordinal);
        Assert.Contains("<strong>Kanji of the day</strong> <span lang=\"ja\">日</span>", html, StringComparison.Ordinal);
        Assert.Contains("毎日歩きます。", _chat.Prompts.Single(p => p.StartsWith("The kanji of the day", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task People_s_own_translation_wins_over_the_model_s()
    {
        var routine = Create(kanji: """{"enabled":true}""", language: "Italian");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        await RunAsync(routine);

        var html = Html();
        Assert.Contains("Mi piace la domenica.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Model's Sunday.", html, StringComparison.Ordinal);
        Assert.Contains("Il kanji del giorno", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_issue_explains_a_kanji_never_explained_and_strokes_that_are_not_path_data_are_left_out()
    {
        var routine = Create(kanji: """{"enabled":true}""");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];
        await RunAsync(routine);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Bridge reopens")];
        var second = await RunAsync(routine);

        Assert.EndsWith(", kanji 月.", second.Summary, StringComparison.Ordinal);
        var html = File.ReadAllText(Directory.GetFiles(_folder, "*N002*.html").Single());
        Assert.Contains("<div class=\"glyph\" lang=\"ja\">月</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_its_dictionaries_the_paper_says_where_they_go_and_prints_anyway()
    {
        var routine = Create(kanji: """{"enabled":true}""");
        _kanji.Available = false;
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("Kanji of the day: its dictionaries are not in /kanji-data", run.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"kanji\"", Html(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_that_cannot_explain_leaves_the_dictionary_s_english()
    {
        var routine = Create(kanji: """{"enabled":true}""");
        _chat.Kanji = "not json";
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        var run = await RunAsync(routine);

        var box = Html()[Html().IndexOf("<section class=\"kanji\">", StringComparison.Ordinal)..];
        Assert.Contains("<p class=\"meaning\">day, sun</p>", box, StringComparison.Ordinal);
        Assert.Contains("I walk every day.", box, StringComparison.Ordinal);
        Assert.Contains("the dictionary's English stays", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_kanji_unless_the_settings_ask_for_it()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        await RunAsync(routine);

        Assert.DoesNotContain("class=\"kanji\"", Html(), StringComparison.Ordinal);
        Assert.DoesNotContain(_chat.Prompts, p => p.StartsWith("The kanji of the day", StringComparison.Ordinal));
    }

    [Fact]
    public void Furigana_gives_each_kanji_its_reading_or_one_over_the_word_and_escapes_the_rest()
    {
        Assert.Equal("<ruby>場<rt>ば</rt>所<rt>しょ</rt></ruby>に<b>行</b>&lt;", PaperPage.Furigana("[場所|ば|しょ]に行<", "行"));
        Assert.Equal("<ruby>今<b>日</b><rt>きょう</rt></ruby>は", PaperPage.Furigana("[今日|きょう]は", "日"));
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

    [Fact]
    public void Titles_sharing_their_main_words_tell_the_same_story()
    {
        Assert.True(MorningPaper.SameStory("Downtown tram line opens today", "Tram line opens downtown"));
        Assert.False(MorningPaper.SameStory("Tram line opens downtown", "Cat show on Sunday"));
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

    /// <summary>Opinions are not news, strikes matter, cats interest the reader, reopenings are good news.</summary>
    private sealed class FakeDecider : IDecider
    {
        public int Asked { get; private set; }

        public bool Available => true;

        public Task<IReadOnlyDictionary<string, Decision>> DecideAsync(string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
        {
            Asked++;
            bool Has(string word) => state.Contains(word, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, Decision>>(new Dictionary<string, Decision>
            {
                ["news"] = new(Has("Opinion") ? "false" : "true", 0.9, false),
                ["importance"] = new(Has("strike") ? "high" : "low", 0.9, false),
                ["interest"] = new(Has("Cat") || Has("Tram") || Has("strike") || Has("bridge") ? "true" : "false", 0.9, false),
                ["hopeful"] = new(Has("reopens") ? "true" : "false", 0.9, false),
            });
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Prompts { get; } = [];

        public string? Headline { get; set; }

        /// <summary>What it answers about the kanji of the day, when not the usual.</summary>
        public string? Kanji { get; set; }

        /// <summary>One article from all the section's news.</summary>
        public bool MergeAll { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Prompts.Add(ask.User);
            if (ask.User.StartsWith("The kanji of the day", StringComparison.Ordinal))
            {
                return Task.FromResult(Kanji ?? """{"meaning":"day, sun","hint":"A window with the sun in it.","words":["each day"],"sentences":["I walk each day.","Model's Sunday."]}""");
            }

            if (ask.User.StartsWith("Today's headlines", StringComparison.Ordinal))
            {
                return Task.FromResult("""{"title":"A calm morning","text":"Coffee first."}""");
            }

            var titles = ask.User.Split('\n').Where(l => l.StartsWith('[')).Select(l => l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..]).ToList();
            if (MergeAll && !ask.User.StartsWith("For the front page", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonSerializer.Serialize(new { articles = new[] { new { headline = "Town news", text = "All at once. [1, 2]", sources = Enumerable.Range(1, titles.Count).ToArray() } } }));
            }

            var articles = titles.Select((title, i) => new { headline = Headline ?? $"About {title}", text = $"It happened. [{i + 1}]", sources = Enumerable.Range(1, titles.Count).Append(99).ToArray() });
            return Task.FromResult(JsonSerializer.Serialize(new { articles }));
        }
    }

    private sealed class FakePrinter : IPdfPrinter
    {
        public Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken) => Task.FromResult("%PDF-1.7"u8.ToArray());
    }

    private sealed class FakeImages : IWebImages
    {
        public Task<Uri?> PictureOfAsync(Uri page, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);

        /// <summary>Photos are 1200x675; "tall" ones are previews of documents, 700x1863.</summary>
        public Task<WebImage?> DownloadAsync(Uri picture, CancellationToken cancellationToken) =>
            Task.FromResult<WebImage?>(picture.ToString().Contains("tall", StringComparison.Ordinal)
                ? new WebImage([0x89, 0x50], "image/png", 700, 1863)
                : new WebImage([0xFF, 0xD8, 0xFF], "image/jpeg", 1200, 675));
    }
}
