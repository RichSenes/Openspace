using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The CV's pages, for printing on A4. The CV is a strict grid in a frontend developer's hand:
/// section names on the left, the name large, details in monospace, one indigo accent. The rirekisho and the shokumu keirekisho keep the
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
    /// The CV: a strict layout with the section names in a narrow column on the left (placed
    /// apart, so that a section may run on to the next page), the name
    /// large and tight, details (contacts, dates, skills) in monospace, one indigo accent for
    /// the dates and markers; the photo sits in the left column, over the section names.
    /// </summary>
    private const string CvStyle = """
        :root{--ink:#111111;--body:#2d2d2d;--mute:#6b6b6b;--rule:#e6e6e6;--accent:#4338ca;--soft:#eef0ff;--chip:#d4d4d8}
        @page{size:A4;margin:14mm 15mm}
        body{font-size:9.2pt;line-height:1.46;color:var(--body)}
        .mono{font-family:"DejaVu Sans Mono","Ubuntu Mono","Noto Sans Mono",monospace}
        html:lang(ja) .mono{font-family:"Noto Sans CJK JP","DejaVu Sans Mono",monospace}
        header{display:grid;grid-template-columns:30mm minmax(0,1fr) 60mm;gap:0 7mm;align-items:center;margin-bottom:6mm}
        header.nophoto{grid-template-columns:minmax(0,1fr) 60mm}
        .photo{width:30mm;height:37mm;object-fit:cover;border-radius:1.6mm;display:block}
        h1{font-family:"Noto Sans Display","Noto Sans","Noto Sans CJK JP",sans-serif;font-weight:900;font-size:34pt;line-height:.98;letter-spacing:-0.035em;color:var(--ink)}
        html:lang(ja) h1{font-size:20pt;letter-spacing:0;line-height:1.2}
        .headline{margin-top:3mm;font-size:9.6pt;color:var(--accent);font-weight:700;line-height:1.35}
        .headline::before{content:"// ";color:var(--mute);font-weight:400}
        .contacts{font-size:8pt;line-height:1.75;border-left:2pt solid var(--accent);padding-left:4mm;color:var(--body);word-break:break-word}
        .row{position:relative;padding-left:37mm;margin-top:4.6mm}
        h2{position:absolute;left:0;top:0;width:30mm;font-size:7.8pt;text-transform:uppercase;letter-spacing:.14em;color:var(--mute);font-weight:700;padding-top:.7mm}
        .summary{font-size:10.1pt;color:var(--ink);line-height:1.55}
        .item{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:0 4mm;break-inside:avoid;padding-bottom:2.8mm;margin-bottom:2.8mm;border-bottom:.5pt solid var(--rule)}
        .item:last-child{border-bottom:0;margin-bottom:0;padding-bottom:0}
        .what{font-weight:700;color:var(--ink);font-size:10.1pt}
        .where{grid-column:1/-1;color:var(--mute);font-size:8.7pt}
        .when{font-size:7.6pt;color:var(--accent);white-space:nowrap;background:var(--soft);border-radius:1mm;padding:.3mm 1.6mm;align-self:start;margin-top:.5mm}
        ul{grid-column:1/-1;list-style:none;margin-top:1.4mm}
        li{padding-left:4.5mm;position:relative;margin-bottom:.7mm}
        li::before{content:"→";position:absolute;left:0;color:var(--accent);font-size:8pt}
        .groups{display:grid;grid-template-columns:24mm minmax(0,1fr);gap:1.6mm 3mm;align-items:start}
        .groups>span{color:var(--mute);font-size:8.1pt;padding-top:.4mm}
        .chips{display:flex;flex-wrap:wrap;gap:1.4mm}
        .chip{border:.6pt solid var(--chip);border-radius:1mm;padding:.35mm 1.8mm;font-size:7.9pt;color:var(--ink)}
        .pairs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:2mm 4mm}
        .pairs b{display:block;color:var(--ink);font-weight:700}
        .pairs span{color:var(--mute);font-size:8.3pt}
        .list p+p{margin-top:1mm}
        .list b{color:var(--ink)}
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
        html.Append(CultureInfo.InvariantCulture, $"""<!doctype html><html lang="{labels.Lang}"><head><meta charset="utf-8"><title>{E(me.Name)}</title><style>{Base}{CvStyle}</style></head><body>""");
        html.Append(photo is null ? "<header class=\"nophoto\">" : $"<header><img class=\"photo\" src=\"{E(photo)}\" alt=\"\">");
        html.Append(CultureInfo.InvariantCulture, $"<div><h1>{E(me.Name)}</h1>");
        if (cv.Headline.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"headline mono\">{E(cv.Headline)}</p>");
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
                skills.Append(CultureInfo.InvariantCulture, $"<span>{E(group.Group)}</span><div class=\"chips\">");
                foreach (var item in group.Items.Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    skills.Append(CultureInfo.InvariantCulture, $"<span class=\"chip mono\">{E(item)}</span>");
                }

                skills.Append("</div>");
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
    private static void Row(StringBuilder html, string heading, string content) =>
        html.Append(CultureInfo.InvariantCulture, $"<div class=\"row\"><h2>{E(heading)}</h2><div>{content}</div></div>");

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

    private static void Items(StringBuilder html, string heading, IEnumerable<(string What, string Where, string When, IReadOnlyList<string> Points)> items)
    {
        var list = items.Where(i => i.What.Length > 0).ToList();
        if (list.Count == 0)
        {
            return;
        }

        var content = new StringBuilder();
        foreach (var (what, where, when, points) in list)
        {
            content.Append(CultureInfo.InvariantCulture, $"<div class=\"item\"><span class=\"what\">{E(what)}</span>");
            content.Append(when.Length > 0 ? $"<span class=\"when mono\">{E(when)}</span>" : "<span></span>");
            if (where.Length > 0)
            {
                content.Append(CultureInfo.InvariantCulture, $"<span class=\"where\">{E(where)}</span>");
            }

            Points(content, points);
            content.Append("</div>");
        }

        Row(html, heading, content.ToString());
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
