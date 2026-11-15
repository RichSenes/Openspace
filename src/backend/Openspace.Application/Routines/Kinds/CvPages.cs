using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The CV's pages, for printing on A4. The CV wears Karuta's look, with a developer's touch. The rirekisho and the shokumu keirekisho keep the
/// plain ruled layout Japanese companies expect.
/// </summary>
public static class CvPages
{
    /// <summary>Colours and type shared by the pages; the red reaches 4.6:1 on white, the greys more.</summary>
    private const string Base = """
        :root{--canvas:#ffffff;--card:#f6f6f3;--hairline:#e5e5e0;--charcoal:#262622;--body:#33332e;--mute:#62625b;--primary:#e60023}
        @page{size:A4;margin:14mm 15mm}
        *{box-sizing:border-box;margin:0;padding:0}
        html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
        body{font-family:"Noto Sans","Noto Sans CJK JP","Hiragino Sans",system-ui,sans-serif;color:var(--body);background:var(--canvas);font-size:9.5pt;line-height:1.45}
        html:lang(ja) body{font-family:"Noto Sans CJK JP","Hiragino Sans","Noto Sans",system-ui,sans-serif}
        """;

    /// <summary>
    /// The CV in Karuta's look: Roboto, warm greys, one red, soft rounded surfaces, on a 4 px
    /// rhythm. The header is a soft card with the photo, the name and the contacts; each section
    /// has its name in a narrow column on the left (placed apart, so a section may run on to the
    /// next page) and a hairline above. Dates sit in a column of their own, so they line up; the
    /// points start with a small red chevron, a developer's touch and no more.
    /// </summary>
    private const string CvStyle = """
        :root{--ink:#262622;--body:#33332e;--mute:#62625b;--ash:#91918c;--hairline:#e5e5e0;--card:#f6f6f3;--primary:#e60023}
        @page{size:A4;margin:12mm 14mm}
        body{font-family:"CV Roboto",Roboto,"Noto Sans","Noto Sans CJK JP",sans-serif;font-size:9.3pt;line-height:1.45;color:var(--body)}
        html:lang(ja) body{font-family:"CV Roboto","Noto Sans CJK JP",sans-serif}
        .mono{font-family:"DejaVu Sans Mono","Ubuntu Mono",monospace;letter-spacing:-0.01em}
        html:lang(ja) .mono{font-family:"Noto Sans CJK JP",monospace}
        header{display:grid;grid-template-columns:28mm minmax(0,1fr) auto;gap:0 6mm;align-items:center;background:var(--card);border-radius:4mm;padding:5mm}
        header.nophoto{grid-template-columns:minmax(0,1fr) auto}
        .photo{width:28mm;height:34mm;object-fit:cover;border-radius:3mm;display:block}
        h1{font-weight:700;font-size:26pt;line-height:1.05;letter-spacing:-0.012em;color:var(--ink)}
        html:lang(ja) h1{font-size:20pt;letter-spacing:0;line-height:1.2}
        .headline{margin-top:2mm;font-size:11pt;font-weight:500;color:var(--primary);line-height:1.3}
        .contacts{font-size:7.8pt;line-height:1.8;color:var(--body);text-align:right;word-break:break-word;max-width:62mm}
        .row{padding:4mm 0 0 34mm;margin-top:4mm;border-top:.5pt solid var(--hairline)}
        header+.row{border-top:0;margin-top:2mm}
        .lead{position:relative;break-inside:avoid}
        .item{position:relative}
        h2{position:absolute;left:-34mm;top:.4mm;width:30mm;font-size:7.4pt;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--mute)}
        .summary{font-size:10pt;line-height:1.55;color:var(--ink)}
        .item{display:grid;grid-template-columns:minmax(0,1fr) 27mm;gap:0 4mm;break-inside:avoid}
        .item+.item{margin-top:3.6mm}
        .what{font-weight:700;color:var(--ink);font-size:10pt;line-height:1.3}
        .when{grid-column:2;grid-row:1;font-size:8.2pt;color:var(--mute);font-variant-numeric:tabular-nums;white-space:nowrap;padding-top:.5mm}
        .where{grid-column:1;color:var(--mute);font-size:8.6pt;margin-top:.4mm}
        ul{grid-column:1/-1;list-style:none;margin-top:1.6mm}
        li{position:relative;padding-left:4mm;margin-bottom:.9mm}
        li::before{content:"\203A";position:absolute;left:.4mm;top:-.1em;color:var(--primary);font-weight:700;font-size:10.5pt;line-height:1.45}
        .groups{display:grid;grid-template-columns:23mm minmax(0,1fr);gap:2mm 3mm;align-items:start}
        .groups>span{color:var(--mute);font-size:8.2pt;padding-top:.5mm}
        .skills{color:var(--ink)}
        .skills i{font-style:normal;color:var(--ash);padding:0 1.6mm}
        .pairs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:2mm 4mm}
        .pairs b{display:block;color:var(--ink);font-weight:700}
        .pairs span{color:var(--mute);font-size:8.4pt}
        .list p+p{margin-top:1.2mm}
        .list b{color:var(--ink);font-weight:700}
        .list span{color:var(--mute)}
        """;

