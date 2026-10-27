using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// A personal paper, every morning. News of the last hours from the outlets the person
/// chose, never printed before, judged one by one by the decider (is it news, does it
/// matter, would the reader care, is it good news) and weighed by how many outlets carry
/// it. The day's most important story opens the front page with a large picture, next to
/// the good news of the day; each section follows with its best stories, written up by a
/// local model from the items alone and citing every outlet that told them. Laid out as a
/// paper, printed to PDF, numbered, kept and delivered. When asked, it closes with a kanji
/// of the day: one never explained, with its readings, strokes, words and sentences.
/// </summary>
public sealed partial class MorningPaper(
    ILocalChat chat, IDecider decider, IFeedReader feeds, IPdfPrinter printer, IWebImages images, IKanjiBook kanjiBook) : IRoutineKind
{
    public string Key => "morning-paper";

    public string Name => "Morning paper";

    public string Description => "News from the outlets you choose, judged and written up by local models, as a numbered PDF paper with a front page.";

    public string DefaultSettings => """
        {
          "title": "Morning paper",
          "subtitle": "",
          "language": "English",
          "model": "local/gemma",
          "folder": "~/Documents/Morning paper",
          "channel": "",
          "reader": "Who reads it and what they care about, in a few sentences.",
          "rules": [
            "Keep facts apart from opinions, and never add facts that are not in the items.",
            "Plain words, short sentences, no jargon. A light joke now and then, never about tragedies."
          ],
          "hoursBack": 24,
          "perSection": 4,
          "goodNews": ["World"],
          "kanji": { "enabled": false, "fromGrade": 1 },
          "sections": [
            { "name": "World", "about": "What happened in the world.", "feeds": [ { "name": "Example News", "url": "https://news.example/feed" } ] }
          ]
        }
        """;

    /// <summary>Items read per feed at most: some feeds carry their whole history.</summary>
    private const int PerFeed = 15;

    /// <summary>Items the decider weighs per section, at most: it takes over ten seconds each on a processor.</summary>
    private const int Candidates = 12;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = PaperSettings.Read(context.Settings);
        var culture = CultureOf(settings.Language);
        var since = context.Now - TimeSpan.FromHours(settings.HoursBack);

        var pool = new List<Candidate>();
        foreach (var section in settings.Sections)
        {
            var fresh = await GatherAsync(context, section, since, pool, cancellationToken).ConfigureAwait(false);
            pool.AddRange(fresh.OrderByDescending(i => i.Published).Take(Candidates).Select(i => new Candidate(i, section)));
        }

        if (pool.Count == 0)
        {
            return "Nothing new to print today.";
        }

        Relate(pool);
        await JudgeAsync(context, settings, pool, cancellationToken).ConfigureAwait(false);

        var news = pool.Where(c => c.Judgement.News >= 0.5).ToList();
        var lead = news.OrderByDescending(c => c.Score).FirstOrDefault()
            ?? pool.OrderByDescending(c => c.Related.Count).ThenByDescending(c => c.Item.Published).First();
        var used = new HashSet<Candidate>(lead.Related.Append(lead));
        var good = news.Where(c => !used.Contains(c) && c.Judgement.Hopeful >= 0.6 && settings.GoodNewsFrom(c.Section))
            .OrderByDescending(c => c.Judgement.Hopeful * c.Judgement.News).FirstOrDefault();
        if (good is not null)
        {
            used.UnionWith(good.Related.Append(good));
        }

        var front = await WriteLeadAsync(context, settings, lead, cancellationToken).ConfigureAwait(false);
        var goodNews = good is null ? null : await WriteGoodNewsAsync(context, settings, good, cancellationToken).ConfigureAwait(false);

        var sections = new List<PaperSection>();
        foreach (var section in settings.Sections)
        {
            var chosen = new List<Candidate>();
            foreach (var candidate in pool
                         .Where(c => c.Section == section && !used.Contains(c) && c.Judgement.News >= 0.5 && c.Judgement.Interest >= 0.35)
                         .OrderByDescending(c => c.Score))
            {
                if (chosen.Count == settings.PerSection)
                {
                    break;
                }

                if (used.Contains(candidate))
                {
                    continue;
                }

                chosen.Add(candidate);
                used.UnionWith(candidate.Related.Append(candidate));
            }

            if (chosen.Count == 0)
            {
                context.Write($"{section.Name}: nothing worth printing.");
                continue;
            }

            var items = Items(chosen);
            var articles = await WriteSectionAsync(context, settings, section, items, cancellationToken).ConfigureAwait(false);
            if (articles.Count > 0)
            {
                var picture = await PictureAsync(items.Take(chosen[0].Related.Count + 1), cancellationToken).ConfigureAwait(false);
                sections.Add(new PaperSection(section.Name, articles, items, picture));
            }
        }

        if (front is null && sections.Count == 0)
        {
            throw new InvalidOperationException("The local model could not write any article: see the log.");
        }

        foreach (var candidate in used)
        {
            await context.MarkAsync(candidate.Item.Link.ToString(), SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        var kanji = await KanjiOfTheDayAsync(context, settings, cancellationToken).ConfigureAwait(false);
        if (kanji is not null)
        {
            var key = KanjiCourse.KeyOf(kanji.Entry.Literal);
            await context.MeetAsync(key, kanji.Entry.Literal, kanji.Entry.Literal, cancellationToken).ConfigureAwait(false);
            await context.MarkAsync(key, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        var opening = await OpeningAsync(context, settings, front, sections, cancellationToken).ConfigureAwait(false);
        var folder = Expand(settings.Folder);
        Directory.CreateDirectory(folder);
        var number = NextNumber(folder);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone).DateTime);
        var html = PaperPage.Render(settings, culture, number, today, opening, front, goodNews, sections, kanji);

        var name = $"{Slug(settings.Title)}-N{number:000}-{today:yyyy-MM-dd}";
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".html"), html, cancellationToken).ConfigureAwait(false);
        var pdf = Path.Combine(folder, name + ".pdf");
        await File.WriteAllBytesAsync(pdf, await printer.PrintAsync(html, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        context.Write($"Printed {Path.GetFileName(pdf)}");

        var count = sections.Sum(s => s.Articles.Count) + (front is null ? 0 : 1) + (goodNews is null ? 0 : 1);
        var caption = $"{settings.Title} · N. {number} · {today.ToString("dddd d MMMM yyyy", culture)}\n{front?.Article.Headline ?? opening.Title}";
        await context.DeliverAsync(caption, pdf, cancellationToken).ConfigureAwait(false);
        return $"N. {number}: {count} articles in {sections.Count} sections{(kanji is null ? string.Empty : $", kanji {kanji.Entry.Literal}")}.";
    }

    /// <summary>The section's news since <paramref name="since"/> that the paper never printed.</summary>
    private async Task<List<FeedItem>> GatherAsync(
        RoutineContext context, PaperSettings.Section section, DateTimeOffset since, List<Candidate> pool, CancellationToken cancellationToken)
    {
        var fresh = new List<FeedItem>();
        foreach (var feed in section.Feeds)
        {
            IReadOnlyList<FeedItem> items;
            try
            {
                items = await feeds.ReadAsync(new Uri(feed.Url), feed.Name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"{feed.Name}: could not be read ({ex.Message}).");
                continue;
            }

            var recent = items.Where(i => i.Published is null || i.Published >= since).OrderByDescending(i => i.Published).Take(PerFeed).ToList();
            foreach (var item in recent)
            {
                if (!fresh.Exists(f => f.Link == item.Link) && !pool.Exists(c => c.Item.Link == item.Link)
                    && await context.MeetAsync(item.Link.ToString(), RoutineContext.Fingerprint(item.Title), item.Title, cancellationToken).ConfigureAwait(false))
                {
                    fresh.Add(item);
                }
            }
        }

        context.Write($"{section.Name}: {fresh.Count} new items.");
        return fresh;
    }

    /// <summary>Links each item to the others telling the same story, by the words their titles share.</summary>
    private static void Relate(List<Candidate> pool)
    {
        var words = pool.ToDictionary(c => c, c => Words(c.Item.Title));
        foreach (var candidate in pool)
        {
            candidate.Related.AddRange(pool.Where(other => other != candidate && Similar(words[candidate], words[other])));
        }
    }

    private static HashSet<string> Words(string title) =>
        [.. WordParts().Split(title.ToLowerInvariant()).Where(w => w.Length >= 4)];

    /// <summary>Two titles on the same story share at least two longer words and a third of them.</summary>
    public static bool SameStory(string first, string second) => Similar(Words(first), Words(second));

    private static bool Similar(HashSet<string> a, HashSet<string> b)
    {
        var shared = a.Count(b.Contains);
        return shared >= 2 && shared / (double)(a.Count + b.Count - shared) >= 0.3;
    }

    /// <summary>
    /// Asks the decider about each item: is it news, does it matter today, would the reader
    /// care, is it good news. Without a decider every item counts as plain news.
    /// </summary>
    private async Task JudgeAsync(RoutineContext context, PaperSettings settings, List<Candidate> pool, CancellationToken cancellationToken)
    {
        if (!decider.Available)
        {
            context.Write("No decider: the newest news goes first.");
            return;
        }

        var questions = new Dictionary<string, DecisionQuestion>
        {
            ["news"] = new("boolean", "Is this a news report about something new that happened in the last day or two? Opinions, interviews, analyses, reviews, events announced long ahead and plain statements of how things are do not count."),
            ["importance"] = new("choice", "How important is this news today, for the people of the country or of the world it concerns?",
                [new("high", "Many people talk about it or are touched by it today."), new("medium", "It matters to some people."), new("low", "A curiosity or a niche fact.")]),
            ["interest"] = new("boolean", $"Would this reader want to read it? The reader: {settings.Reader}"),
            ["hopeful"] = new("boolean", "Is this good news: something positive, a solution, a step forward, a reason for hope?"),
        };

        var judged = 0;
        foreach (var candidate in pool)
        {
            context.Progress($"Judging {judged + 1} of {pool.Count} news items");
            try
            {
                var answers = await decider.DecideAsync(
                    $"{candidate.Item.Source}, section {candidate.Section.Name}: {candidate.Item.Title}\n\n{candidate.Item.Summary}", questions, cancellationToken).ConfigureAwait(false);
                candidate.Judgement = new Judgement(Yes(answers, "news"), Importance(answers), Yes(answers, "interest"), Yes(answers, "hopeful"));
                judged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"The decider failed ({ex.Message}): the rest counts as plain news.");
                break;
            }
        }

        context.Write($"Judged {judged} of {pool.Count} items.");
    }

    private static double Yes(IReadOnlyDictionary<string, Decision> answers, string key) =>
        !answers.TryGetValue(key, out var answer) || answer.Abstained ? 0.5
        : answer.Answer == "true" ? answer.Confidence : 1 - answer.Confidence;

    private static double Importance(IReadOnlyDictionary<string, Decision> answers) =>
        !answers.TryGetValue("importance", out var answer) || answer.Abstained ? 0.5
        : (answer.Answer switch { "high" => 1.0, "medium" => 0.6, _ => 0.2 } * answer.Confidence) + (0.5 * (1 - answer.Confidence));

    /// <summary>The chosen items, each followed by the other outlets' items on the same story.</summary>
    private static List<FeedItem> Items(IEnumerable<Candidate> chosen) =>
        [.. chosen.SelectMany(c => c.Related.Prepend(c)).Select(c => c.Item).DistinctBy(i => i.Link)];

    private async Task<PaperLead?> WriteLeadAsync(RoutineContext context, PaperSettings settings, Candidate lead, CancellationToken cancellationToken)
    {
        var items = Items([lead]);
        var article = (await WriteAsync(
            context, settings, items, "the front page",
            "Write the day's opening story from all these items: a headline, then five or six sentences that say what happened, why it matters and what comes next.",
            1, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (article is null)
        {
            return null;
        }

        return new PaperLead(lead.Section.Name, article, items, await PictureAsync(items, cancellationToken).ConfigureAwait(false));
    }

    private async Task<PaperLead?> WriteGoodNewsAsync(RoutineContext context, PaperSettings settings, Candidate good, CancellationToken cancellationToken)
    {
        var items = Items([good]);
        var article = (await WriteAsync(
            context, settings, items, "the good news of the day",
            "Write the good news of the day from these items: a headline and three warm sentences.",
            1, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return article is null ? null : new PaperLead(good.Section.Name, article, items, null);
    }

    private Task<List<PaperArticle>> WriteSectionAsync(
        RoutineContext context, PaperSettings settings, PaperSettings.Section section, List<FeedItem> items, CancellationToken cancellationToken) =>
        WriteAsync(
            context, settings, items, $"the section {section.Name} ({section.About})",
            "Write one short article per story (items about the same fact make one article), each with a headline and two to four sentences.",
            settings.PerSection, cancellationToken);

    private async Task<List<PaperArticle>> WriteAsync(
        RoutineContext context, PaperSettings settings, List<FeedItem> items, string where, string task, int most, CancellationToken cancellationToken)
    {
        var list = string.Join("\n\n", items.Select((item, i) => $"[{i + 1}] {item.Source}: {item.Title}\n{item.Summary}"));
        var user = $"""
            For {where}. The news, numbered:
            {list}

            {task} Give the numbers of the items each article comes from in "sources", all of them, never in the text.
            """;
        try
        {
            var answer = await chat.AskAsync(context, new LocalAsk(settings.Model, Editor(settings), user, ArticlesSchema, MaxTokens: 3000), cancellationToken).ConfigureAwait(false);
            var articles = (JsonSerializer.Deserialize<ArticlesAnswer>(answer, JsonSerializerOptions.Web)?.Articles ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Headline) && !string.IsNullOrWhiteSpace(a.Text))
                .Select(a => a with
                {
                    Text = Markers().Replace(a.Text, string.Empty).Trim(),
                    Sources = [.. (a.Sources ?? []).Where(n => n >= 1 && n <= items.Count).Distinct()],
                })
                .Take(most)
                .ToList();
            context.Write($"{Capitalised(where)}: {articles.Count} articles written.");
            return articles;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{Capitalised(where)}: could not be written ({ex.Message}).");
            return [];
        }
    }

    /// <summary>The first picture of these items: the feed's own, or the one their page declares.</summary>
    private async Task<PaperPicture?> PictureAsync(IEnumerable<FeedItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items.Take(3))
        {
            var address = item.Image ?? await images.PictureOfAsync(item.Link, cancellationToken).ConfigureAwait(false);
            if (address is not null && await images.DownloadAsync(address, cancellationToken).ConfigureAwait(false) is { FitsAFrame: true } picture)
            {
                return new PaperPicture(picture, item.Source);
            }
        }

        return null;
    }

    private async Task<PaperOpening> OpeningAsync(
        RoutineContext context, PaperSettings settings, PaperLead? front, List<PaperSection> sections, CancellationToken cancellationToken)
    {
        var headlines = string.Join("\n", (front is null ? [] : new[] { $"- Front page: {front.Article.Headline}" })
            .Concat(sections.SelectMany(s => s.Articles.Select(a => $"- {s.Name}: {a.Headline}"))));
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model, Editor(settings),
                    $"Today's headlines:\n{headlines}\n\nWrite the paper's short opening note: a title of its own (not the paper's name) and three or four warm sentences that set the day, with a light touch.",
                    OpeningSchema, MaxTokens: 800),
                cancellationToken).ConfigureAwait(false);
            if (JsonSerializer.Deserialize<PaperOpening>(answer, JsonSerializerOptions.Web) is { Title.Length: > 0, Text.Length: > 0 } opening)
            {
                return opening;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"The opening note could not be written ({ex.Message}).");
        }

        return new PaperOpening(settings.Title, string.Empty);
    }

    /// <summary>
    /// The next kanji of the course, looked up in the dictionaries and explained in the
    /// paper's language by the local model: its meaning, a memory trick, what its words mean,
    /// the sentences translated where people did not translate them already.
    /// </summary>
    private async Task<PaperKanji?> KanjiOfTheDayAsync(RoutineContext context, PaperSettings settings, CancellationToken cancellationToken)
    {
        var wanted = KanjiSettings.Read(context.Settings);
        if (!wanted.Enabled)
        {
            return null;
        }

        if (!kanjiBook.Available)
        {
            context.Write($"Kanji of the day: its dictionaries are not in {kanjiBook.Folder} (scripts/openspace kanji-data downloads them).");
            return null;
        }

        try
        {
            var next = KanjiCourse.Next(
                await kanjiBook.CurriculumAsync(cancellationToken).ConfigureAwait(false),
                await context.DealtWithAsync(KanjiCourse.KeyPrefix, cancellationToken).ConfigureAwait(false),
                wanted.FromGrade);
            if (next is null)
            {
                context.Write("Kanji of the day: every kanji of the course is explained.");
                return null;
            }

            if (await kanjiBook.LookUpAsync(next.Literal, cancellationToken).ConfigureAwait(false) is not { } entry)
            {
                context.Write($"Kanji of the day: {next.Literal} is not in the dictionary.");
                return null;
            }

            var kanji = await ExplainAsync(context, settings, entry, cancellationToken).ConfigureAwait(false);
            context.Write($"Kanji of the day: {entry.Literal}.");
            return kanji;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"Kanji of the day: could not be prepared ({ex.Message}).");
            return null;
        }
    }

    private async Task<PaperKanji> ExplainAsync(RoutineContext context, PaperSettings settings, KanjiEntry entry, CancellationToken cancellationToken)
    {
        var language = CultureOf(settings.Language).TwoLetterISOLanguageName;
        var people = entry.Sentences.Select(s => language switch { "it" => s.Italian, "en" => s.English, _ => null }).ToList();
        var fallback = new PaperKanji(
            entry, string.Join(", ", entry.Meanings), string.Empty,
            [.. entry.Words.Select(w => string.Join(", ", w.Meanings.Take(2)))],
            [.. entry.Sentences.Select((s, i) => people[i] ?? s.English ?? s.Italian ?? string.Empty)]);

        var words = string.Join("\n", entry.Words.Select((w, i) => $"[{i + 1}] {w.Written} ({w.Reading}): {string.Join("; ", w.Meanings)}"));
        var sentences = string.Join("\n", entry.Sentences.Select((s, i) => $"[{i + 1}] {s.Text}{(s.English is null ? string.Empty : $" English: {s.English}")}"));
        var user = $"""
            The kanji of the day: {entry.Literal}, {entry.StrokeCount} strokes. Its meanings in English: {string.Join(", ", entry.Meanings)}.
            Readings: on {string.Join(", ", entry.On)}; kun {string.Join(", ", entry.Kun)}.
            {(entry.Parts.Count > 0 ? $"Built from: {string.Join(", ", entry.Parts)}." : "It is not built from other kanji.")}
            Words, numbered:
            {words}
            Sentences, numbered:
            {sentences}

            Write in {settings.Language}: "meaning", the kanji's meanings in a few words; "words", what each word means, in order, in a few words each; "sentences", each sentence translated from the Japanese, in order (the English is there to help); "hint", one sentence that helps remember the kanji from its shape or its parts, told as a memory trick and never as its history.
            """;
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"You help someone learn Japanese. Write in {settings.Language}, and only in {settings.Language}. Never add readings, meanings or facts that are not given.",
                    user, KanjiSchema, MaxTokens: 1200),
                cancellationToken).ConfigureAwait(false);
            var notes = JsonSerializer.Deserialize<KanjiAnswer>(answer, JsonSerializerOptions.Web);
            if (notes is null || string.IsNullOrWhiteSpace(notes.Meaning))
            {
                return fallback;
            }

            return fallback with
            {
                Meaning = notes.Meaning.Trim(),
                Hint = notes.Hint?.Trim() ?? string.Empty,
                WordMeanings = [.. fallback.WordMeanings.Select((m, i) => notes.Words?.ElementAtOrDefault(i) is { Length: > 0 } said ? said.Trim() : m)],
                Translations = [.. fallback.Translations.Select((t, i) => people[i] ?? (notes.Sentences?.ElementAtOrDefault(i) is { Length: > 0 } said ? said.Trim() : t))],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"Kanji of the day: the local model could not explain it ({ex.Message}), the dictionary's English stays.");
            return fallback;
        }
    }

    private const string KanjiSchema = """
        {"type":"object","properties":{"meaning":{"type":"string"},"hint":{"type":"string"},
          "words":{"type":"array","items":{"type":"string"}},"sentences":{"type":"array","items":{"type":"string"}}},
          "required":["meaning","hint","words","sentences"]}
        """;

    private sealed record KanjiAnswer(string? Meaning, string? Hint, List<string>? Words, List<string>? Sentences);

    private static string Editor(PaperSettings settings) => $"""
        You are the editor of "{settings.Title}", a personal paper. Write in {settings.Language}, and only in {settings.Language}.
        Who reads it: {settings.Reader}
        Rules:
        {string.Join("\n", settings.Rules.Select(r => $"- {r}"))}
        - Use only what the news items say: never invent facts, names, numbers or quotes.
        """;

    private const string ArticlesSchema = """
        {"type":"object","properties":{"articles":{"type":"array","items":{"type":"object",
          "properties":{"headline":{"type":"string"},"text":{"type":"string"},"sources":{"type":"array","items":{"type":"integer"}}},
          "required":["headline","text","sources"]}}},"required":["articles"]}
        """;

    private const string OpeningSchema = """
        {"type":"object","properties":{"title":{"type":"string"},"text":{"type":"string"}},"required":["title","text"]}
        """;

    /// <summary>The next issue number: one more than the highest kept in the folder.</summary>
    public static int NextNumber(string folder) =>
        Directory.EnumerateFiles(folder, "*.pdf")
            .Select(f => IssueNumber().Match(Path.GetFileName(f)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;

    internal static string Expand(string folder) =>
        folder.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder.TrimStart('~', '/')) : folder;

    internal static CultureInfo CultureOf(string language) => language.Trim().ToUpperInvariant() switch
    {
        "ITALIAN" or "ITALIANO" => CultureInfo.GetCultureInfo("it-IT"),
        "JAPANESE" => CultureInfo.GetCultureInfo("ja-JP"),
        "SPANISH" => CultureInfo.GetCultureInfo("es-ES"),
        "FRENCH" => CultureInfo.GetCultureInfo("fr-FR"),
        "GERMAN" => CultureInfo.GetCultureInfo("de-DE"),
        _ => CultureInfo.GetCultureInfo("en-GB"),
    };

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    internal static string Slug(string title) => SlugParts().Replace(title.Trim(), "-").Trim('-') is { Length: > 0 } slug ? slug : "Paper";

    /// <summary>Source markers a model may leave in the text anyway: "[2]", " [1, 3]".</summary>
    [GeneratedRegex(@"\s*\[\d+(?:\s*,\s*\d+)*\]")]
    private static partial Regex Markers();

    [GeneratedRegex(@"-N(\d+)-")]
    private static partial Regex IssueNumber();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex SlugParts();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex WordParts();

    private sealed record ArticlesAnswer(List<PaperArticle>? Articles);

    /// <summary>What the decider said about an item, each from 0 to 1.</summary>
    private sealed record Judgement(double News, double Importance, double Interest, double Hopeful)
    {
        public static readonly Judgement Unknown = new(0.6, 0.5, 0.5, 0);
    }

    private sealed class Candidate(FeedItem item, PaperSettings.Section section)
    {
        public FeedItem Item { get; } = item;

        public PaperSettings.Section Section { get; } = section;

        /// <summary>Items of other outlets on the same story.</summary>
        public List<Candidate> Related { get; } = [];

        public Judgement Judgement { get; set; } = Judgement.Unknown;

        /// <summary>How much it deserves a place: importance first, then the reader's interest, then how many outlets carry it.</summary>
        public double Score => (Judgement.Importance * 0.45) + (Judgement.Interest * 0.35) + (Math.Min(Related.Count, 3) / 3.0 * 0.2);
    }
}

