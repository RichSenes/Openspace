using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The paper as pages. The front page: the issue line, the masthead, the day's opening
/// story with a large picture, the opening note beside the good news, and the index. Then
/// each section in two columns, its first story with a picture when there is one. Laid out
/// for A4 and printed with backgrounds. Every text is escaped; links are only the feeds'
/// own web addresses; pictures are embedded, never fetched by the page. The kanji of the day
/// closes the paper: its strokes drawn one by one, its words and sentences with their reading.
/// </summary>
public static partial class PaperPage
{
    public static string Render(
        PaperSettings settings, CultureInfo culture, int number, DateOnly day, PaperOpening opening,
        PaperLead? front, PaperLead? goodNews, IReadOnlyList<PaperSection> sections, PaperKanji? kanji = null)
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

        if (sections.Count > 0 || kanji is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<nav class=\"index\"><h3>{E(words.InThisIssue)}</h3><ul>\n");
            foreach (var section in sections)
            {
                html.Append(CultureInfo.InvariantCulture, $"  <li><strong>{E(section.Name)}</strong> {string.Join(" · ", section.Articles.Select(a => E(a.Headline)))}</li>\n");
            }

            if (kanji is not null)
            {
                html.Append(CultureInfo.InvariantCulture, $"  <li><strong>{E(words.Kanji)}</strong> <span lang=\"ja\">{E(kanji.Entry.Literal)}</span> · {E(kanji.Meaning)}</li>\n");
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

        if (kanji is not null)
        {
            AppendKanji(html, kanji, words);
        }

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>Readings of each kind shown at most: the first are the common ones, some kanji have ten.</summary>
    private const int ReadingsShown = 6;

    private static void AppendKanji(StringBuilder html, PaperKanji kanji, Words words)
    {
        var entry = kanji.Entry;
        var literal = entry.Literal;
        html.Append(CultureInfo.InvariantCulture, $"""
            <section class="kanji">
              <h2>{E(words.Kanji)}</h2>
              <div class="kanji-head">
                <div class="glyph" lang="ja">{E(literal)}</div>
                <div class="facts">
                  <p class="meaning">{E(kanji.Meaning)}</p>

            """);
        if (entry.On.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"      <p><span class=\"label\">On</span> <span lang=\"ja\">{E(string.Join(" · ", entry.On.Take(ReadingsShown)))}</span></p>\n");
        }

        if (entry.Kun.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"      <p><span class=\"label\">Kun</span> <span lang=\"ja\">{E(string.Join(" · ", entry.Kun.Take(ReadingsShown)))}</span></p>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"      <p class=\"about\">{E(string.Format(CultureInfo.InvariantCulture, words.Strokes, entry.StrokeCount))} · {E(words.GradeOf(entry.Grade))}");
        if (entry.Parts.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $" · {E(words.Parts)} <span lang=\"ja\">{E(string.Join(" + ", entry.Parts))}</span>");
        }