    private const string FormStyle = """
        body{font-size:10pt}
        h1{font-size:18pt;letter-spacing:0.4em;font-weight:700;color:var(--charcoal)}
        .head{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:3mm}
        .date{font-size:9pt}
        table{width:100%;border-collapse:collapse;table-layout:fixed}
        td,th{border:0.3mm solid var(--charcoal);padding:1.4mm 2mm;vertical-align:top;font-weight:400;text-align:left}
        th{font-size:8pt;color:var(--mute);white-space:nowrap}
        .kana{font-size:8pt;color:var(--mute);border-bottom:0.2mm dashed var(--mute)}
        .name{font-size:16pt;height:16mm;vertical-align:middle}
        .top{display:grid;grid-template-columns:1fr 32mm;gap:4mm;align-items:start}
        .photo{width:30mm;height:40mm;border:0.3mm dashed var(--mute);display:flex;align-items:center;justify-content:center;font-size:7.5pt;color:var(--mute);text-align:center}
        .photo img{width:30mm;height:40mm;object-fit:cover}
        .rows td{height:7.4mm}
        .rows .y{width:16mm;text-align:center}
        .rows .m{width:10mm;text-align:center}
        .rows .center{text-align:center}
        .rows .end{text-align:right}
        .box{height:52mm}
        .wish{height:32mm}
        .page{break-after:page}
        section{margin-top:5mm;break-inside:avoid}
        h2{font-size:11pt;font-weight:700;color:var(--charcoal);margin-bottom:1.5mm}
        h3{font-size:10pt;font-weight:700;color:var(--charcoal)}
        .job{border:0.3mm solid var(--hairline);border-radius:2mm;padding:3mm;margin-bottom:3mm;break-inside:avoid}
        .meta{color:var(--mute);font-size:9pt;margin:0.8mm 0 1.5mm}
        ul{margin-left:4.5mm}
        .right{text-align:right}
        """;

