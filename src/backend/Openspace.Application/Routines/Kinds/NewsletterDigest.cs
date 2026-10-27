using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The newsletters of the last days, read for the person. Each newsletter the routine never
/// summed up (those the mail routine labelled, by default) is summed up by a local model in a
/// few sentences, without its advertising; the summaries are grouped by topic and laid out
/// like the paper, printed to PDF, numbered, kept and delivered. Mail goes to local models only.
/// </summary>
public sealed class NewsletterDigest(IMailbox mailbox, ILocalChat chat, IPdfPrinter printer) : IRoutineKind
{
    public const string KeyPrefix = "letter:";

    public string Key => "newsletters";

    public string Name => "Newsletter digest";

    public string Description => "Sums up the newsletters in your mail, by topic and without their advertising, as a numbered PDF.";

    public string DefaultSettings => """
        {
          "title": "Newsletters",
          "subtitle": "",
          "account": "",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "folder": "~/Documents/Newsletters",
          "query": "label:openspace-newsletters newer_than:14d",
          "most": 20,
          "reader": "Who reads it and what they care about, in a few sentences."
        }
        """;

    /// <summary>Pages of ids looked through at most: a few thousand messages.</summary>
    private const int MostPages = 20;

    /// <summary>Of each newsletter, what the model reads: long ones are mostly links and footers past this.</summary>
    private const int Reading = 6000;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = DigestSettings.Read(context.Settings);
        var culture = MorningPaper.CultureOf(settings.Language);

        var letters = await GatherAsync(context, settings, cancellationToken).ConfigureAwait(false);
        context.Write($"{letters.Count} newsletters to sum up.");
        if (letters.Count == 0)
        {
            return "No new newsletters.";
        }

        var summaries = new List<(MailMessage Letter, LetterSummary Summary)>();
        var skipped = 0;
        foreach (var (letter, i) in letters.Select((l, i) => (l, i)))
        {
            context.Progress($"Summing up {i + 1} of {letters.Count} newsletters");
            switch (await SumUpAsync(context, settings, letter, cancellationToken).ConfigureAwait(false))
            {
                case { Worth: false }:
                    skipped++;
                    break;
                case { } summary:
                    summaries.Add((letter, summary));
                    break;
            }
        }

        if (summaries.Count == 0 && skipped == 0)
        {
            throw new InvalidOperationException("The local model could not sum up any newsletter: see the log.");
        }

        foreach (var letter in letters)
        {
            await context.MarkAsync(KeyPrefix + letter.Id, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        if (summaries.Count == 0)
        {
            return $"{skipped} newsletters, all advertising: nothing to print.";
        }

        var topics = summaries
            .GroupBy(s => Topic(s.Summary.Topic, culture), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DigestTopic(g.Key, [.. g.OrderByDescending(s => s.Letter.Received).Select(s => new DigestEntry(s.Summary.Headline, s.Summary.Summary, MailSorting.Sender(s.Letter.From), s.Letter.Received, Link(settings, s.Letter)))]))
            .ToList();
        var opening = await OpeningAsync(context, settings, topics, cancellationToken).ConfigureAwait(false);

        var folder = MorningPaper.Expand(settings.Folder);
        Directory.CreateDirectory(folder);
        var number = MorningPaper.NextNumber(folder);
        var zone = context.Clock.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, zone).DateTime);
        var html = Render(settings, culture, zone, number, today, opening, topics);

