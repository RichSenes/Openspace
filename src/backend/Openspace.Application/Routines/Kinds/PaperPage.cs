using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The paper as pages. The front page: the issue line, the masthead, the day's opening
/// story with a large picture, the opening note beside the good news, and the index. Then
/// each section in two columns, its first story with a picture when there is one. Laid out
/// for A4 and printed with backgrounds. Every text is escaped; links are only the feeds'
/// own web addresses; pictures are embedded, never fetched by the page.
/// </summary>
public static class PaperPage
{
    public static string Render(
        PaperSettings settings, CultureInfo culture, int number, DateOnly day, PaperOpening opening,
        PaperLead? front, PaperLead? goodNews, IReadOnlyList<PaperSection> sections)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(sections);
        var words = Words.For(culture);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="{culture.TwoLetterISOLanguageName}">
            <head>
            <meta charset="utf-8">
            <title>{E(settings.Title)} N. {number}</title>
            <style>{Style}</style>
            </head>
            <body>
            <section class="front">
            <p class="issue"><span>N. {number}</span><span class="colophon">{E(words.Colophon)}</span><span>{E(Capitalised(day.ToString("dddd d MMMM yyyy", culture), culture))}</span></p>
            <header class="masthead">
              <h1>{E(settings.Title)}</h1>