        html.Append("</p>\n");
        if (kanji.Hint.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"      <p class=\"hint\">{E(kanji.Hint)}</p>\n");
        }

        html.Append("    </div>\n  </div>\n");
        AppendStrokes(html, entry.Strokes);

        html.Append("  <div class=\"kanji-columns\">\n");
        if (entry.Words.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"    <div><h3>{E(words.WordsTitle)}</h3><ul class=\"words\">\n");
            for (var i = 0; i < entry.Words.Count; i++)
            {
                var word = entry.Words[i];
                html.Append(CultureInfo.InvariantCulture, $"      <li><span lang=\"ja\" class=\"word\"><ruby>{Marked(word.Written, literal)}<rt>{E(word.Reading)}</rt></ruby></span> {E(kanji.WordMeanings.ElementAtOrDefault(i) ?? string.Empty)}</li>\n");
            }

            html.Append("    </ul></div>\n");
        }

        if (entry.Sentences.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"    <div><h3>{E(words.Sentences)}</h3>\n");
            for (var i = 0; i < entry.Sentences.Count; i++)
            {
                html.Append(CultureInfo.InvariantCulture, $"      <p class=\"sentence\"><span lang=\"ja\">{Furigana(entry.Sentences[i].Furigana, literal)}</span><br><span class=\"translation\">{E(kanji.Translations.ElementAtOrDefault(i) ?? string.Empty)}</span></p>\n");
            }

            html.Append("    </div>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"  </div>\n  <p class=\"credits\">{E(words.KanjiCredits)}</p>\n</section>\n");
    }

    /// <summary>
    /// One small square per stroke: the strokes already written in a light tone, the new one
    /// in ink with a dot where the brush starts, its number in the corner. Path data comes
    /// from a file, so anything that is not plain path data leaves the drawing out.
    /// </summary>
    private static void AppendStrokes(StringBuilder html, IReadOnlyList<string> strokes)
    {
        if (strokes.Count == 0 || !strokes.All(d => PathData().IsMatch(d)))
        {
            return;
        }

        html.Append("  <div class=\"strokes\" aria-hidden=\"true\">");
        for (var n = 0; n < strokes.Count; n++)
        {
            html.Append("<svg viewBox=\"0 0 109 109\"><path class=\"guide\" d=\"M54.5,0V109M0,54.5H109\"/>");
            for (var done = 0; done < n; done++)
            {
                html.Append(CultureInfo.InvariantCulture, $"<path class=\"done\" d=\"{strokes[done]}\"/>");
            }

            html.Append(CultureInfo.InvariantCulture, $"<path class=\"now\" d=\"{strokes[n]}\"/>");
            if (StartOf(strokes[n]) is { } start)
            {
                html.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{start.X}\" cy=\"{start.Y}\" r=\"6\"/>");
            }

            html.Append(CultureInfo.InvariantCulture, $"<text x=\"4\" y=\"17\">{n + 1}</text></svg>");
        }

        html.Append("</div>\n");
    }

    private static (string X, string Y)? StartOf(string path) =>
        PathStart().Match(path) is { Success: true } m ? (m.Groups[1].Value, m.Groups[2].Value) : null;

    /// <summary>
    /// A sentence with the reading over its kanji: "[場所|ば|しょ]" gives each kanji its own
    /// reading, "[今日|きょう]" one over the whole word. The kanji of the day stands out.
    /// </summary>
    public static string Furigana(string markup, string literal)
    {
        var html = new StringBuilder();
        var at = 0;
        foreach (Match m in RubyMarkup().Matches(markup))
        {
            html.Append(Marked(markup[at..m.Index], literal));
            var word = m.Groups[1].Value;
            var readings = m.Groups[2].Value.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var letters = StringInfo.GetTextElementEnumerator(word);
            var characters = new List<string>();
            while (letters.MoveNext())
            {
                characters.Add(letters.GetTextElement());
            }

            html.Append("<ruby>");
            if (readings.Length == characters.Count && readings.Length > 1)
            {
                for (var i = 0; i < characters.Count; i++)
                {
                    html.Append(CultureInfo.InvariantCulture, $"{Marked(characters[i], literal)}<rt>{E(readings[i])}</rt>");
                }
            }
            else
            {
                html.Append(CultureInfo.InvariantCulture, $"{Marked(word, literal)}<rt>{E(string.Concat(readings))}</rt>");
            }

            html.Append("</ruby>");
            at = m.Index + m.Length;
        }

        html.Append(Marked(markup[at..], literal));
        return html.ToString();
    }

    /// <summary>The text escaped, with the kanji of the day in bold.</summary>
    private static string Marked(string text, string literal) =>
        literal.Length == 0 ? E(text) : string.Join($"<b>{E(literal)}</b>", text.Split(literal).Select(E));

    [GeneratedRegex(@"\[([^\[\]|]+)((?:\|[^\[\]|]*)+)\]")]
    private static partial Regex RubyMarkup();

    [GeneratedRegex(@"^[MmCcSsLlHhVvZzQqTtAa0-9eE.,\s-]+$")]
    private static partial Regex PathData();

    [GeneratedRegex(@"^\s*[Mm]\s*(-?[\d.]+)[,\s]+(-?[\d.]+)")]
    private static partial Regex PathStart();

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
    private sealed record Words(string Lead, string GoodNews, string InThisIssue, string Photo, string Colophon, KanjiWords KanjiText)
    {
        public string Kanji => KanjiText.Title;

        public string Strokes => KanjiText.Strokes;

        public string Parts => KanjiText.Parts;

        public string WordsTitle => KanjiText.Words;

        public string Sentences => KanjiText.Sentences;

        public string KanjiCredits => KanjiText.Credits;

        public string GradeOf(int grade) =>
            grade is >= 1 and <= 6 ? string.Format(CultureInfo.InvariantCulture, KanjiText.Grade, grade) : KanjiText.Secondary;

        public static Words For(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
        {
            "it" => new("In apertura", "La buona notizia", "In questo numero", "Foto", "Scritto da un modello locale a partire dalle fonti citate: i fatti sono loro.",
                new("Il kanji del giorno", "{0} tratti", "Fatto di", "Parole", "Frasi", "{0}ª elementare", "scuole medie e superiori",
                    "Dati: KANJIDIC2 e JMdict (EDRDG, CC BY-SA 4.0), tratti da KanjiVG (Ulrich Apel, CC BY-SA 3.0), frasi da Tatoeba (CC BY 2.0 FR). Le traduzioni che Tatoeba non ha in italiano sono del modello locale.")),
            "ja" => new("トップ記事", "今日のいいニュース", "今号の内容", "写真", "ローカルモデルが出典をもとに書きました。事実は出典によります。",
                new("今日の漢字", "{0}画", "部品", "言葉", "例文", "小学{0}年", "中学以降",
                    "データ：KANJIDIC2・JMdict（EDRDG、CC BY-SA 4.0）、KanjiVG（Ulrich Apel、CC BY-SA 3.0）、Tatoeba（CC BY 2.0 FR）。")),
            "es" => new("Portada", "La buena noticia", "En este número", "Foto", "Escrito por un modelo local a partir de las fuentes citadas: los hechos son suyos.", KanjiWords.English),
            "fr" => new("À la une", "La bonne nouvelle", "Dans ce numéro", "Photo", "Écrit par un modèle local à partir des sources citées : les faits sont les leurs.", KanjiWords.English),
            "de" => new("Aufmacher", "Die gute Nachricht", "In dieser Ausgabe", "Foto", "Von einem lokalen Modell aus den verlinkten Quellen geschrieben: die Fakten stammen von ihnen.", KanjiWords.English),
            _ => new("Front page", "The good news", "In this issue", "Photo", "Written by a local model from the linked sources: the facts are theirs.", KanjiWords.English),
        };
    }

    /// <param name="Grade">With {0} for the school year; <paramref name="Secondary"/> for the kanji taught after it.</param>
    private sealed record KanjiWords(string Title, string Strokes, string Parts, string Words, string Sentences, string Grade, string Secondary, string Credits)
    {
        public static readonly KanjiWords English = new(
            "Kanji of the day", "{0} strokes", "Made of", "Words", "Sentences", "school year {0}", "secondary school",
            "Data: KANJIDIC2 and JMdict (EDRDG, CC BY-SA 4.0), strokes from KanjiVG (Ulrich Apel, CC BY-SA 3.0), sentences from Tatoeba (CC BY 2.0 FR). Translations Tatoeba lacks are by the local model.");
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
        .kanji { break-inside: avoid; margin-top: 14px; padding: 10px 12px 8px; border: 1.5px solid var(--rule); }
        .kanji h2 { margin: 0 0 8px; padding-bottom: 2px; border-bottom: 1.5px solid var(--rule); font-size: 13pt; text-transform: uppercase; letter-spacing: 0.12em; }
        .kanji-head { display: grid; grid-template-columns: 34mm 1fr; gap: 6mm; align-items: center; }
        .glyph { font-family: "Noto Serif CJK JP", serif; font-size: 72pt; line-height: 1; text-align: center; border: 0.5px solid #b9ad99; background: var(--box); padding: 3mm 0; }
        .facts p { margin: 0 0 3px; }
        .facts .meaning { font-size: 15pt; font-weight: 700; }
        .facts .label { display: inline-block; min-width: 9mm; font-size: 8.5pt; text-transform: uppercase; letter-spacing: 0.1em; color: var(--muted); }
        .facts [lang="ja"] { font-family: "Noto Serif CJK JP", serif; font-size: 12pt; }
        .facts .about { font-size: 9pt; color: var(--muted); }
        .facts .hint { font-style: italic; }
        .strokes { display: flex; flex-wrap: wrap; gap: 2mm; margin: 8px 0; }
        .strokes svg { width: 16mm; height: 16mm; border: 0.5px solid #b9ad99; background: #fffdf8; }
        .strokes path { fill: none; stroke-linecap: round; stroke-linejoin: round; }
        .strokes .guide { stroke: #ddd3c2; stroke-width: 0.8; stroke-dasharray: 3 3; }
        .strokes .done { stroke: #a89d8a; stroke-width: 4; }
        .strokes .now { stroke: var(--ink); stroke-width: 6; }
        .strokes circle { fill: var(--ink); }
        .strokes text { font-size: 15px; fill: var(--muted); font-family: "Noto Serif", serif; }
        .kanji-columns { display: grid; grid-template-columns: 1fr 1.4fr; gap: 8mm; }
        .kanji h3 { margin: 0 0 4px; font-size: 10pt; text-transform: uppercase; letter-spacing: 0.12em; }
        .words { margin: 0; padding: 0; list-style: none; }
        .words li { margin: 0 0 5px; }
        .word, .sentence [lang="ja"] { font-family: "Noto Serif CJK JP", serif; font-size: 12pt; }
        ruby rt { font-size: 0.5em; color: var(--muted); }
        .sentence { margin: 0 0 6px; line-height: 1.9; }
        .sentence .translation { font-size: 9.5pt; line-height: 1.3; font-style: italic; }
        .kanji b { font-weight: 900; text-decoration: underline; text-underline-offset: 3px; }
        .kanji .credits { margin: 6px 0 0; font-size: 7pt; color: var(--muted); }
        .issue .colophon { font-size: 7pt; text-transform: none; letter-spacing: 0; color: var(--muted); text-align: center; }
        """;
}