        var name = $"{MorningPaper.Slug(settings.Title)}-N{number:000}-{today:yyyy-MM-dd}";
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".html"), html, cancellationToken).ConfigureAwait(false);
        var pdf = Path.Combine(folder, name + ".pdf");
        await File.WriteAllBytesAsync(pdf, await printer.PrintAsync(html, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        context.Write($"Printed {Path.GetFileName(pdf)}");

        var caption = $"{settings.Title} · N. {number} · {today.ToString("dddd d MMMM yyyy", culture)}\n{string.Join(" · ", topics.Select(t => $"{t.Name} {t.Entries.Count}"))}";
        await context.DeliverAsync(caption, pdf, cancellationToken).ConfigureAwait(false);
        return $"N. {number}: {summaries.Count} newsletters in {topics.Count} topics{(skipped > 0 ? $", {skipped} left out as advertising" : string.Empty)}.";
    }

    /// <summary>The newsletters matching the query, newest first, never summed up before: up to the most per issue.</summary>
    private async Task<List<MailMessage>> GatherAsync(RoutineContext context, DigestSettings settings, CancellationToken cancellationToken)
    {
        var done = await context.DealtWithAsync(KeyPrefix, cancellationToken).ConfigureAwait(false);
        var ids = new List<string>();
        string? page = null;
        var pages = 0;
        do
        {
            var found = await mailbox.FindAsync(settings.Account, settings.Query, page, cancellationToken).ConfigureAwait(false);
            ids.AddRange(found.Ids.Where(id => !done.Contains(KeyPrefix + id) && !ids.Contains(id)).Take(settings.Most - ids.Count));
            page = found.Next;
            pages++;
        }
        while (page is not null && pages < MostPages && ids.Count < settings.Most);

        var letters = new List<MailMessage>();
        foreach (var (id, i) in ids.Select((id, i) => (id, i)))
        {
            context.Progress($"Reading {i + 1} of {ids.Count} newsletters");
            var letter = await mailbox.ReadAsync(settings.Account, id, cancellationToken).ConfigureAwait(false);
            if (await context.MeetAsync(KeyPrefix + letter.Id, letter.Id, $"{MailSorting.Sender(letter.From)}: {letter.Subject}", cancellationToken).ConfigureAwait(false))
            {
                letters.Add(letter);
            }
        }

        return letters;
    }

    private async Task<LetterSummary?> SumUpAsync(RoutineContext context, DigestSettings settings, MailMessage letter, CancellationToken cancellationToken)
    {
        var text = letter.Text.Length > 0 ? letter.Text : letter.Snippet;
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model, Editor(settings),
                    $"""
                    A newsletter.
                    From: {letter.From}
                    Subject: {letter.Subject}

                    {(text.Length <= Reading ? text : text[..Reading])}

                    Sum it up for the reader in {settings.Language}: a headline of your own, then three to five sentences with what is new or useful in it. Leave out advertising, sponsors, offers and calls to subscribe or share. Give its topic in one or two words (Technology, Japan, Politics, Games…). If it is nothing but advertising, say so with "worth": false.
                    """,
                    SummarySchema, MaxTokens: 900),
                cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Deserialize<LetterSummary>(answer, JsonSerializerOptions.Web);
            if (summary is null || (summary.Worth && (string.IsNullOrWhiteSpace(summary.Headline) || string.IsNullOrWhiteSpace(summary.Summary))))
            {
                context.Write($"{letter.Subject}: the summary came out empty.");
                return null;
            }

            return summary with { Headline = summary.Headline.Trim(), Summary = summary.Summary.Trim() };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{letter.Subject}: could not be summed up ({ex.Message}).");
            return null;
        }
    }

    private async Task<PaperOpening> OpeningAsync(RoutineContext context, DigestSettings settings, List<DigestTopic> topics, CancellationToken cancellationToken)
    {
        var headlines = string.Join("\n", topics.SelectMany(t => t.Entries.Select(e => $"- {t.Name}: {e.Headline}")));
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model, Editor(settings),
                    $"This issue's newsletters:\n{headlines}\n\nWrite a short opening note: a title of its own and two or three sentences on what stands out, with a light touch.",
                    OpeningSchema, MaxTokens: 600),
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

    /// <summary>"technology" and "Technology" are one topic, named as the first letter says; no topic is "Other".</summary>
    private static string Topic(string? topic, CultureInfo culture)
    {
        var name = (topic ?? string.Empty).Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            return culture.TwoLetterISOLanguageName == "it" ? "Altro" : "Other";
        }

        return char.ToUpper(name[0], culture) + name[1..];
    }

    private static string Link(DigestSettings settings, MailMessage letter) =>
        $"https://mail.google.com/mail/?authuser={Uri.EscapeDataString(settings.Account)}#all/{letter.ThreadId}";

    private static string Editor(DigestSettings settings) => $"""
        You read newsletters for one person and sum them up. Write in {settings.Language}, and only in {settings.Language}.
        Who reads it: {settings.Reader}
        Use only what the newsletter says: never invent facts, names, numbers or quotes.
        """;

    public static string Render(DigestSettings settings, CultureInfo culture, TimeZoneInfo zone, int number, DateOnly day, PaperOpening opening, IReadOnlyList<DigestTopic> topics)
    {
        var italian = culture.TwoLetterISOLanguageName == "it";
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="{culture.TwoLetterISOLanguageName}">
            <head>
            <meta charset="utf-8">
            <title>{E(settings.Title)} N. {number}</title>
            <style>{PaperPage.Style}</style>
            </head>
            <body>
            <section class="front">
            <p class="issue"><span>N. {number}</span><span class="colophon">{E(italian ? "Riassunte da un modello locale dalle newsletter della tua posta" : "Summed up by a local model from the newsletters in your mail")}</span><span>{E(Capitalised(day.ToString("dddd d MMMM yyyy", culture), culture))}</span></p>
            <header class="masthead">
              <h1>{E(settings.Title)}</h1>

            """);
        if (settings.Subtitle.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"subtitle\" lang=\"ja\">{E(settings.Subtitle)}</p>\n");
        }

        html.Append("</header>\n");
        if (opening.Text.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section class=\"opening\"><h3>{E(opening.Title)}</h3><p>{E(opening.Text)}</p></section>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"<nav class=\"index\"><h3>{E(italian ? "In questo numero" : "In this issue")}</h3><ul>\n");
        foreach (var topic in topics)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <li><strong>{E(topic.Name)}</strong> {string.Join(" · ", topic.Entries.Select(e => E(e.Headline)))}</li>\n");
        }

        html.Append("</ul></nav>\n</section>\n");
        foreach (var topic in topics)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section class=\"section\">\n  <div class=\"opener\"><h2>{E(topic.Name)}</h2></div>\n  <div class=\"columns\">\n");
            foreach (var entry in topic.Entries)
            {
                var received = TimeZoneInfo.ConvertTime(entry.Received, zone).ToString("d MMM", culture);
                html.Append(CultureInfo.InvariantCulture, $"""
                        <article><h3>{E(entry.Headline)}</h3><p>{E(entry.Summary)}</p><p class="sources"><a href="{E(entry.Link)}">{E(entry.Sender)}</a> · {E(received)}</p></article>

                    """);
            }

            html.Append("  </div>\n</section>\n");
        }

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Capitalised(string text, CultureInfo culture) => text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];

    private const string SummarySchema = """
        {"type":"object","properties":{"headline":{"type":"string"},"summary":{"type":"string"},"topic":{"type":"string"},"worth":{"type":"boolean"}},
          "required":["headline","summary","topic","worth"]}
        """;

    private const string OpeningSchema = """
        {"type":"object","properties":{"title":{"type":"string"},"text":{"type":"string"}},"required":["title","text"]}
        """;

    private sealed record LetterSummary(string Headline, string Summary, string? Topic, bool Worth = true);
}

public sealed record DigestTopic(string Name, IReadOnlyList<DigestEntry> Entries);

/// <param name="Link">Back to the newsletter in the mailbox.</param>
public sealed record DigestEntry(string Headline, string Summary, string Sender, DateTimeOffset Received, string Link);

/// <summary>A newsletter digest's settings, read from its JSON.</summary>
public sealed record DigestSettings(
    string Title, string Subtitle, string Account, string Language, string Model, string Folder, string Query, int Most, string Reader)
{
    public static DigestSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        var account = Text("account", string.Empty);
        if (account.Length == 0)
        {
            throw new InvalidOperationException("This routine names no account: choose the Google account in Change.");
        }

        var most = settings.TryGetProperty("most", out var m) && m.TryGetInt32(out var count) ? Math.Clamp(count, 1, 60) : 20;
        return new DigestSettings(
            Text("title", "Newsletters"), Text("subtitle", string.Empty), account, Text("language", "English"), Text("model", "local/gemma"),
            Text("folder", "~/Documents/Newsletters"), Text("query", "label:openspace-newsletters newer_than:14d"), most,
            Text("reader", "Someone curious, short of time."));
    }
}
