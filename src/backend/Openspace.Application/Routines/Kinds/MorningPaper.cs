using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// A personal paper, every morning: news from the outlets the person chose, sorted into
/// sections, filtered by the decider for what interests them, written up by a local model
/// in their language, laid out as a paper, printed to PDF, numbered, kept and delivered.
/// Nothing already printed comes back the next day.
/// </summary>
public sealed partial class MorningPaper(ILocalChat chat, IDecider decider, IFeedReader feeds, IPdfPrinter printer) : IRoutineKind
{
    public string Key => "morning-paper";

    public string Name => "Morning paper";

    public string Description => "News from the outlets you choose, filtered and written up by a local model, as a numbered PDF paper.";

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
          "hoursBack": 30,
          "perSection": 4,
          "sections": [
            { "name": "World", "about": "What happened in the world.", "feeds": [ { "name": "Example News", "url": "https://news.example/feed" } ] }
          ]
        }
        """;

    /// <summary>Items read per feed at most: some feeds carry their whole history.</summary>
    private const int PerFeed = 15;

    /// <summary>Items the decider weighs per section, at most: it takes a few seconds each.</summary>
    private const int Candidates = 12;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = PaperSettings.Read(context.Settings);
        var culture = CultureOf(settings.Language);
        var since = context.Now - TimeSpan.FromHours(settings.HoursBack);

        var sections = new List<PaperSection>();
        foreach (var section in settings.Sections)
        {
            var fresh = await GatherAsync(context, section, since, cancellationToken).ConfigureAwait(false);
            var chosen = await ChooseAsync(context, settings, section, fresh, cancellationToken).ConfigureAwait(false);
            if (chosen.Count == 0)
            {
                context.Write($"{section.Name}: nothing new.");
                continue;
            }

            var articles = await WriteAsync(context, settings, section, chosen, cancellationToken).ConfigureAwait(false);
            if (articles.Count == 0)
            {
                continue;
            }

            foreach (var item in chosen)
            {
                await context.MarkAsync(item.Link.ToString(), SeenState.Shown, cancellationToken).ConfigureAwait(false);
            }

            sections.Add(new PaperSection(section.Name, articles, chosen));
        }

        if (sections.Count == 0)
        {
            return "Nothing new to print today.";
        }

        var opening = await OpeningAsync(context, settings, sections, cancellationToken).ConfigureAwait(false);
        var folder = Expand(settings.Folder);
        Directory.CreateDirectory(folder);
        var number = NextNumber(folder);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone).DateTime);
        var html = PaperPage.Render(settings, culture, number, today, opening, sections);

        var name = $"{Slug(settings.Title)}-N{number:000}-{today:yyyy-MM-dd}";
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".html"), html, cancellationToken).ConfigureAwait(false);
        var pdf = Path.Combine(folder, name + ".pdf");
        await File.WriteAllBytesAsync(pdf, await printer.PrintAsync(html, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        context.Write($"Printed {Path.GetFileName(pdf)}");

        var count = sections.Sum(s => s.Articles.Count);
        var caption = $"{settings.Title} · N. {number} · {today.ToString("dddd d MMMM yyyy", culture)}\n{opening.Title}";
        await context.DeliverAsync(caption, pdf, cancellationToken).ConfigureAwait(false);
        return $"N. {number}: {count} articles in {sections.Count} sections.";
    }

    /// <summary>The section's news since <paramref name="since"/> that the paper never printed.</summary>
    private async Task<List<FeedItem>> GatherAsync(RoutineContext context, PaperSettings.Section section, DateTimeOffset since, CancellationToken cancellationToken)
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
                if (await context.MeetAsync(item.Link.ToString(), RoutineContext.Fingerprint(item.Title), item.Title, cancellationToken).ConfigureAwait(false)
                    && !fresh.Exists(f => f.Link == item.Link))
                {
                    fresh.Add(item);
                }
            }
        }

        context.Write($"{section.Name}: {fresh.Count} new items.");
        return fresh;
    }

    /// <summary>
    /// The items the reader would care about most, as the decider judges them; the newest
    /// ones when there is no decider or it fails.
    /// </summary>
    private async Task<List<FeedItem>> ChooseAsync(
        RoutineContext context, PaperSettings settings, PaperSettings.Section section, List<FeedItem> fresh, CancellationToken cancellationToken)
    {
        var candidates = fresh.OrderByDescending(i => i.Published).Take(Candidates).ToList();
        if (candidates.Count <= settings.PerSection || !decider.Available)
        {
            return [.. candidates.Take(settings.PerSection)];
        }

        var question = new Dictionary<string, DecisionQuestion>
        {
            ["interesting"] = new("boolean", $"Would this reader want to read this news in the section \"{section.Name}\" ({section.About})? The reader: {settings.Reader}"),
        };
        var scored = new List<(FeedItem Item, double Score)>();
        try
        {
            foreach (var item in candidates)
            {
                var answer = (await decider.DecideAsync($"{item.Source}: {item.Title}\n\n{item.Summary}", question, cancellationToken).ConfigureAwait(false))["interesting"];
                var yes = answer.Abstained ? 0.5 : answer.Answer == "true" ? answer.Confidence : 1 - answer.Confidence;
                scored.Add((item, yes));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{section.Name}: the decider failed ({ex.Message}); taking the newest items.");
            return [.. candidates.Take(settings.PerSection)];
        }

        return [.. scored.Where(s => s.Score >= 0.4).OrderByDescending(s => s.Score).Take(settings.PerSection).Select(s => s.Item)];
    }

    private async Task<List<PaperArticle>> WriteAsync(
        RoutineContext context, PaperSettings settings, PaperSettings.Section section, List<FeedItem> items, CancellationToken cancellationToken)
    {
        var list = string.Join("\n\n", items.Select((item, i) => $"[{i + 1}] {item.Source}: {item.Title}\n{item.Summary}"));
        var user = $"""
            Section: {section.Name} ({section.About})

            The news, numbered:
            {list}

            Write one short article per piece of news worth it (merge pieces about the same fact), each with a headline,
            two to four sentences, and the numbers of the news it comes from in "sources" (never in the text).
            """;
        try
        {
            var answer = await chat.AskAsync(
                new LocalAsk(settings.Model, Editor(settings), user, ArticlesSchema, MaxTokens: 3000), cancellationToken).ConfigureAwait(false);
            var articles = JsonSerializer.Deserialize<ArticlesAnswer>(answer, JsonSerializerOptions.Web)?.Articles ?? [];
            var valid = articles
                .Where(a => !string.IsNullOrWhiteSpace(a.Headline) && !string.IsNullOrWhiteSpace(a.Text))
                .Select(a => a with
                {
                    Text = Markers().Replace(a.Text, string.Empty).Trim(),
                    Sources = [.. (a.Sources ?? []).Where(n => n >= 1 && n <= items.Count).Distinct()],
                })
                .ToList();
            context.Write($"{section.Name}: {valid.Count} articles written.");
            return valid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{section.Name}: could not be written ({ex.Message}).");
            return [];
        }
    }

    private async Task<PaperOpening> OpeningAsync(RoutineContext context, PaperSettings settings, List<PaperSection> sections, CancellationToken cancellationToken)
    {
        var headlines = string.Join("\n", sections.SelectMany(s => s.Articles.Select(a => $"- {s.Name}: {a.Headline}")));
        try
        {
            var answer = await chat.AskAsync(
                new LocalAsk(
                    settings.Model, Editor(settings),
                    $"Today's headlines:\n{headlines}\n\nWrite the paper's short opening note: a title of its own (not the paper's name) and four or five warm sentences that set the day, with a light touch.",
                    OpeningSchema, MaxTokens: 800),
                cancellationToken).ConfigureAwait(false);
            var opening = JsonSerializer.Deserialize<PaperOpening>(answer, JsonSerializerOptions.Web);
            if (opening is { Title.Length: > 0, Text.Length: > 0 })
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

    private static string Expand(string folder) =>
        folder.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder.TrimStart('~', '/')) : folder;

    private static CultureInfo CultureOf(string language) => language.Trim().ToUpperInvariant() switch
    {
        "ITALIAN" or "ITALIANO" => CultureInfo.GetCultureInfo("it-IT"),
        "JAPANESE" => CultureInfo.GetCultureInfo("ja-JP"),
        "SPANISH" => CultureInfo.GetCultureInfo("es-ES"),
        "FRENCH" => CultureInfo.GetCultureInfo("fr-FR"),
        "GERMAN" => CultureInfo.GetCultureInfo("de-DE"),
        _ => CultureInfo.GetCultureInfo("en-GB"),
    };

    private static string Slug(string title) => SlugParts().Replace(title.Trim(), "-").Trim('-') is { Length: > 0 } slug ? slug : "Paper";

    /// <summary>Source markers a model may leave in the text anyway: "[2]", " [1, 3]".</summary>
    [GeneratedRegex(@"\s*\[\d+(?:\s*,\s*\d+)*\]")]
    private static partial Regex Markers();

    [GeneratedRegex(@"-N(\d+)-")]
    private static partial Regex IssueNumber();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex SlugParts();

    private sealed record ArticlesAnswer(List<PaperArticle>? Articles);
}

/// <param name="Sources">Numbers of the section's items it comes from, from 1.</param>
public sealed record PaperArticle(string Headline, string Text, IReadOnlyList<int>? Sources);

public sealed record PaperOpening(string Title, string Text);

public sealed record PaperSection(string Name, IReadOnlyList<PaperArticle> Articles, IReadOnlyList<FeedItem> Items);

/// <summary>What a morning paper routine is told in its settings.</summary>
public sealed record PaperSettings(
    string Title, string Subtitle, string Language, string Model, string Folder, string Reader,
    IReadOnlyList<string> Rules, int HoursBack, int PerSection, IReadOnlyList<PaperSettings.Section> Sections)
{
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
            HoursBack = read.HoursBack is > 0 and <= 24 * 7 ? read.HoursBack : 30,
            PerSection = read.PerSection is > 0 and <= 10 ? read.PerSection : 4,
            Sections = [.. read.Sections.Where(s => s.Feeds is { Count: > 0 })],
        };
    }
}
