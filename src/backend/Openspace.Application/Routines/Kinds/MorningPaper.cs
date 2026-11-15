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
          "priorities": [],
          "avoid": [],
          "hoursBack": 24,
          "perSection": 4,
          "layout": { "columns": 2, "frontPicture": "medium", "pictures": "medium" },
          "goodNews": ["World"],
          "kanji": { "enabled": false, "fromGrade": 1 },
          "sections": [
            {
              "name": "World", "about": "What happened in the world.",
              "only": "", "keywords": [], "style": "",
              "width": "full", "picture": "medium", "pictureAt": "beside", "length": "normal",
              "feeds": [ { "name": "Example News", "url": "https://news.example/feed" } ]
            }
          ]
        }
        """;

    /// <summary>Items read per feed at most: some feeds carry their whole history.</summary>
    private const int PerFeed = 15;

    /// <summary>Items the decider weighs per section, at most: it takes over ten seconds each on a processor.</summary>
    private const int Candidates = 12;

    /// <summary>Titles a section's desk reads, at most: the busiest outlets take turns with the others.</summary>
    private const int DeskReads = 100;

    /// <summary>Items another section's desk may send to a section, at most.</summary>
    private const int Sent = 4;

    /// <summary>The issues whose headlines the desk is shown, so that a story comes back only with something new.</summary>
    private const int RecentIssues = 3;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = PaperSettings.Read(context.Settings);
        var culture = CultureOf(settings.Language);

        // Every section's news first, so that the desk can send an item to the section it belongs to.
        var met = new HashSet<Uri>();
        var gathered = new List<(PaperSettings.Section Section, List<FeedItem> Fresh)>();
        foreach (var section in settings.Sections)
        {
            gathered.Add((section, await GatherAsync(context, section, context.Now - TimeSpan.FromHours(settings.HoursBackOf(section)), met, cancellationToken).ConfigureAwait(false)));
        }

        // What the paper already told: the last issues' headlines, then what the desk takes today.
        var told = RecentHeadlines(Expand(settings.Folder), RecentIssues);
        var pool = new List<Candidate>();
        var sent = new List<Candidate>();
        foreach (var (section, fresh) in gathered)
        {
            foreach (var (item, to) in await DeskAsync(context, settings, section, fresh, told, cancellationToken).ConfigureAwait(false))
            {
                (to == section ? pool : sent).Add(new Candidate(item, to));
            }

            told.AddRange(pool.Where(c => c.Section == section).Select(c => c.Item.Title));
        }

        // Items sent from another desk join their section, a few at most, and only where they may go.
        foreach (var group in sent.GroupBy(c => c.Section))
        {
            pool.AddRange(group.Where(c => !group.Key.OnlyWithKeywords || Mentions(c.Item, group.Key.Keywords)).Take(Sent));
        }

        if (pool.Count == 0)
        {
            return "Nothing new to print today.";
        }

        Relate(pool);
        await JudgeAsync(context, settings, pool, cancellationToken).ConfigureAwait(false);

        // Never printed: what the reader asked to avoid, and items that do not belong to their section.
        var printable = pool.Where(c => c.Judgement.Avoid < 0.6 && c.Judgement.Fits >= 0.4).ToList();
        if (printable.Count == 0)
        {
            return "Nothing new to print today.";
        }

        var news = printable.Where(c => c.Judgement.News >= 0.5).ToList();
        var lead = news.OrderByDescending(c => c.LeadScore).FirstOrDefault()
            ?? printable.OrderByDescending(c => c.Related.Count).ThenByDescending(c => c.Item.Published).First();
        var used = new HashSet<Candidate>(lead.Related.Append(lead));

        // Good news is about something that matters, not just something pleasant.
        var good = news.Where(c => !used.Contains(c) && c.Judgement.Hopeful >= 0.6 && c.Judgement.Importance >= 0.5 && settings.GoodNewsFrom(c.Section))
            .OrderByDescending(c => c.Judgement.Hopeful * c.Judgement.News * (0.5 + c.Judgement.Importance)).FirstOrDefault();
        if (good is not null)
        {
            used.UnionWith(good.Related.Append(good));
        }

        var front = await WriteLeadAsync(context, settings, lead, cancellationToken).ConfigureAwait(false);
        var goodNews = good is null ? null : await WriteGoodNewsAsync(context, settings, good, cancellationToken).ConfigureAwait(false);

        // First what each section prints, then how much room it gets, then the writing.
        var plans = new List<(PaperSettings.Section Section, List<Candidate> Chosen)>();
        foreach (var section in settings.Sections)
        {
            var chosen = new List<Candidate>();
            foreach (var candidate in printable
                         .Where(c => c.Section == section && !used.Contains(c) && c.Judgement.News >= 0.5 && (c.Judgement.Interest >= 0.35 || c.Judgement.Priority >= 0.6))
                         .OrderByDescending(c => c.Score))
            {
                if (chosen.Count == settings.ArticlesOf(section))
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

            plans.Add((section, chosen));
        }

        var looks = settings.Page.Adapt
            ? Adapt(settings, [.. plans.Select(p => (p.Section, p.Chosen.Max(c => c.Judgement.Importance)))])
            : [.. plans.Select(p => (p.Section, SectionLook.AsSet))];
        var sections = new List<PaperSection>();
        foreach (var (section, look) in looks)
        {
            var chosen = plans.First(p => p.Section == section).Chosen;
            var form = look.Form ?? PaperSettings.FormOf(section);
            var items = Items(chosen);
            var articles = await WriteSectionAsync(context, settings, section, form, items, cancellationToken).ConfigureAwait(false);
            if (articles.Count > 0)
            {
                var size = look.Picture ?? settings.PictureOf(section);
                var picture = size == "none" || form == "briefs"
                    ? null
                    : await PictureAsync(items.Take(chosen[0].Related.Count + 1), cancellationToken).ConfigureAwait(false);
                sections.Add(new PaperSection(section.Name, articles, items, picture, look.Width, form, look.Picture));
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

        if (settings.Proofread)
        {
            (front, goodNews, sections) = await ProofreadAsync(context, settings, front, goodNews, sections, cancellationToken).ConfigureAwait(false);
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
        RoutineContext context, PaperSettings.Section section, DateTimeOffset since, HashSet<Uri> met, CancellationToken cancellationToken)
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
                if (!met.Contains(item.Link)
                    && (!section.OnlyWithKeywords || Mentions(item, section.Keywords))
                    && await context.MeetAsync(item.Link.ToString(), RoutineContext.Fingerprint(item.Title), item.Title, cancellationToken).ConfigureAwait(false))
                {
                    fresh.Add(item);
                    met.Add(item.Link);
                }
            }
        }

        context.Write($"{section.Name}: {fresh.Count} new items.");
        return fresh;
    }

    /// <summary>
    /// The items of a section the decider weighs when the desk cannot choose: those mentioning
    /// one of the section's keywords first, so that what the reader looks for is never crowded
    /// out by the newest wire stories; then the newest, taking turns among the outlets so that
    /// the busiest one never fills every place.
    /// </summary>
    public static List<FeedItem> Shortlist(IEnumerable<FeedItem> fresh, PaperSettings.Section section, int most = Candidates) =>
        [.. fresh
            .GroupBy(i => (Mentions(i, section.Keywords), i.Source))
            .SelectMany(g => g.OrderByDescending(i => i.Published).Select((item, turn) => (item, turn, wanted: g.Key.Item1)))
            .OrderByDescending(x => x.wanted).ThenBy(x => x.turn).ThenByDescending(x => x.item.Published)
            .Select(x => x.item)
            .Take(most)];

    /// <summary>How many items the desk passes to the decider: three per article, within what judging takes.</summary>
    internal static int DeskSize(PaperSettings settings, PaperSettings.Section section) => Math.Clamp(settings.ArticlesOf(section) * 3, 4, Candidates);

    /// <summary>
    /// The section's desk: the local model reads every title the section gathered, with what
    /// the paper told lately, and picks the few worth judging; an item that belongs to another
    /// section is sent there. Without an answer the shortlist chooses instead.
    /// </summary>
    private async Task<List<(FeedItem Item, PaperSettings.Section To)>> DeskAsync(
        RoutineContext context, PaperSettings settings, PaperSettings.Section section, List<FeedItem> fresh, IReadOnlyList<string> told,
        CancellationToken cancellationToken)
    {
        if (fresh.Count == 0)
        {
            return [];
        }

        var most = DeskSize(settings, section);
        fresh = fresh.Count > DeskReads ? Shortlist(fresh, section, DeskReads) : fresh;
        context.Progress($"Choosing the news of {section.Name}");
        try
        {
            var answer = await chat.AskAsync(context, new LocalAsk(settings.Model, Desk(settings), DeskPrompt(settings, section, fresh, told, most), DeskSchema(settings), MaxTokens: 1200), cancellationToken)
                .ConfigureAwait(false);
            var picks = JsonSerializer.Deserialize<DeskAnswer>(answer, JsonSerializerOptions.Web)?.Picks ?? [];
            var chosen = picks
                .Where(p => p.Item >= 1 && p.Item <= fresh.Count)
                .DistinctBy(p => p.Item)
                .Select(p => (Item: fresh[p.Item - 1], To: settings.Sections.FirstOrDefault(s => string.Equals(s.Name, p.Section?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? section))
                .ToList();
            var own = chosen.Where(c => c.To == section).Take(most).ToList();
            var elsewhere = chosen.Where(c => c.To != section).ToList();
            context.Write($"{section.Name}: the desk took {own.Count} of {fresh.Count}{(elsewhere.Count > 0 ? $" and sent {elsewhere.Count} to {string.Join(", ", elsewhere.Select(c => c.To.Name).Distinct())}" : string.Empty)}.");
            return [.. own, .. elsewhere];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{section.Name}: the desk could not choose ({ex.Message}), the newest go on.");
            return [.. Shortlist(fresh, section, most).Select(i => (i, section))];
        }
    }

    internal static string DeskPrompt(PaperSettings settings, PaperSettings.Section section, IReadOnlyList<FeedItem> fresh, IReadOnlyList<string> told, int most)
    {
        var others = settings.Sections.Where(s => s != section).Select(s => $"- {s.Name}: {s.About}{(string.IsNullOrWhiteSpace(s.Only) ? string.Empty : $" Only if {s.Only.Trim()}.")}");
        var items = fresh.Select((item, i) => $"[{i + 1}] {item.Source}: {item.Title}{(string.IsNullOrWhiteSpace(item.Summary) ? string.Empty : $" — {Clip(item.Summary, 120)}")}");
        var looks = (section.Focus ?? []).Concat(section.Keywords is { Count: > 0 } k && !section.OnlyWithKeywords ? [$"news naming {string.Join(", ", k)}"] : []).ToList();
        return $"""
            The desk of the section {section.Name}: {section.About}{(string.IsNullOrWhiteSpace(section.Only) ? string.Empty : $" Only if {section.Only.Trim()}.")}
            {(looks.Count > 0 ? $"It looks out for: {string.Join("; ", looks)}." : string.Empty)}

            The other sections of the paper:
            {(others.Any() ? string.Join('\n', others) : "- none")}

            Already in the paper, in the last issues or today:
            {(told.Count > 0 ? string.Join('\n', told.Select(t => $"- {t}")) : "- nothing yet")}

            Today's items, numbered:
            {string.Join('\n', items)}

            Pick at most {most} items for {section.Name}, the most important first: real news of the last day or two that many people would care about.
            Leave out opinions, interviews, comments and predictions, reviews, discounts and adverts, and statements of how things are.
            Leave out a story already in the paper, unless the item brings a new fact about it; then pick only the item with the new fact.
            Several items on the same story: pick the one that tells it best.
            An item that belongs to one of the other sections instead: pick it with that section's name, it goes there. Leave out what belongs nowhere.
            Give each pick's number in "item" and its section's name in "section".
            """;
    }

    private static string Clip(string text, int most)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= most ? flat : flat[..most].TrimEnd() + "…";
    }

    private static string Desk(PaperSettings settings) => $"""
        You are the editor of "{settings.Title}", a personal paper, choosing today's news for its sections. Answer only with the JSON asked.
        Who reads it: {settings.Reader}
        """;

    private static string DeskSchema(PaperSettings settings) =>
        """{"type":"object","properties":{"picks":{"type":"array","items":{"type":"object","properties":{"item":{"type":"integer"},"section":{"type":"string","enum":"""
        + JsonSerializer.Serialize(settings.Sections.Select(s => s.Name))
        + """}},"required":["item","section"]}}},"required":["picks"]}""";

    private sealed record DeskAnswer(List<DeskPick>? Picks);

    private sealed record DeskPick(int Item, string? Section);

    /// <summary>
    /// One last read of every article for mistakes of grammar and spelling, the facts and the
    /// style left alone. An answer that does not give back every article changes nothing.
    /// </summary>
    private async Task<(PaperLead? Front, PaperLead? Good, List<PaperSection> Sections)> ProofreadAsync(
        RoutineContext context, PaperSettings settings, PaperLead? front, PaperLead? good, List<PaperSection> sections, CancellationToken cancellationToken)
    {
        var all = new List<PaperArticle>();
        all.AddRange(new[] { front?.Article, good?.Article }.OfType<PaperArticle>());
        all.AddRange(sections.SelectMany(s => s.Articles));
        if (all.Count == 0)
        {
            return (front, good, sections);
        }

        context.Progress("Proofreading");
        var list = string.Join("\n\n", all.Select((a, i) => $"[{i + 1}] {a.Headline}\n{a.Text}"));
        try
        {
            var answer = await chat.AskAsync(context, new LocalAsk(
                settings.Model, Editor(settings),
                $"Proofread these {all.Count} articles in {settings.Language}, numbered:\n{list}\n\nCorrect only mistakes of grammar, spelling and agreement, words that do not exist and words not in {settings.Language}. Change nothing else: not the facts, not the order, not the style. Give back every article, in the same order, with its headline and text.",
                ProofSchema, MaxTokens: 8000), cancellationToken).ConfigureAwait(false);
            var read = JsonSerializer.Deserialize<ArticlesAnswer>(answer, JsonSerializerOptions.Web)?.Articles ?? [];
            if (read.Count != all.Count)
            {
                context.Write($"Proofreading gave back {read.Count} of {all.Count} articles: they stay as written.");
                return (front, good, sections);
            }

            var queue = new Queue<PaperArticle>(all.Select((a, i) => a with
            {
                Headline = string.IsNullOrWhiteSpace(read[i].Headline) ? a.Headline : read[i].Headline.Trim(),
                Text = string.IsNullOrWhiteSpace(read[i].Text) ? a.Text : Markers().Replace(read[i].Text, string.Empty).Trim(),
            }));
            var changed = all.Select((a, i) => a.Headline != read[i].Headline?.Trim() || a.Text != read[i].Text?.Trim()).Count(c => c);
            front = front is null ? null : front with { Article = queue.Dequeue() };
            good = good is null ? null : good with { Article = queue.Dequeue() };
            sections = [.. sections.Select(s => s with { Articles = [.. s.Articles.Select(_ => queue.Dequeue())] })];
            context.Write($"Proofread: {changed} of {all.Count} articles corrected.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"Proofreading failed ({ex.Message}): the articles stay as written.");
        }

        return (front, good, sections);
    }

    /// <summary>Under this importance a section is quiet that day: in brief, with the other quiet ones.</summary>
    private const double Quiet = 0.5;

    /// <summary>
    /// The room each section gets when the space follows the news: the section with the
    /// day's most important story spans the page with its picture; quiet sections are told in
    /// brief, together at the end, side by side so that every row is full; the rest keep their
    /// settings. Without two sections nothing changes.
    /// </summary>
    internal static List<(PaperSettings.Section Section, SectionLook Look)> Adapt(PaperSettings settings, IReadOnlyList<(PaperSettings.Section Section, double Weight)> sections)
    {
        if (sections.Count < 2)
        {
            return [.. sections.Select(s => (s.Section, SectionLook.AsSet))];
        }

        var heaviest = sections.MaxBy(s => s.Weight).Section;
        var quiet = sections.Where(s => s.Section != heaviest && s.Weight < Quiet).Select(s => s.Section).ToList();
        var rest = sections.Select(s => s.Section).Where(s => !quiet.Contains(s))
            .Select(s => (s, s == heaviest ? new SectionLook("full", null, settings.PictureOf(s) == "none" ? "medium" : null) : SectionLook.AsSet));
        var widths = RowsOf(quiet.Count);
        return [.. rest, .. quiet.Select((s, i) => (s, new SectionLook(widths[i], "briefs", "none")))];
    }

    /// <summary>Widths that fill whole rows: thirds, then two or four halves for what is left, one alone across.</summary>
    public static List<string> RowsOf(int count) => (count, count % 3) switch
    {
        (0, _) => [],
        (1, _) => ["full"],
        (_, 0) => [.. Enumerable.Repeat("third", count)],
        (_, 2) => [.. Enumerable.Repeat("third", count - 2), "half", "half"],
        _ => [.. Enumerable.Repeat("third", count - 4), "half", "half", "half", "half"],
    };

    /// <summary>The opening notes of the last issues kept in the folder, the newest first.</summary>
    public static List<PaperOpening> RecentOpenings(string folder, int issues) =>
        [.. Issues(folder).Take(issues).Select(f => PaperPage.OpeningOf(File.ReadAllText(f))).OfType<PaperOpening>()];

    private static IEnumerable<string> Issues(string folder) =>
        !Directory.Exists(folder) ? [] : Directory.EnumerateFiles(folder, "*.html")
            .Select(f => (File: f, Match: IssueNumber().Match(Path.GetFileName(f))))
            .Where(f => f.Match.Success)
            .OrderByDescending(f => int.Parse(f.Match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Select(f => f.File);

    /// <summary>The headlines of the last issues kept in the folder, the newest first.</summary>
    public static List<string> RecentHeadlines(string folder, int issues) =>
        [.. Issues(folder).Take(issues).SelectMany(f => PaperPage.HeadlinesOf(File.ReadAllText(f))).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Whether the title or summary contains one of the keywords, as words or the start of
    /// words; spaces and punctuation do not count, so "Tyler the Creator" finds "Tyler, The
    /// Creator" and "Mrs. GREEN APPLE" finds "Mrs.GREEN APPLE".
    /// </summary>
    public static bool Mentions(FeedItem item, IReadOnlyList<string>? keywords) =>
        keywords is { Count: > 0 }
        && Bare($"{item.Title} {item.Summary}") is var text
        && keywords.Select(Bare).Any(k => k.Length > 0 && text.Contains(k, StringComparison.Ordinal));

    private static string Bare(string text) => WordParts().Replace(text.ToLowerInvariant(), string.Empty);

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

        var judged = 0;
        foreach (var candidate in pool)
        {
            context.Progress($"Judging {judged + 1} of {pool.Count} news items");
            try
            {
                var answers = await decider.DecideAsync(
                    $"{candidate.Item.Source}, section {candidate.Section.Name}: {candidate.Item.Title}\n\n{candidate.Item.Summary}",
                    Questions(settings, candidate.Section), cancellationToken).ConfigureAwait(false);
                candidate.Judgement = new Judgement(
                    Yes(answers, "news"), Importance(answers), Yes(answers, "interest"), Yes(answers, "hopeful"),
                    answers.ContainsKey("priority") ? Yes(answers, "priority") : 0.5,
                    answers.ContainsKey("avoid") ? Yes(answers, "avoid") : 0,
                    answers.ContainsKey("fits") ? Yes(answers, "fits") : 1,
                    answers.ContainsKey("focus") ? Yes(answers, "focus") : 0);
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

    /// <summary>
    /// What the decider is asked about an item: always whether it is news, how much it
    /// matters, whether the reader cares and whether it is good news; then, when the
    /// settings say so, whether it is about the reader's priorities, about something to
    /// avoid, and whether it belongs to its section at all.
    /// </summary>
    public static Dictionary<string, DecisionQuestion> Questions(PaperSettings settings, PaperSettings.Section section)
    {
        var questions = new Dictionary<string, DecisionQuestion>
        {
            ["news"] = new("boolean", "Is this a news report about something new that happened in the last day or two? Opinions, interviews, analyses, reviews, events announced long ahead and plain statements of how things are do not count."),
            ["importance"] = new("choice", "How important is this news today, for the people of the country or of the world it concerns?",
                [new("high", "Many people talk about it or are touched by it today."), new("medium", "It matters to some people."), new("low", "A curiosity or a niche fact.")]),
            ["interest"] = new("boolean", $"Would this reader want to read it? The reader: {settings.Reader}"),
            ["hopeful"] = new("boolean", "Is this good news about something that matters: a solution, a step forward, rights won, help given, a reason for hope? Entertainment, shows and celebrities do not count."),
        };
        if (settings.Priorities is { Count: > 0 } priorities)
        {
            questions["priority"] = new("boolean", $"Is it mainly about one of these topics: {string.Join("; ", priorities)}?");
        }

        // The section's own topics weigh within the section, never on the front page.
        if ((section.Focus ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).ToList() is { Count: > 0 } focus)
        {
            questions["focus"] = new("boolean", $"Is it mainly about one of these topics: {string.Join("; ", focus)}?");
        }

        if (settings.Avoid is { Count: > 0 } avoid)
        {
            questions["avoid"] = new("boolean", $"Is it mainly about one of these topics: {string.Join("; ", avoid)}?");
        }

        if (!string.IsNullOrWhiteSpace(section.Only))
        {
            questions["fits"] = new("boolean", $"Does it meet this condition: {section.Only}?");
        }

        return questions;
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

        var picture = settings.Page.FrontPicture == "none" ? null : await PictureAsync(items, cancellationToken).ConfigureAwait(false);
        return new PaperLead(lead.Section.Name, article, items, picture);
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
        RoutineContext context, PaperSettings settings, PaperSettings.Section section, string form, List<FeedItem> items, CancellationToken cancellationToken) =>
        WriteAsync(
            context, settings, items, $"the section {section.Name} ({section.About})",
            FormTask(section, form) + (string.IsNullOrWhiteSpace(section.Style) ? string.Empty : $" For this section: {section.Style.Trim()}"),
            settings.ArticlesOf(section), cancellationToken, form == "explainer" ? ExplainerSchema : ArticlesSchema);

    private const string Brief = "a brief: a headline of a few words and one sentence with the fact";

    /// <summary>What the writer is asked for a section of this form: articles, briefs, a main story with briefs, or a story explained by questions.</summary>
    internal static string FormTask(PaperSettings.Section section, string form) => form switch
    {
        "briefs" => $"Write one brief per story (items about the same fact make one): a headline of a few words and one sentence with the fact.",
        "feature" => $"The story of item [1] is the section's main one: write it as a full article, with a headline and five to seven sentences. Each other story (items about the same fact make one) is {Brief}.",
        "explainer" => $"The story of item [1] is explained simply: a headline, two sentences that say what happened, and in \"questions\" three to five questions a reader would ask about it, each with a short answer taken only from the items. Each other story (items about the same fact make one) is {Brief}.",
        _ => $"Write one {ArticleSize(section)} article per story (items about the same fact make one article), each with a headline and {SentencesOf(section)}.",
    };

    private static string ArticleSize(PaperSettings.Section section) => PaperSettings.LengthOf(section) switch
    {
        "short" => "very short",
        "long" => "full",
        _ => "short",
    };

    internal static string SentencesOf(PaperSettings.Section section) => PaperSettings.LengthOf(section) switch
    {
        "short" => "one or two sentences",
        "long" => "five to seven sentences",
        _ => "two to four sentences",
    };

    private async Task<List<PaperArticle>> WriteAsync(
        RoutineContext context, PaperSettings settings, List<FeedItem> items, string where, string task, int most, CancellationToken cancellationToken,
        string schema = ArticlesSchema)
    {
        var list = string.Join("\n\n", items.Select((item, i) => $"[{i + 1}] {item.Source}: {item.Title}\n{item.Summary}"));
        var user = $"""
            For {where}. The news, numbered:
            {list}

            {task} Give the numbers of the items each article comes from in "sources", all of them, never in the text.
            """;
        try
        {
            var answer = await chat.AskAsync(context, new LocalAsk(settings.Model, Editor(settings), user, schema, MaxTokens: 3000), cancellationToken).ConfigureAwait(false);
            var articles = (JsonSerializer.Deserialize<ArticlesAnswer>(answer, JsonSerializerOptions.Web)?.Articles ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Headline) && !string.IsNullOrWhiteSpace(a.Text))
                .Select(a => a with
                {
                    Text = Markers().Replace(a.Text, string.Empty).Trim(),
                    Sources = [.. (a.Sources ?? []).Where(n => n >= 1 && n <= items.Count).Distinct()],
                    Questions = a.Questions is null ? null : [.. a.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Question) && !string.IsNullOrWhiteSpace(q.Answer))],
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
        var past = RecentOpenings(Expand(settings.Folder), RecentIssues);
        var headlines = string.Join("\n", (front is null ? [] : new[] { $"- Front page: {front.Article.Headline}" })
            .Concat(sections.SelectMany(s => s.Articles.Select(a => $"- {s.Name}: {a.Headline}"))));
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model, Editor(settings),
                    $"Today's headlines:\n{headlines}\n\nWrite the paper's short opening note: a title of its own (not the paper's name) and three or four warm sentences that start from the day's biggest story and set the day, with a light touch. Never a set greeting or a set closing."
                    + (past.Count == 0 ? string.Empty : $"\n\nThe last notes, whose words, images and closings you never reuse:\n{string.Join('\n', past.Select(o => $"- {o.Title}: {o.Text}"))}"),
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
        - No superlatives, records or "for the first time" unless the items say so.
        - A headline says the fact, with who did what, in a few words. Two headlines never start with the same word, and none is a formula such as "Between this and that" or "New…".
        - Each article ends on a fact, never on a comment about what the news means, shows or opens.
        {NeverWrite(settings)}
        """;

    private static string NeverWrite(PaperSettings settings) =>
        settings.NeverWrite is { Count: > 0 } phrases ? $"- Never write: {string.Join("; ", phrases.Select(p => $"\"{p}\""))}." : string.Empty;

    private const string ArticlesSchema = """
        {"type":"object","properties":{"articles":{"type":"array","items":{"type":"object",
          "properties":{"headline":{"type":"string"},"text":{"type":"string"},"sources":{"type":"array","items":{"type":"integer"}}},
          "required":["headline","text","sources"]}}},"required":["articles"]}
        """;

    private const string ExplainerSchema = """
        {"type":"object","properties":{"articles":{"type":"array","items":{"type":"object",
          "properties":{"headline":{"type":"string"},"text":{"type":"string"},"sources":{"type":"array","items":{"type":"integer"}},
            "questions":{"type":"array","items":{"type":"object","properties":{"question":{"type":"string"},"answer":{"type":"string"}},"required":["question","answer"]}}},
          "required":["headline","text","sources"]}}},"required":["articles"]}
        """;

    private const string ProofSchema = """
        {"type":"object","properties":{"articles":{"type":"array","items":{"type":"object",
          "properties":{"headline":{"type":"string"},"text":{"type":"string"}},"required":["headline","text"]}}},"required":["articles"]}
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
    /// <param name="Priority">About the reader's priorities, or the section's focus; 0.5 when not asked.</param>
    /// <param name="Avoid">About a topic the reader never wants; 0 when not asked.</param>
    /// <param name="Fits">Meets the section's condition; 1 when it has none.</param>
    /// <param name="Focus">About the section's own topics; 0 when it has none.</param>
    private sealed record Judgement(double News, double Importance, double Interest, double Hopeful, double Priority = 0.5, double Avoid = 0, double Fits = 1, double Focus = 0)
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

        /// <summary>How much it deserves a place in its section: importance, the reader's priorities or the section's topics, interest, how many outlets carry it.</summary>
        public double Score => (Judgement.Importance * 0.35) + (Math.Max(Judgement.Priority, Judgement.Focus) * 0.25) + (Judgement.Interest * 0.25) + (Coverage * 0.15);

        /// <summary>
        /// How much it deserves to open the paper: importance weighs most, then the reader's
        /// priorities and how many outlets carry it. A section's own topics do not count here,
        /// so a club's daily chatter never opens the paper over the news of the day.
        /// </summary>
        public double LeadScore => (Judgement.Importance * 0.4) + (Judgement.Priority * 0.3) + (Coverage * 0.2) + (Judgement.Interest * 0.1);

        private double Coverage => Math.Min(Related.Count, 3) / 3.0;
    }
}

/// <param name="Sources">Numbers of the items it comes from, from 1.</param>
/// <param name="Questions">An explained story's questions and answers.</param>
public sealed record PaperArticle(string Headline, string Text, IReadOnlyList<int>? Sources, IReadOnlyList<PaperQuestion>? Questions = null);

public sealed record PaperQuestion(string Question, string Answer);

/// <summary>How a section looks in one issue, when the space follows the news; null keeps its settings.</summary>
internal sealed record SectionLook(string? Width, string? Form, string? Picture)
{
    public static readonly SectionLook AsSet = new(null, null, null);
}

public sealed record PaperOpening(string Title, string Text);

/// <param name="Credit">The outlet the picture comes from.</param>
public sealed record PaperPicture(WebImage Image, string Credit);

/// <summary>A story with a place of its own: the front page's opening, or the good news of the day.</summary>
public sealed record PaperLead(string Section, PaperArticle Article, IReadOnlyList<FeedItem> Items, PaperPicture? Picture);

/// <summary>The kanji of the day with what the local model wrote about it, in the paper's language.</summary>
/// <param name="WordMeanings">One per word of the entry, in order; <paramref name="Translations"/>, one per sentence.</param>
public sealed record PaperKanji(KanjiEntry Entry, string Meaning, string Hint, IReadOnlyList<string> WordMeanings, IReadOnlyList<string> Translations);

/// <param name="Picture">A picture for the section's first story, when one was found.</param>
/// <param name="Width">Its width in this issue, instead of its settings'.</param>
/// <param name="Form">Its form in this issue: articles, briefs, feature or explainer.</param>
/// <param name="PictureSize">The size of its picture in this issue, instead of its settings'.</param>
public sealed record PaperSection(
    string Name, IReadOnlyList<PaperArticle> Articles, IReadOnlyList<FeedItem> Items, PaperPicture? Picture = null,
    string? Width = null, string? Form = null, string? PictureSize = null);

/// <summary>What a morning paper routine is told in its settings.</summary>
public sealed record PaperSettings(
    string Title, string Subtitle, string Language, string Model, string Folder, string Reader,
    IReadOnlyList<string> Rules, int HoursBack, int PerSection, IReadOnlyList<PaperSettings.Section> Sections,
    IReadOnlyList<string>? GoodNews = null, IReadOnlyList<string>? Priorities = null, IReadOnlyList<string>? Avoid = null,
    PaperSettings.PageLayout? Layout = null, IReadOnlyList<string>? NeverWrite = null, bool Proofread = false)
{
    /// <summary>Picture sizes, as words in the settings: none, small, medium, large.</summary>
    public static readonly IReadOnlyList<string> PictureSizes = ["none", "small", "medium", "large"];

    /// <summary>How wide a section is on the page: the whole width, half or a third of it.</summary>
    public static readonly IReadOnlyList<string> Widths = ["full", "half", "third"];

    /// <summary>How long a section's articles are: short, normal or long.</summary>
    public static readonly IReadOnlyList<string> Lengths = ["short", "normal", "long"];

    /// <summary>How a section tells its stories: articles, briefs, a main story with briefs, or the main story explained by questions.</summary>
    public static readonly IReadOnlyList<string> Forms = ["articles", "briefs", "feature", "explainer"];

    /// <param name="Columns">Text columns of a full-width section, 1 to 4.</param>
    /// <param name="FrontPicture">The size of the front page's picture.</param>
    /// <param name="Pictures">The size of each section's picture, unless the section says otherwise.</param>
    /// <param name="Adapt">The space follows the news: the day's biggest story gets the room, quiet sections go in brief.</param>
    /// <param name="FrontPictureAt">The front page's picture across the page above its story, or beside it in the first column.</param>
    public sealed record PageLayout(int Columns = 2, string FrontPicture = "medium", string Pictures = "medium", bool Adapt = false, string FrontPictureAt = "above");

    /// <summary>The page layout, with every value in its range.</summary>
    public PageLayout Page => Layout ?? new PageLayout();

    /// <summary>How wide the section is: full, half or third.</summary>
    public static string WidthOf(Section section) => Pick(section.Width, Widths, "full");

    /// <summary>The section's text columns: its own or the paper's, never more than its width allows.</summary>
    public int ColumnsOf(Section section)
    {
        var most = WidthOf(section) switch { "third" => 1, "half" => 2, _ => 4 };
        return Math.Clamp(section.Columns ?? Page.Columns, 1, most);
    }

    /// <summary>The size of the section's picture: its own or the paper's.</summary>
    public string PictureOf(Section section) => Pick(section.Picture, PictureSizes, Page.Pictures);

    /// <summary>Where the picture goes: beside the first story, or above it. Narrow sections always put it above.</summary>
    public static string PictureAtOf(Section section) =>
        WidthOf(section) != "full" ? "above" : Pick(section.PictureAt, ["beside", "above"], "beside");

    /// <summary>The most articles the section prints: its own or the paper's.</summary>
    public int ArticlesOf(Section section) => section.Articles is > 0 and <= 10 ? section.Articles.Value : PerSection;

    /// <summary>How far back the section looks for news, in hours: its own or the paper's.</summary>
    public int HoursBackOf(Section section) => section.HoursBack is > 0 and <= 24 * 7 ? section.HoursBack.Value : HoursBack;

    /// <summary>How the section tells its stories.</summary>
    public static string FormOf(Section section) => Pick(section.Form, Forms, "articles");

    /// <summary>How long the section's articles are.</summary>
    public static string LengthOf(Section section) => Pick(section.Length, Lengths, "normal");

    private static string Pick(string? value, IReadOnlyList<string> allowed, string fallback)
    {
        var word = value?.Trim().ToLowerInvariant();
        return word is not null && allowed.Contains(word) ? word : fallback;
    }

    /// <summary>Whether the good news of the day may come from this section: from any, unless some are named.</summary>
    public bool GoodNewsFrom(Section section) =>
        GoodNews is not { Count: > 0 } names || names.Contains(section.Name, StringComparer.OrdinalIgnoreCase);

    public sealed record Feed(string Name, string Url);

    /// <param name="Only">A condition every item must meet to be printed here, such as "it happened in Italy"; empty for none.</param>
    /// <param name="Focus">Topics this section looks out for, on top of the reader's priorities.</param>
    /// <param name="Keywords">Words, or starts of words, that put an item on the shortlist before newer ones.</param>
    /// <param name="Style">How to write this section, on top of the paper's rules.</param>
    /// <param name="Width">How wide it is on the page: full, half or third; narrower sections sit side by side.</param>
    /// <param name="Columns">Its text columns, instead of the paper's.</param>
    /// <param name="Picture">The size of its picture (none, small, medium, large), instead of the paper's.</param>
    /// <param name="PictureAt">Beside the first story or above it.</param>
    /// <param name="Articles">The most articles it prints, instead of the paper's.</param>
    /// <param name="Length">How long its articles are: short, normal or long.</param>
    /// <param name="Form">How it tells its stories: articles, briefs, feature (a main story and briefs) or explainer (the main story by questions).</param>
    /// <param name="HoursBack">How far back it looks for news, instead of the paper's: for outlets that write every few days.</param>
    /// <param name="OnlyWithKeywords">Only news naming one of its keywords: without any, the section is left out that day.</param>
    public sealed record Section(
        string Name, string About, IReadOnlyList<Feed> Feeds,
        string? Only = null, IReadOnlyList<string>? Focus = null, IReadOnlyList<string>? Keywords = null, string? Style = null,
        string? Width = null, int? Columns = null, string? Picture = null, string? PictureAt = null, int? Articles = null, string? Length = null,
        bool OnlyWithKeywords = false, int? HoursBack = null, string? Form = null);

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
            Priorities = [.. (read.Priorities ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())],
            Avoid = [.. (read.Avoid ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())],
            HoursBack = read.HoursBack is > 0 and <= 24 * 7 ? read.HoursBack : 24,
            PerSection = read.PerSection is > 0 and <= 10 ? read.PerSection : 4,
            Sections = [.. read.Sections.Where(s => s.Feeds is { Count: > 0 })],
            Layout = new PageLayout(
                Math.Clamp(read.Layout?.Columns ?? 2, 1, 4),
                Pick(read.Layout?.FrontPicture, PictureSizes, "medium"),
                Pick(read.Layout?.Pictures, PictureSizes, "medium"),
                read.Layout?.Adapt ?? false,
                Pick(read.Layout?.FrontPictureAt, ["above", "beside"], "above")),
            NeverWrite = [.. (read.NeverWrite ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())],
        };
    }
}