    public static string Cv(CvContent cv, PersonalData me, string? photo, CvLabels labels)
    {
        ArgumentNullException.ThrowIfNull(cv);
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(labels);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""<!doctype html><html lang="{labels.Lang}"><head><meta charset="utf-8"><title>{E(me.Name)}</title><style>{Base}{Fonts.Value}{CvStyle}</style></head><body>""");
        html.Append(photo is null ? "<header class=\"nophoto\">" : $"<header><img class=\"photo\" src=\"{E(photo)}\" alt=\"\">");
        html.Append(CultureInfo.InvariantCulture, $"<div><h1>{E(me.Name)}</h1>");
        if (cv.Headline.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"headline\">{E(cv.Headline)}</p>");
        }

        html.Append("</div>");
        var contacts = new[] { me.Email, me.Phone, me.Location }.Concat(me.Links).Where(c => c.Length > 0).ToList();
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"contacts mono\">{string.Join("<br>", contacts.Select(E))}</div></header>");

        if (cv.Summary.Length > 0)
        {
            Row(html, labels.Profile, $"<p class=\"summary\">{E(cv.Summary)}</p>");
        }

        Items(html, labels.Experience, cv.Experience.Select(j => (j.Role, Join(" · ", j.Company, j.Place), j.Period, j.Points)));
        Items(html, labels.Projects, cv.Projects.Select(p => (p.Name, Join(" · ", p.Kind, p.Link), p.Period, p.Points)));
        Items(html, labels.Education, cv.Education.Select(s => (s.Title, Join(" · ", s.School, s.Note), s.Period, (IReadOnlyList<string>)[])));

        var groups = cv.Skills.Where(g => g.Items.Count > 0).ToList();
        if (groups.Count > 0)
        {
            var skills = new StringBuilder("<div class=\"groups\">");
            foreach (var group in groups)
            {
                // A plain line, the skills apart by quiet dots.
                skills.Append(CultureInfo.InvariantCulture,
                    $"<span>{E(group.Group)}</span><p class=\"skills\">{string.Join("<i>·</i>", group.Items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => E(i.Trim())))}</p>");
            }

            Row(html, labels.Skills, skills.Append("</div>").ToString());
        }

        if (cv.Languages.Count > 0)
        {
            Row(html, labels.Languages, $"<div class=\"pairs\">{string.Concat(cv.Languages.Select(l => $"<p><b>{E(l.Name)}</b><span>{E(l.Level)}</span></p>"))}</div>");
        }

        if (cv.Certifications.Count > 0)
        {
            Row(html, labels.Certifications, $"<div class=\"list\">{string.Concat(cv.Certifications.Select(c => $"<p><b>{E(c.Name)}</b> <span>· {E(Join(", ", c.Issuer, c.Year))}</span></p>"))}</div>");
        }

        if (cv.Interests.Length > 0)
        {
            Row(html, labels.Interests, $"<p>{E(cv.Interests)}</p>");
        }

        return html.Append("</body></html>").ToString();
    }

    /// <summary>A section of the CV: its name in the left column, what it holds on the right.</summary>
    /// <remarks>The name sits inside what comes first, so that it never ends a page alone.</remarks>
    private static void Row(StringBuilder html, string heading, string content) =>
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"row\"><div class=\"lead\"><h2>{E(heading)}</h2>{content}</div></div>");

    /// <summary>The rirekisho on two pages: who, then 学歴・職歴; then 免許・資格, 志望の動機 and 本人希望記入欄.</summary>
    public static string Rirekisho(Rirekisho form, PersonalData me, string? photo, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(me);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>履歴書 {E(me.Name)}</title><style>{Base}{FormStyle}</style></head><body><div class="page">""");
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"head\"><h1>履歴書</h1><span class=\"date\">{Jp(today)}現在</span></div>");
        html.Append("<div class=\"top\"><table>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th style=\"width:22mm\">ふりがな</th><td class=\"kana\">{E(me.NameKana)}</td></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th>氏名</th><td class=\"name\">{E(me.Name)}</td></tr>");
        var age = me.Birth is { } birth ? today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0) : (int?)null;
        html.Append(CultureInfo.InvariantCulture, $"<tr><th>生年月日</th><td>{(me.Birth is { } b ? $"{Jp(b)}生（満 {age} 歳）" : "　　年　　月　　日生（満　　歳）")}</td></tr>");
        html.Append("</table>");
        html.Append(photo is null
            ? "<div class=\"photo\">写真<br>縦 40mm<br>横 30mm</div>"
            : $"<div class=\"photo\"><img src=\"{E(photo)}\" alt=\"\"></div>");
        html.Append("</div><table style=\"margin-top:3mm\">");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th style=\"width:22mm\">ふりがな</th><td class=\"kana\">{E(me.AddressKana)}</td></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th>現住所</th><td style=\"height:14mm\">{E(me.Address)}</td></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th>電話</th><td>{E(me.Phone)}</td></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr><th>メール</th><td>{E(me.Email)}</td></tr>");
        html.Append("</table>");

        html.Append("<table class=\"rows\" style=\"margin-top:4mm\"><tr><th class=\"y\">年</th><th class=\"m\">月</th><th class=\"center\">学歴・職歴</th></tr>");
        html.Append("<tr><td></td><td></td><td class=\"center\">学歴</td></tr>");
        Rows(html, form.Education);
        html.Append("<tr><td></td><td></td><td class=\"center\">職歴</td></tr>");
        Rows(html, form.Work);
        html.Append("<tr><td></td><td></td><td class=\"end\">以上</td></tr>");
        var used = form.Education.Count + form.Work.Count + 3;
        for (var i = used; i < 18; i++)
        {
            html.Append("<tr><td></td><td></td><td></td></tr>");
        }

        html.Append("</table></div>");
        html.Append("<table class=\"rows\"><tr><th class=\"y\">年</th><th class=\"m\">月</th><th class=\"center\">免許・資格</th></tr>");
        Rows(html, form.Licenses);
        for (var i = form.Licenses.Count; i < 6; i++)
        {
            html.Append("<tr><td></td><td></td><td></td></tr>");
        }

        html.Append("</table>");
        html.Append(CultureInfo.InvariantCulture, $"<table style=\"margin-top:4mm\"><tr><th>志望の動機、特技、好きな学科、アピールポイントなど</th></tr><tr><td class=\"box\">{Lines(form.Motivation)}</td></tr></table>");
        html.Append(CultureInfo.InvariantCulture, $"<table style=\"margin-top:4mm\"><tr><th>本人希望記入欄（特に給料・職種・勤務時間・勤務地・その他についての希望などがあれば記入）</th></tr><tr><td class=\"wish\">{Lines(form.Wishes)}</td></tr></table>");
        return html.Append("</body></html>").ToString();
    }

    /// <summary>The shokumu keirekisho: summary, each job with its work, personal projects, skills, self-presentation.</summary>
    public static string Shokumu(Shokumu history, PersonalData me, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(me);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"""<!doctype html><html lang="ja"><head><meta charset="utf-8"><title>職務経歴書 {E(me.Name)}</title><style>{Base}{FormStyle}</style></head><body>""");
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"head\"><h1>職務経歴書</h1><span class=\"date right\">{Jp(today)}現在<br>氏名　{E(me.Name)}</span></div>");
        html.Append(CultureInfo.InvariantCulture, $"<section><h2>■ 職務要約</h2><p>{Lines(history.Summary)}</p></section>");
        if (history.Jobs.Count > 0)
        {
            html.Append("<section><h2>■ 職務経歴</h2>");
            foreach (var job in history.Jobs)
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"job\"><h3>{E(job.Company)}</h3>");
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"meta\">{E(Join("　", job.Period, job.Employment))}<br>{E(job.Business)}</p>");
                if (job.Role.Length > 0)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<p><b>{E(job.Role)}</b></p>");
                }

                Points(html, job.Points);
                html.Append("</div>");
            }

            html.Append("</section>");
        }

        if (history.Projects.Count > 0)
        {
            html.Append("<section><h2>■ 個人開発</h2>");
            foreach (var project in history.Projects)
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"job\"><h3>{E(project.Name)}</h3><p class=\"meta\">{E(Join("　", project.Period, project.Kind, project.Link))}</p>");
                Points(html, project.Points);
                html.Append("</div>");
            }

            html.Append("</section>");
        }

        if (history.Skills.Count > 0)
        {
            html.Append("<section><h2>■ 活かせる経験・知識・技術</h2><table>");
            foreach (var group in history.Skills.Where(g => g.Items.Count > 0))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><th style=\"width:38mm\">{E(group.Group)}</th><td>{E(string.Join("、", group.Items))}</td></tr>");
            }

            html.Append("</table></section>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<section><h2>■ 自己PR</h2><p>{Lines(history.Pr)}</p></section>");
        html.Append("<p class=\"right\" style=\"margin-top:4mm\">以上</p>");
        return html.Append("</body></html>").ToString();
    }

    /// <summary>Roboto embedded in the page, in the three weights the CV uses; none when the files are missing.</summary>
    private static readonly Lazy<string> Fonts = new(() =>
    {
        const string Latin = "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";
        const string LatinExt = "U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+1D00-1DBF,U+1E00-1E9F,U+1EF2-1EFF,U+2020,U+20A0-20AB,U+20AD-20C0,U+2113,U+2C60-2C7F,U+A720-A7FF";
        var css = new StringBuilder();
        var assembly = typeof(CvPages).Assembly;
        foreach (var weight in new[] { 400, 500, 700 })
        {
            foreach (var (subset, range) in new[] { ("latin", Latin), ("latin-ext", LatinExt) })
            {
                using var stream = assembly.GetManifestResourceStream($"Openspace.Fonts.roboto-{subset}-{weight}.woff2");
                if (stream is null)
                {
                    continue;
                }

                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                css.Append(CultureInfo.InvariantCulture,
                    $"@font-face{{font-family:\"CV Roboto\";font-weight:{weight};font-style:normal;unicode-range:{range};src:url(data:font/woff2;base64,{Convert.ToBase64String(bytes.ToArray())}) format(\"woff2\")}}");
            }
        }

        return css.ToString();
    });

    private static void Items(StringBuilder html, string heading, IEnumerable<(string What, string Where, string When, IReadOnlyList<string> Points)> items)
    {
        var list = items.Where(i => i.What.Length > 0).ToList();
        if (list.Count == 0)
        {
            return;
        }

        // The section's name goes inside its first item, which never breaks: the two stay together.
        var content = new StringBuilder("<div class=\"row\">");
        foreach (var ((what, where, when, points), i) in list.Select((item, i) => (item, i)))
        {
            content.Append(CultureInfo.InvariantCulture, $"<div class=\"item\">{(i == 0 ? $"<h2>{E(heading)}</h2>" : string.Empty)}<span class=\"what\">{E(what)}</span>");
            content.Append(when.Length > 0 ? $"<span class=\"when\">{E(when)}</span>" : string.Empty);
            if (where.Length > 0)
            {
                content.Append(CultureInfo.InvariantCulture, $"<span class=\"where\">{E(where)}</span>");
            }

            Points(content, points);
            content.Append("</div>");
        }

        html.Append(content.Append("</div>"));
    }

    private static void Points(StringBuilder html, IReadOnlyList<string> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        html.Append("<ul>");
        foreach (var point in points.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            html.Append(CultureInfo.InvariantCulture, $"<li>{E(point)}</li>");
        }

        html.Append("</ul>");
    }

    private static void Rows(StringBuilder html, IReadOnlyList<Rirekisho.Row> rows)
    {
        foreach (var row in rows)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"y\">{(row.Year > 0 ? row.Year.ToString(CultureInfo.InvariantCulture) : string.Empty)}</td><td class=\"m\">{(row.Month is >= 1 and <= 12 ? row.Month.ToString(CultureInfo.InvariantCulture) : string.Empty)}</td><td>{E(row.Text)}</td></tr>");
        }
    }

    /// <summary>"2026年10月28日".</summary>
    private static string Jp(DateOnly date) => string.Create(CultureInfo.InvariantCulture, $"{date.Year}年{date.Month}月{date.Day}日");

    private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

    private static string Lines(string text) => E(text).ReplaceLineEndings("<br>");

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