/// <param name="Sources">Numbers of the items it comes from, from 1.</param>
public sealed record PaperArticle(string Headline, string Text, IReadOnlyList<int>? Sources);

public sealed record PaperOpening(string Title, string Text);

/// <param name="Credit">The outlet the picture comes from.</param>
public sealed record PaperPicture(WebImage Image, string Credit);

/// <summary>A story with a place of its own: the front page's opening, or the good news of the day.</summary>
public sealed record PaperLead(string Section, PaperArticle Article, IReadOnlyList<FeedItem> Items, PaperPicture? Picture);

/// <summary>The kanji of the day with what the local model wrote about it, in the paper's language.</summary>
/// <param name="WordMeanings">One per word of the entry, in order; <paramref name="Translations"/>, one per sentence.</param>
public sealed record PaperKanji(KanjiEntry Entry, string Meaning, string Hint, IReadOnlyList<string> WordMeanings, IReadOnlyList<string> Translations);

/// <param name="Picture">A picture for the section's first story, when one was found.</param>
public sealed record PaperSection(string Name, IReadOnlyList<PaperArticle> Articles, IReadOnlyList<FeedItem> Items, PaperPicture? Picture = null);

/// <summary>What a morning paper routine is told in its settings.</summary>
public sealed record PaperSettings(
    string Title, string Subtitle, string Language, string Model, string Folder, string Reader,
    IReadOnlyList<string> Rules, int HoursBack, int PerSection, IReadOnlyList<PaperSettings.Section> Sections,
    IReadOnlyList<string>? GoodNews = null)
{
    /// <summary>Whether the good news of the day may come from this section: from any, unless some are named.</summary>
    public bool GoodNewsFrom(Section section) =>
        GoodNews is not { Count: > 0 } names || names.Contains(section.Name, StringComparer.OrdinalIgnoreCase);

    public sealed record Feed(string Name, string Url);

    public sealed record Section(string Name, string About, IReadOnlyList<Feed> Feeds);

    public static PaperSettings Read(JsonElement json)
    {
        var read = json.Deserialize<PaperSettings>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The paper has no settings.");
        if (read.Sections is not { Count: > 0 })
        {
            throw new InvalidOperationException("The paper has no sections: add some, each with its feeds.");
        }

        return read with
        {
            Title = string.IsNullOrWhiteSpace(read.Title) ? "Morning paper" : read.Title.Trim(),
            Subtitle = read.Subtitle?.Trim() ?? string.Empty,
            Language = string.IsNullOrWhiteSpace(read.Language) ? "English" : read.Language.Trim(),
            Model = string.IsNullOrWhiteSpace(read.Model) ? "local/gemma" : read.Model.Trim(),
            Folder = string.IsNullOrWhiteSpace(read.Folder) ? "~/Documents/Morning paper" : read.Folder.Trim(),
            Reader = read.Reader?.Trim() ?? string.Empty,
            Rules = read.Rules ?? [],
            HoursBack = read.HoursBack is > 0 and <= 24 * 7 ? read.HoursBack : 24,
            PerSection = read.PerSection is > 0 and <= 10 ? read.PerSection : 4,
            Sections = [.. read.Sections.Where(s => s.Feeds is { Count: > 0 })],
        };
    }
}