            """);
        if (settings.Subtitle.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"subtitle\" lang=\"ja\">{E(settings.Subtitle)}</p>\n");
        }

        html.Append("</header>\n");

        if (front is not null)
        {
            html.Append("<article class=\"lead\">\n");
            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"kicker\">{E(words.Lead)}</p>\n  <h2>{E(front.Article.Headline)}</h2>\n");
            if (front.Picture is { } picture)
            {
                html.Append(CultureInfo.InvariantCulture, $"  <figure><img src=\"{picture.Image.DataUri}\" alt=\"\"><figcaption>{E(words.Photo)}: {E(picture.Credit)}</figcaption></figure>\n");
            }

            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"lead-text\">{E(front.Article.Text)}</p>\n");
            AppendSources(html, front.Article, front.Items);
            html.Append("</article>\n");
        }

        html.Append("<div class=\"front-row\">\n");
        if (opening.Text.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <section class=\"opening\"><h3>{E(opening.Title)}</h3><p>{E(opening.Text)}</p></section>\n");
        }

        if (goodNews is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <section class=\"good\"><p class=\"kicker\">{E(words.GoodNews)}</p><h3>{E(goodNews.Article.Headline)}</h3><p>{E(goodNews.Article.Text)}</p>");
            AppendSources(html, goodNews.Article, goodNews.Items);
            html.Append("</section>\n");
        }

        html.Append("</div>\n");

        if (sections.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<nav class=\"index\"><h3>{E(words.InThisIssue)}</h3><ul>\n");
            foreach (var section in sections)
            {
                html.Append(CultureInfo.InvariantCulture, $"  <li><strong>{E(section.Name)}</strong> {string.Join(" · ", section.Articles.Select(a => E(a.Headline)))}</li>\n");
            }

            html.Append("</ul></nav>\n");
        }

        html.Append("</section>\n");

        foreach (var section in sections)
        {
            // The heading travels with the first story, so it never ends a page alone.
            html.Append(CultureInfo.InvariantCulture, $"<section class=\"section\">\n  <div class=\"opener\"><h2>{E(section.Name)}</h2>\n");
            for (var i = 0; i < section.Articles.Count; i++)
            {
                var article = section.Articles[i];
                if (i == 0 && section.Picture is { } picture)
                {
                    // The section's first story opens it across the page, with its picture.
                    html.Append(CultureInfo.InvariantCulture, $"""
                          <article class="top">
                            <figure><img src="{picture.Image.DataUri}" alt=""><figcaption>{E(words.Photo)}: {E(picture.Credit)}</figcaption></figure>
                            <div><h3>{E(article.Headline)}</h3><p>{E(article.Text)}</p>
                        """);
                    AppendSources(html, article, section.Items);
                    html.Append("</div>\n  </article></div>\n  <div class=\"columns\">\n");
                    continue;
                }

                if (i == 0)
                {
                    // Without a picture the first story is a plain one, kept with the heading.
                    html.Append(CultureInfo.InvariantCulture, $"<article class=\"first\"><h3>{E(article.Headline)}</h3><p>{E(article.Text)}</p>");
                    AppendSources(html, article, section.Items);
                    html.Append("</article></div>\n  <div class=\"columns\">\n");
                    continue;
                }

                html.Append(CultureInfo.InvariantCulture, $"    <article><h3>{E(article.Headline)}</h3><p>{E(article.Text)}</p>");
                AppendSources(html, article, section.Items);
                html.Append("</article>\n");
            }

            html.Append("  </div>\n</section>\n");
        }

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static void AppendSources(StringBuilder html, PaperArticle article, IReadOnlyList<FeedItem> items)
    {
        var sources = Sources(article, items);
        if (sources.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"sources\">{sources}</p>");
        }
    }

    /// <summary>"ReliefWeb, Domani", or "ReliefWeb (1, 2)" with a link per item when one outlet gave several.</summary>
    private static string Sources(PaperArticle article, IReadOnlyList<FeedItem> items) =>
        string.Join(", ", (article.Sources ?? [])
            .Select(n => items[n - 1])
            .GroupBy(item => item.Source)
            .Select(outlet => outlet.Count() == 1
                ? $"<a href=\"{E(outlet.First().Link.ToString())}\">{E(outlet.Key)}</a>"
                : $"{E(outlet.Key)} ({string.Join(", ", outlet.Select((item, i) => $"<a href=\"{E(item.Link.ToString())}\">{i + 1}</a>"))})"));

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Capitalised(string text, CultureInfo culture) =>
        text.Length == 0 ? text : string.Concat(char.ToUpper(text[0], culture).ToString(), text.AsSpan(1));

    /// <summary>The few words the page writes itself, in the paper's language.</summary>
    private sealed record Words(string Lead, string GoodNews, string InThisIssue, string Photo, string Colophon)
    {
        public static Words For(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
        {
            "it" => new("In apertura", "La buona notizia", "In questo numero", "Foto", "Scritto da un modello locale a partire dalle fonti citate: i fatti sono loro."),
            "es" => new("Portada", "La buena noticia", "En este número", "Foto", "Escrito por un modelo local a partir de las fuentes citadas: los hechos son suyos."),
            "fr" => new("À la une", "La bonne nouvelle", "Dans ce numéro", "Photo", "Écrit par un modèle local à partir des sources citées : les faits sont les leurs."),
            "de" => new("Aufmacher", "Die gute Nachricht", "In dieser Ausgabe", "Foto", "Von einem lokalen Modell aus den verlinkten Quellen geschrieben: die Fakten stammen von ihnen."),
            "ja" => new("トップ記事", "今日のいいニュース", "今号の内容", "写真", "ローカルモデルが出典をもとに書きました。事実は出典によります。"),
            _ => new("Front page", "The good news", "In this issue", "Photo", "Written by a local model from the linked sources: the facts are theirs."),
        };
    }

    /// <summary>Warm paper, near-black ink: muted text stays above 7:1 on the paper colour.</summary>
    private const string Style = """
        @page { size: A4; margin: 0; }
        :root { --paper: #f6f1e7; --ink: #1f1b16; --muted: #4a4339; --rule: #1f1b16; --box: #ece4d4; }
        * { box-sizing: border-box; }
        html { background: var(--paper); }
        body { margin: 0; padding: 12mm 15mm 14mm; color: var(--ink); background: var(--paper);
               box-decoration-break: clone; -webkit-box-decoration-break: clone;
               font-family: "Noto Serif", "Noto Serif CJK JP", Georgia, serif; font-size: 10.5pt; line-height: 1.45; }
        .front { margin-bottom: 6px; }
        .issue { display: flex; justify-content: space-between; align-items: baseline; gap: 6mm; margin: 0 0 4px; padding-bottom: 3px; border-bottom: 1px solid var(--rule);
                 font-size: 9.5pt; text-transform: uppercase; letter-spacing: 0.08em; }
        .masthead { text-align: center; border-bottom: 3px double var(--rule); padding-bottom: 4px; margin-bottom: 10px; }
        .masthead h1 { margin: 0; font-size: 46pt; letter-spacing: 0.04em; font-weight: 800; line-height: 1.05; }
        .subtitle { margin: 2px 0 2px; font-family: "Noto Serif CJK JP", serif; font-size: 13pt; letter-spacing: 0.3em; color: var(--muted); }
        .kicker { margin: 0 0 2px; font-size: 8.5pt; text-transform: uppercase; letter-spacing: 0.14em; color: var(--muted); }
        .lead h2 { margin: 0 0 8px; font-size: 24pt; line-height: 1.12; }
        figure { margin: 0 0 8px; }
        figure img { display: block; width: 100%; object-fit: cover; border: 0.5px solid #b9ad99; }
        .lead figure img { height: 58mm; }
        figcaption { margin-top: 2px; font-size: 7.5pt; color: var(--muted); text-align: right; }
        .lead-text { margin: 0; column-count: 2; column-gap: 8mm; text-align: justify; hyphens: auto; font-size: 11pt; }
        .front-row { display: grid; grid-template-columns: 1fr 1fr; gap: 8mm; break-inside: avoid; margin-top: 12px; padding-top: 10px;
                     border-top: 1.5px solid var(--rule); }
        .opening { font-style: italic; font-size: 10pt; }
        .opening h3, .good h3 { margin: 0 0 4px; font-size: 13pt; font-style: normal; }
        .opening p, .good p { margin: 0; }
        .good { padding: 8px 10px; background: var(--box); border: 1px solid #b9ad99; }
        .index { margin-top: 12px; padding-top: 8px; border-top: 1px solid var(--rule); }
        .index h3 { margin: 0 0 4px; font-size: 10pt; text-transform: uppercase; letter-spacing: 0.12em; }
        .index ul { margin: 0; padding: 0; list-style: none; font-size: 9pt; }
        .index li { margin: 0 0 3px; }
        .index strong { text-transform: uppercase; letter-spacing: 0.06em; margin-right: 4px; }
        .section { margin-top: 12px; }
        .opener { break-inside: avoid; }
        .section h2 { break-after: avoid; margin: 0 0 8px; padding-bottom: 2px; border-bottom: 1.5px solid var(--rule); font-size: 13pt;
                        text-transform: uppercase; letter-spacing: 0.12em; }
        .top { display: grid; grid-template-columns: 1fr 1fr; gap: 8mm; break-inside: avoid; margin-bottom: 10px; }
        .top figure img { height: 58mm; }
        .top h3 { margin: 0 0 4px; font-size: 15pt; line-height: 1.15; }
        .top p { margin: 0; text-align: justify; hyphens: auto; }
        .columns { column-count: 2; column-gap: 8mm; column-rule: 0.5px solid #b9ad99; }
        article { break-inside: avoid; margin: 0 0 9px; }
        article h3 { margin: 0 0 3px; font-size: 12pt; line-height: 1.2; }
        article p { margin: 0; text-align: justify; hyphens: auto; }
        p.sources { margin-top: 3px; font-size: 8.5pt; color: var(--muted); text-align: left; }
        .sources a { color: var(--muted); }
        .issue .colophon { font-size: 7pt; text-transform: none; letter-spacing: 0; color: var(--muted); text-align: center; }
        """;
}
