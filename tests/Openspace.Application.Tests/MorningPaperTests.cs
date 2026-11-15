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

    private Routine Create(int perSection = 2, string? sections = null, string goodNews = "[]", string kanji = "{}", string language = "English", string extra = "")
    {
        var settings = $$"""
            {{{extra}}"title":"Daily Bugle","subtitle":"デイリー","language":"{{language}}","folder":{{JsonSerializer.Serialize(_folder)}},"kanji":{{kanji}},
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
        Assert.Contains("class=\"lead pic-medium\"", html, StringComparison.Ordinal);
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
        var lead = html[html.IndexOf("class=\"lead ", StringComparison.Ordinal)..html.IndexOf("class=\"front-row\"", StringComparison.Ordinal)];
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
        var lead = html[html.IndexOf("class=\"lead ", StringComparison.Ordinal)..html.IndexOf("class=\"front-row\"", StringComparison.Ordinal)];
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
    public async Task A_section_prints_as_many_articles_as_long_and_as_wide_as_its_settings_say()
    {
        var routine = Create(perSection: 3, sections: """
            [{"name":"City","about":"The town.","width":"half","columns":3,"picture":"none","articles":1,"length":"long",
              "feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] =
        [
            Item("Gazette", "Big strike stops the city"), Item("Gazette", "Cat cafe opens", image: new Uri("https://img.example/cafe.jpg")),
            Item("Gazette", "Tram line opens"), Item("Gazette", "Bakery wins a prize"),
        ];

        await RunAsync(routine);

        var html = Html();
        var section = html[html.IndexOf("<section class=\"section", StringComparison.Ordinal)..];
        Assert.StartsWith("<section class=\"section w-half form-articles\">", section, StringComparison.Ordinal);
        Assert.Contains("style=\"column-count: 1\"", section, StringComparison.Ordinal); // nothing after its one story
        Assert.DoesNotContain("<figure>", section, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(section, "<h3"));
        Assert.Contains(_chat.Prompts, p => p.Contains("five to seven sentences", StringComparison.Ordinal));
    }

    [Fact]
    public void The_preview_lays_out_sample_text_as_the_settings_say_without_reading_or_asking_anything()
    {
        var html = PaperPreview.Render("""
            {"title":"Daily Bugle","layout":{"columns":3,"frontPicture":"large","pictures":"small"},"perSection":2,
             "sections":[
               {"name":"City","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]},
               {"name":"Sport","about":"","width":"third","columns":4,"picture":"huge","pictureAt":"beside","articles":5,"length":"short","feeds":[{"name":"G","url":"https://g.example/feed"}]}]}
            """, new DateOnly(2026, 11, 13));

        Assert.Contains("class=\"lead pic-large\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"lead-text\" style=\"column-count: 3\"", html, StringComparison.Ordinal);
        var city = html[html.IndexOf("<section class=\"section w-full form-articles\">", StringComparison.Ordinal)..html.IndexOf("<section class=\"section w-third form-articles\">", StringComparison.Ordinal)];
        Assert.Contains("<article class=\"top beside\"><div class=\"body ruled\" style=\"column-count: 2\"><figure class=\"pic-small\">", city, StringComparison.Ordinal);
        Assert.Contains("column-count: 1", city, StringComparison.Ordinal); // one story after the first
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(city, "City: headline"));
        var sport = html[html.IndexOf("<section class=\"section w-third form-articles\">", StringComparison.Ordinal)..];
        Assert.Matches("<div class=\"opener\"><h2>Sport</h2>\\s*<figure class=\"pic-small\">.*?</figure><h3 class=\"top-head\">[^<]*</h3>\\s*<article class=\"top above\">", sport);
        Assert.Contains("column-count: 1", sport, StringComparison.Ordinal);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Count(sport, "Sport: headline"));
    }

    [Fact]
    public void A_paper_without_a_layout_keeps_the_layout_it_always_had()
    {
        var html = PaperPreview.Render("""{"sections":[{"name":"City","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]}]}""", new DateOnly(2026, 11, 13));

        Assert.Contains("class=\"lead pic-medium\"", html, StringComparison.Ordinal);
        Assert.Contains("<article class=\"top beside\"><div class=\"body ruled\" style=\"column-count: 2\"><figure class=\"pic-medium\">", html, StringComparison.Ordinal);
        Assert.Contains("class=\"columns ruled\" style=\"column-count: 2\"", html, StringComparison.Ordinal);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Count(html, "<h3[^>]*>City: headline"));
    }

    [Fact]
    public async Task A_section_heading_travels_with_its_first_story()
    {
        var routine = Create(perSection: 3);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Cat cafe opens"), Item("Gazette", "Tram line opens")];

        await RunAsync(routine);

        var html = Html();
        var opener = html[html.IndexOf("<div class=\"opener\"><h2>City</h2>", StringComparison.Ordinal)..];
        Assert.Matches("^<div class=\"opener\"><h2>City</h2>\\s*<h3 class=\"top-head\">About[^<]*</h3>\\s*<article class=\"top plain\">.*?</article></div>", opener);
        Assert.Contains("<p class=\"kicker\">Front page</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reader_s_priorities_open_the_paper_before_a_bigger_story()
    {
        var routine = Create(perSection: 2, extra: "\"priorities\":[\"rents and the cost of living\"],");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Summit talks resume in a far capital"), Item("Gazette", "Tenants protest as rents climb")];

        await RunAsync(routine);

        var html = Html();
        var front = html[html.IndexOf("<p class=\"kicker\">Front page</p>", StringComparison.Ordinal)..];
        Assert.StartsWith("<p class=\"kicker\">Front page</p>", front, StringComparison.Ordinal);
        Assert.Contains("About Tenants protest as rents climb", front[..front.IndexOf("</article>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("rents and the cost of living", _decider.Questions[0]["priority"].Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topics_to_avoid_are_never_printed_not_even_as_good_news()
    {
        var routine = Create(goodNews: """["City"]""", extra: "\"avoid\":[\"television shows and celebrities\"],");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Dance show star wins the final")];

        await RunAsync(routine);

        Assert.DoesNotContain("Dance show", string.Join("\n", _chat.Prompts), StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"good\"", Html(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_with_a_condition_prints_only_what_meets_it_in_its_own_style()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","only":"it happened in the town","style":"Tease the local team kindly.",
              "feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Abroad: tram fares fall in a far town"), Item("Gazette", "Tram line opens")];

        await RunAsync(routine);

        var prompts = string.Join("\n", _chat.Prompts);
        Assert.DoesNotContain("Abroad", prompts, StringComparison.Ordinal);
        Assert.Contains("For this section: Tease the local team kindly.", prompts, StringComparison.Ordinal);
        Assert.Equal("Does it meet this condition: it happened in the town?", _decider.Questions[0]["fits"].Instructions);
    }

    [Fact]
    public void Items_with_the_section_s_keywords_make_the_shortlist_before_newer_ones()
    {
        var section = new PaperSettings.Section("Italy", "Italy.", [], Keywords: ["scioper", "caro vita"]);
        var items = Enumerable.Range(1, 14).Select(i => Item("Wire", $"Wire story {i}", hoursAgo: i)).ToList();
        items.Add(Item("Union", "Lo sciopero generale di domani", hoursAgo: 20));
        items.Add(Item("Daily", "Families and the cost", hoursAgo: 21, summary: "Il caro vita pesa sulle famiglie."));

        var shortlist = MorningPaper.Shortlist(items, section);

        Assert.Equal(12, shortlist.Count);
        Assert.Equal(["Lo sciopero generale di domani", "Families and the cost"], shortlist.Take(2).Select(i => i.Title));
        Assert.Equal(10, MorningPaper.Shortlist(items, section with { Keywords = null }).Count(i => i.Source == "Wire"));
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
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
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
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
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
    public async Task The_desk_sends_an_item_to_the_section_it_belongs_to()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
             {"name":"World","about":"Far countries.","feeds":[{"name":"Globe","url":"https://globe.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Cat show on Sunday"), Item("Gazette", "Far away: tram strike in a capital")];
        _feeds.Items["https://globe.example/feed"] = [Item("Globe", "Big strike at the border")];

        var run = await RunAsync(routine);

        var html = Html();
        var world = html[html.IndexOf("<h2>World</h2>", StringComparison.Ordinal)..];
        Assert.Contains("About Far away: tram strike in a capital", world, StringComparison.Ordinal);
        Assert.Contains("City: the desk took 1 of 2 and sent 1 to World.", run.Log, StringComparison.Ordinal);
        Assert.Contains("- World: Far countries.", _chat.Desks[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_desk_sees_the_last_issues_headlines_and_what_other_sections_took()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
             {"name":"Pets","about":"Animals.","feeds":[{"name":"Paws","url":"https://paws.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown"), Item("Gazette", "Cat show on Sunday")];
        await RunAsync(routine);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        _chat.Desks.Clear();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line: first riders")];
        _feeds.Items["https://paws.example/feed"] = [Item("Paws", "Dog park reopens")];
        await RunAsync(routine);

        Assert.Contains("- About Tram line opens downtown", _chat.Desks[0], StringComparison.Ordinal);
        Assert.Contains("- Tram line: first riders", _chat.Desks[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_s_own_topics_never_open_the_paper_over_the_news_of_the_day()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","focus":["the cats of the town"],"feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Cat cafe opens"), Item("Gazette", "Big strike stops the city")];

        await RunAsync(routine);

        var html = Html();
        var lead = html[html.IndexOf("class=\"lead ", StringComparison.Ordinal)..html.IndexOf("class=\"front-row\"", StringComparison.Ordinal)];
        Assert.Contains("About Big strike stops the city", lead, StringComparison.Ordinal);
        Assert.False(_decider.Questions[0].ContainsKey("priority"), "The section's topics are not the reader's priorities.");
        Assert.Equal("Is it mainly about one of these topics: the cats of the town?", _decider.Questions[0]["focus"].Instructions);
    }

    [Fact]
    public async Task A_section_only_for_its_keywords_prints_nothing_else_and_is_left_out_without_them()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
             {"name":"Music","about":"Artists.","keywords":["Caparezza"],"onlyWithKeywords":true,"feeds":[{"name":"Tunes","url":"https://tunes.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city")];
        _feeds.Items["https://tunes.example/feed"] = [Item("Tunes", "Caparezza: a cat song and a tour"), Item("Tunes", "Another band, another cat tour")];

        var run = await RunAsync(routine);

        var prompts = string.Join("\n", _chat.Prompts);
        Assert.Contains("Caparezza: a cat song", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("Another band", prompts, StringComparison.Ordinal);
        Assert.Contains("Music: 1 new items.", run.Log, StringComparison.Ordinal);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Bridge reopens")];
        _feeds.Items["https://tunes.example/feed"] = [Item("Tunes", "A third band's cat record")];
        File.Delete(Directory.GetFiles(_folder, "*.html")[0]);
        await RunAsync(routine);
        Assert.DoesNotContain("<h2>Music</h2>", Html(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_section_may_look_further_back_than_the_paper()
    {
        var routine = Create(sections: """
            [{"name":"City","about":"The town.","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
             {"name":"Rights","about":"Rights.","hoursBack":72,"feeds":[{"name":"Watch","url":"https://watch.example/feed"}]}]
            """);
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Cat show last week", hoursAgo: 50)];
        _feeds.Items["https://watch.example/feed"] = [Item("Watch", "Court frees the strike leaders", hoursAgo: 50)];

        await RunAsync(routine);

        var prompts = string.Join("\n", _chat.Prompts);
        Assert.Contains("Court frees the strike leaders", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("Cat show last week", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_desk_the_shortlist_chooses_and_the_paper_prints()
    {
        var routine = Create();
        _chat.DeskFails = true;
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("City: the desk could not choose (The model is away.), the newest go on.", run.Log, StringComparison.Ordinal);
        Assert.Contains("About Tram line opens downtown", Html(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Tyler, The Creator announces a tour", "Tyler the Creator")]
    [InlineData("Mrs.GREEN APPLE: new single", "Mrs. GREEN APPLE")]
    [InlineData("あいみょん、新曲を発表", "あいみょん")]
    [InlineData("Lo sciopero generale di domani", "scioper")]
    public void Keywords_are_found_whatever_the_spaces_and_punctuation(string title, string keyword) =>
        Assert.True(MorningPaper.Mentions(Item("Tunes", title), [keyword]));

    [Fact]
    public void A_keyword_is_not_found_in_another_name() =>
        Assert.False(MorningPaper.Mentions(Item("Tunes", "Bruno Martini wins the race"), ["Bruno Mars"]));

    [Fact]
    public void The_shortlist_takes_turns_among_outlets_so_the_busiest_never_fills_it()
    {
        var section = new PaperSettings.Section("Sport", "Sport.", []);
        var items = Enumerable.Range(1, 20).Select(i => Item("Club", $"Club chatter {i}", hoursAgo: i)).ToList();
        items.AddRange(Enumerable.Range(1, 3).Select(i => Item("Wire", $"League result {i}", hoursAgo: 10 + i)));

        var shortlist = MorningPaper.Shortlist(items, section);

        Assert.Equal(12, shortlist.Count);
        Assert.Equal(3, shortlist.Count(i => i.Source == "Wire"));
    }

    [Fact]
    public async Task A_printed_page_gives_back_its_headlines_but_not_its_index_or_opening()
    {
        var routine = Create(goodNews: """["City"]""", kanji: """{"enabled":true}""");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city", image: new Uri("https://img.example/strike.jpg")), Item("Gazette", "Bridge reopens"), Item("Gazette", "Cat & mouse show")];
        await RunAsync(routine);

        var headlines = PaperPage.HeadlinesOf(Html()).ToList();

        Assert.Contains("About Big strike stops the city", headlines);
        Assert.Contains("About Bridge reopens", headlines);
        Assert.Contains("About Cat & mouse show", headlines);
        Assert.DoesNotContain("A calm morning", headlines);
        Assert.DoesNotContain("In this issue", headlines);
        Assert.Equal(headlines, MorningPaper.RecentHeadlines(_folder, 3));
    }

    private const string ThreeSections = """
        [{"name":"City","about":"The town.","form":"FORM","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
         {"name":"Pets","about":"Animals.","feeds":[{"name":"Paws","url":"https://paws.example/feed"}]},
         {"name":"Parks","about":"Green.","feeds":[{"name":"Leaf","url":"https://leaf.example/feed"}]}]
        """;

    private void ThreeSectionsNews()
    {
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Opening day for the tram"), Item("Gazette", "Big strike stops the city", image: new Uri("https://img.example/strike.jpg")), Item("Gazette", "Bridge reopens"), Item("Gazette", "Tram fares fall")];
        _feeds.Items["https://paws.example/feed"] = [Item("Paws", "Cat show on Sunday")];
        _feeds.Items["https://leaf.example/feed"] = [Item("Leaf", "Cat naps in the park")];
    }

    [Fact]
    public async Task A_section_in_brief_tells_each_story_in_one_sentence_without_a_picture()
    {
        var routine = Create(perSection: 3, sections: ThreeSections.Replace("FORM", "briefs", StringComparison.Ordinal));
        ThreeSectionsNews();

        await RunAsync(routine);

        var html = Html();
        var city = html[html.IndexOf("<h2>City</h2>", StringComparison.Ordinal)..html.IndexOf("<h2>Pets</h2>", StringComparison.Ordinal)];
        Assert.Contains("form-briefs", html, StringComparison.Ordinal);
        Assert.Contains("<article class=\"first brief\"><h3>", city, StringComparison.Ordinal);
        Assert.Contains("<article class=\"brief\"><h3>", city, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure>", city, StringComparison.Ordinal);
        Assert.Contains(_chat.Prompts, p => p.Contains("Write one brief per story", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_feature_section_tells_its_main_story_at_length_and_the_rest_in_brief()
    {
        var routine = Create(perSection: 3, sections: ThreeSections.Replace("FORM", "feature", StringComparison.Ordinal));
        ThreeSectionsNews();

        await RunAsync(routine);

        var html = Html();
        var city = html[html.IndexOf("<h2>City</h2>", StringComparison.Ordinal)..html.IndexOf("<h2>Pets</h2>", StringComparison.Ordinal)];
        Assert.Contains("<h3 class=\"top-head\">", city, StringComparison.Ordinal);
        Assert.Contains("<article class=\"brief\"><h3>", city, StringComparison.Ordinal);
        Assert.Contains(_chat.Prompts, p => p.Contains("five to seven sentences. Each other story", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_explained_story_comes_with_its_questions_and_answers()
    {
        var routine = Create(perSection: 3, sections: ThreeSections.Replace("FORM", "explainer", StringComparison.Ordinal));
        ThreeSectionsNews();

        await RunAsync(routine);

        var html = Html();
        Assert.Contains("<dl class=\"qa\"><dt>Why now?</dt><dd>Because.</dd></dl>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Dropped.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_the_space_follows_the_news_the_big_story_spans_the_page_and_quiet_sections_go_in_brief_together()
    {
        var routine = Create(perSection: 2, sections: """
            [{"name":"Pets","about":"Animals.","width":"half","feeds":[{"name":"Paws","url":"https://paws.example/feed"}]},
             {"name":"City","about":"The town.","width":"third","picture":"none","feeds":[{"name":"Gazette","url":"https://gazette.example/feed"}]},
             {"name":"Parks","about":"Green.","feeds":[{"name":"Leaf","url":"https://leaf.example/feed"}]}]
            """, extra: "\"layout\":{\"adapt\":true},");
        // The strike opens the paper; the talks are the biggest story left, in City.
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Strike talks resume in the town hall", image: new Uri("https://img.example/talks.jpg")), Item("Gazette", "Tram fares fall")];
        _feeds.Items["https://paws.example/feed"] = [Item("Paws", "Cat show on Sunday")];
        _feeds.Items["https://leaf.example/feed"] = [Item("Leaf", "Cat naps in the park")];

        await RunAsync(routine);

        var html = Html();
        var sections = html[html.IndexOf("<div class=\"sections\">", StringComparison.Ordinal)..];
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(sections, "^<div class=\"sections\">\\s*<section class=\"section w-full form-articles\">\\s*<div class=\"opener\"><h2>City</h2>"), sections[..Math.Min(900, sections.Length)]);
        Assert.Contains("<figure class=\"pic-medium\">", sections[..sections.IndexOf("<h2>Pets</h2>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("<section class=\"section w-half form-briefs\">\n  <div class=\"opener\"><h2>Pets</h2>", sections, StringComparison.Ordinal);
        Assert.Contains("<section class=\"section w-half form-briefs\">\n  <div class=\"opener\"><h2>Parks</h2>", sections, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "full")]
    [InlineData(2, "half half")]
    [InlineData(3, "third third third")]
    [InlineData(4, "half half half half")]
    [InlineData(5, "third third third half half")]
    [InlineData(7, "third third third half half half half")]
    public void Quiet_sections_fill_whole_rows(int count, string widths) =>
        Assert.Equal(widths, string.Join(' ', MorningPaper.RowsOf(count)));

    [Fact]
    public async Task The_writer_is_told_the_rules_of_a_good_article_and_the_phrases_never_to_write()
    {
        var routine = Create(extra: "\"neverWrite\":[\"Grab a coffee\",\" \"],");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];

        await RunAsync(routine);

        var system = _chat.Systems.Last();
        Assert.Contains("Never write: \"Grab a coffee\".", system, StringComparison.Ordinal);
        Assert.Contains("ends on a fact", system, StringComparison.Ordinal);
        Assert.Contains("for the first time", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_opening_note_starts_from_the_news_and_never_repeats_the_last_ones()
    {
        var routine = Create();
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Tram line opens downtown")];
        await RunAsync(routine);
        Assert.DoesNotContain("The last notes", _chat.Prompts.Last(p => p.StartsWith("Today's headlines", StringComparison.Ordinal)), StringComparison.Ordinal);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Bridge reopens")];
        await RunAsync(routine);

        var opening = _chat.Prompts.Last(p => p.StartsWith("Today's headlines", StringComparison.Ordinal));
        Assert.Contains("start from the day's biggest story", opening, StringComparison.Ordinal);
        Assert.Contains("- A calm morning: Coffee first.", opening, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Proofreading_corrects_every_article_or_leaves_them_all_as_written()
    {
        var routine = Create(goodNews: """["City"]""", extra: "\"proofread\":true,");
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Bridge reopens"), Item("Gazette", "Cat show on Sunday")];

        var run = await RunAsync(routine);

        var html = Html();
        Assert.Contains("<h2>Proofread 1</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<h3>Proofread 2</h3>", html, StringComparison.Ordinal);
        Assert.Contains("Proofread 3</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Corrected.</p>", html, StringComparison.Ordinal);
        Assert.Contains("Proofread: 3 of 3 articles corrected.", run.Log, StringComparison.Ordinal);
        Assert.Contains("Proofread 3", _chat.Prompts.Last(p => p.StartsWith("Today's headlines", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Proofreading_that_loses_an_article_changes_nothing()
    {
        var routine = Create(extra: "\"proofread\":true,");
        _chat.ProofShort = true;
        _feeds.Items["https://gazette.example/feed"] = [Item("Gazette", "Big strike stops the city"), Item("Gazette", "Cat show on Sunday")];

        var run = await RunAsync(routine);

        Assert.DoesNotContain("Proofread 1", Html(), StringComparison.Ordinal);
        Assert.Contains("Proofreading gave back 1 of 2 articles: they stay as written.", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_shows_each_section_in_its_form()
    {
        var html = PaperPreview.Render("""
            {"sections":[{"name":"City","about":"","form":"explainer","articles":3,"feeds":[{"name":"G","url":"https://g.example/feed"}]},
                         {"name":"Pets","about":"","form":"briefs","feeds":[{"name":"P","url":"https://p.example/feed"}]}]}
            """, new DateOnly(2026, 11, 15));

        Assert.Contains("<dl class=\"qa\"><dt>A question a reader would ask, number 1?</dt>", html, StringComparison.Ordinal);
        Assert.Contains("<article class=\"brief\"><h3>City: headline of story 2</h3>", html, StringComparison.Ordinal);
        var pets = html[html.IndexOf("<h2>Pets</h2>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("<figure>", pets, StringComparison.Ordinal);
    }

    [Fact]
    public void A_printed_page_gives_back_its_opening_note()
    {
        var html = PaperPreview.Render("""{"sections":[{"name":"City","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]}]}""", new DateOnly(2026, 11, 15));

        Assert.Equal("Good morning", PaperPage.OpeningOf(html)!.Title);
        Assert.Null(PaperPage.OpeningOf("<p>No note.</p>"));
    }

    [Theory]
    [InlineData(3, 2, 2)]
    [InlineData(3, 3, 3)]
    [InlineData(3, 4, 2)]
    [InlineData(3, 5, 3)]
    [InlineData(4, 6, 3)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 0, 1)]
    public void Columns_never_stay_empty_on_a_day_with_less_news(int columns, int stories, int expected) =>
        Assert.Equal(expected, PaperPage.ColumnsFor(columns, stories));

    [Fact]
    public void The_front_picture_may_sit_beside_its_story_and_the_index_lists_each_section_s_main_story()
    {
        var html = PaperPreview.Render("""
            {"layout":{"frontPicture":"large","frontPictureAt":"beside"},"perSection":3,
             "sections":[{"name":"City","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]}]}
            """, new DateOnly(2026, 11, 15));

        Assert.Contains("<article class=\"lead beside pic-large\">", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"body ruled\" style=\"column-count: 2\"><figure class=\"pic-large\">", html, StringComparison.Ordinal);
        Assert.Contains("<li><strong>City</strong> City: headline of story 1</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("City: headline of story 2</li>", html, StringComparison.Ordinal);
        Assert.Contains("for (const body of document.querySelectorAll('.beside .body'))", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_width_sections_are_plain_blocks_and_narrow_ones_share_rows()
    {
        var html = PaperPreview.Render("""
            {"sections":[{"name":"City","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]},
                         {"name":"Pets","about":"","width":"half","feeds":[{"name":"G","url":"https://g.example/feed"}]},
                         {"name":"Parks","about":"","width":"half","feeds":[{"name":"G","url":"https://g.example/feed"}]},
                         {"name":"Sport","about":"","feeds":[{"name":"G","url":"https://g.example/feed"}]}]}
            """, new DateOnly(2026, 11, 15));

        var sections = html[html.IndexOf("<div class=\"sections\">", StringComparison.Ordinal)..];
        Assert.Matches("^<div class=\"sections\">\\s*<section class=\"section w-full", sections);
        Assert.Matches("</section>\\s*<div class=\"row\">\\s*<section class=\"section w-half form-articles\">\\s*<div class=\"opener\"><h2>Pets</h2>", sections);
        Assert.Matches("</section>\\s*</div>\\s*<section class=\"section w-full form-articles\">\\s*<div class=\"opener\"><h2>Sport</h2>", sections);
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

        public List<IReadOnlyDictionary<string, DecisionQuestion>> Questions { get; } = [];

        public bool Available => true;

        public Task<IReadOnlyDictionary<string, Decision>> DecideAsync(string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
        {
            Asked++;
            bool Has(string word) => state.Contains(word, StringComparison.OrdinalIgnoreCase);
            Questions.Add(questions);
            var answers = new Dictionary<string, Decision>
            {
                ["news"] = new(Has("Opinion") ? "false" : "true", 0.9, false),
                ["importance"] = new(Has("strike") || Has("Summit") ? "high" : Has("reopens") || Has("protest") || Has("wins") ? "medium" : "low", 0.9, false),
                ["interest"] = new(Has("Cat") || Has("Tram") || Has("strike") || Has("bridge") || Has("protest") ? "true" : "false", 0.9, false),
                ["hopeful"] = new(Has("reopens") || Has("wins") ? "true" : "false", 0.9, false),
            };
            if (questions.ContainsKey("priority"))
            {
                answers["priority"] = new(Has("protest") || Has("rent") ? "true" : "false", 0.9, false);
            }

            if (questions.ContainsKey("focus"))
            {
                answers["focus"] = new(Has("Cat") ? "true" : "false", 0.9, false);
            }

            if (questions.ContainsKey("avoid"))
            {
                answers["avoid"] = new(Has("show") ? "true" : "false", 0.9, false);
            }

            if (questions.ContainsKey("fits"))
            {
                answers["fits"] = new(Has("Abroad") ? "false" : "true", 0.9, false);
            }

            return Task.FromResult<IReadOnlyDictionary<string, Decision>>(answers);
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        /// <summary>What the model was asked to write: articles, the opening note, the kanji.</summary>
        public List<string> Prompts { get; } = [];

        /// <summary>What each section's desk was asked.</summary>
        public List<string> Desks { get; } = [];

        public bool DeskFails { get; set; }

        /// <summary>The system prompt of every ask.</summary>
        public List<string> Systems { get; } = [];

        /// <summary>Proofreading gives back one article less than it was given.</summary>
        public bool ProofShort { get; set; }

        public string? Headline { get; set; }

        /// <summary>What it answers about the kanji of the day, when not the usual.</summary>
        public string? Kanji { get; set; }

        /// <summary>One article from all the section's news.</summary>
        public bool MergeAll { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Systems.Add(ask.System);
            if (ask.User.StartsWith("Proofread these ", StringComparison.Ordinal))
            {
                Prompts.Add(ask.User);
                var count = int.Parse(ask.User.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture) - (ProofShort ? 1 : 0);
                var proofed = Enumerable.Range(1, count).Select(i => new { headline = $"Proofread {i}", text = "Corrected." });
                return Task.FromResult(JsonSerializer.Serialize(new { articles = proofed }));
            }

            if (ask.User.StartsWith("The desk of the section", StringComparison.Ordinal))
            {
                Desks.Add(ask.User);
                using (var schema = JsonDocument.Parse(ask.JsonSchema!))
                {
                    Assert.Contains("City", schema.RootElement.GetRawText(), StringComparison.Ordinal);
                }

                if (DeskFails)
                {
                    throw new HttpRequestException("The model is away.");
                }

                // Every item for its own section, but news from far away goes to World when there is one.
                var own = ask.User["The desk of the section ".Length..ask.User.IndexOf(':', StringComparison.Ordinal)];
                var world = ask.User.Contains("\n- World:", StringComparison.Ordinal);
                var picks = ask.User.Split('\n').Where(l => l.StartsWith('['))
                    .Select(l => new { item = int.Parse(l[1..l.IndexOf(']', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture), section = world && l.Contains("Far away", StringComparison.Ordinal) ? "World" : own });
                return Task.FromResult(JsonSerializer.Serialize(new { picks }));
            }

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

            var explained = ask.User.Contains("explained simply", StringComparison.Ordinal);
            var articles = titles.Select((title, i) => new
            {
                headline = Headline ?? $"About {title}",
                text = $"It happened. [{i + 1}]",
                sources = Enumerable.Range(1, titles.Count).Append(99).ToArray(),
                questions = explained && i == 0 ? new[] { new { question = "Why now?", answer = "Because." }, new { question = " ", answer = "Dropped." } } : null,
            });
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
