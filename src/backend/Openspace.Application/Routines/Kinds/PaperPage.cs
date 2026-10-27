using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The paper as a page: a masthead with its number and date, the opening note, then each
/// section in two columns with its sources. Laid out for A4, printed with backgrounds.
/// Every text is escaped; links are only the feeds' own web addresses.
/// </summary>
public static class PaperPage
{
    public static string Render(
        PaperSettings settings, CultureInfo culture, int number, DateOnly day, PaperOpening opening, IReadOnlyList<PaperSection> sections)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(sections);
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
            <header class="masthead">
              <h1>{E(settings.Title)}</h1>
            """);
        if (settings.Subtitle.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"subtitle\" lang=\"ja\">{E(settings.Subtitle)}</p>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
              <p class="issue"><span>N. {number}</span><span>{E(Capitalised(day.ToString("dddd d MMMM yyyy", culture), culture))}</span></p>
            </header>
            """);

        if (opening.Text.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"""
                <section class="opening">
                  <h2>{E(opening.Title)}</h2>
                  <p>{E(opening.Text)}</p>
                </section>
                """);
        }

        foreach (var section in sections)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section class=\"section\">\n  <h2>{E(section.Name)}</h2>\n  <div class=\"columns\">\n");
            foreach (var article in section.Articles)
            {
                var sources = Sources(article, section);
                html.Append(CultureInfo.InvariantCulture, $"""
                        <article>
                          <h3>{E(article.Headline)}</h3>
                          <p>{E(article.Text)}</p>
                    """);
                if (sources.Length > 0)
                {
                    html.Append(CultureInfo.InvariantCulture, $"      <p class=\"sources\">{sources}</p>\n");
                }

                html.Append("    </article>\n");
            }

            html.Append("  </div>\n</section>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"<footer class=\"colophon\">{E(settings.Title)}{(settings.Subtitle.Length > 0 ? $" · <span lang=\"ja\">{E(settings.Subtitle)}</span>" : "")} · N. {number}. Written by a local model from the linked sources; facts are theirs.</footer>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>"ReliefWeb, Domani", or "ReliefWeb (1, 2)" with a link per item when one outlet gave several.</summary>
    private static string Sources(PaperArticle article, PaperSection section) =>
        string.Join(", ", (article.Sources ?? [])
            .Select(n => section.Items[n - 1])
            .GroupBy(item => item.Source)
            .Select(outlet => outlet.Count() == 1
                ? $"<a href=\"{E(outlet.First().Link.ToString())}\">{E(outlet.Key)}</a>"
                : $"{E(outlet.Key)} ({string.Join(", ", outlet.Select((item, i) => $"<a href=\"{E(item.Link.ToString())}\">{i + 1}</a>"))})"));

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Capitalised(string text, CultureInfo culture) =>
        text.Length == 0 ? text : string.Concat(char.ToUpper(text[0], culture).ToString(), text.AsSpan(1));

    /// <summary>Warm paper, near-black ink: muted text stays above 7:1 on the paper colour.</summary>
    private const string Style = """
        @page { size: A4; margin: 0; }
        :root { --paper: #f6f1e7; --ink: #1f1b16; --muted: #4a4339; --rule: #1f1b16; }
        * { box-sizing: border-box; }
        html { background: var(--paper); }
        body { margin: 0; padding: 14mm 15mm 12mm; color: var(--ink); background: var(--paper);
               font-family: "Noto Serif", "Noto Serif CJK JP", Georgia, serif; font-size: 10.5pt; line-height: 1.45; }
        .masthead { text-align: center; border-bottom: 3px double var(--rule); padding-bottom: 6px; margin-bottom: 12px; }
        .masthead h1 { margin: 0; font-size: 46pt; letter-spacing: 0.04em; font-weight: 800; line-height: 1.05; }
        .subtitle { margin: 2px 0 6px; font-family: "Noto Serif CJK JP", serif; font-size: 13pt; letter-spacing: 0.3em; color: var(--muted); }
        .issue { display: flex; justify-content: space-between; margin: 0; padding: 4px 2px 0; border-top: 1px solid var(--rule);
                 font-size: 9.5pt; text-transform: uppercase; letter-spacing: 0.08em; }
        .opening { margin: 0 0 14px; padding: 8px 12px; border-left: 3px solid var(--rule); font-style: italic; }
        .opening h2 { margin: 0 0 4px; font-size: 14pt; font-style: normal; }
        .opening p { margin: 0; }
        .section { margin-top: 12px; }
        .section > h2 { break-after: avoid; margin: 0 0 8px; padding-bottom: 2px; border-bottom: 1.5px solid var(--rule); font-size: 13pt;
                        text-transform: uppercase; letter-spacing: 0.12em; }
        .columns { column-count: 2; column-gap: 8mm; column-rule: 0.5px solid #b9ad99; }
        article { break-inside: avoid; margin: 0 0 9px; }
        article h3 { margin: 0 0 3px; font-size: 12pt; line-height: 1.2; }
        article p { margin: 0; text-align: justify; hyphens: auto; }
        .sources { margin-top: 3px !important; font-size: 8.5pt; color: var(--muted); text-align: left !important; }
        .sources a { color: var(--muted); }
        .colophon { margin-top: 14px; padding-top: 4px; border-top: 1px solid var(--rule); font-size: 8pt; color: var(--muted); text-align: center; }
        """;
}
