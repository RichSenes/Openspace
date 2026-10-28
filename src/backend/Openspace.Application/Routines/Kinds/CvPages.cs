using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The CV's pages, for printing on A4. The CV wears the Karuta look: white paper, warm greys,
/// one red used sparingly, rounded corners. The rirekisho and the shokumu keirekisho keep the
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

    private const string CvStyle = """
        header{display:flex;gap:6mm;align-items:flex-start;padding-bottom:5mm;border-bottom:1px solid var(--hairline);margin-bottom:5mm}
        header .who{flex:1}
        h1{font-size:24pt;line-height:1.1;color:var(--charcoal);font-weight:700;letter-spacing:-0.01em}
        .headline{font-size:12pt;color:var(--primary);font-weight:600;margin-top:1.5mm}
        .contacts{margin-top:3mm;color:var(--mute);font-size:8.5pt;display:flex;flex-wrap:wrap;gap:1mm 4mm}
        .photo{width:30mm;height:38mm;object-fit:cover;border-radius:3mm;flex:none}
        section{margin-top:5mm}
        h2{font-size:8.5pt;text-transform:uppercase;letter-spacing:0.12em;color:var(--charcoal);font-weight:700;margin-bottom:2.5mm}
        .summary{font-size:10pt}
        .item{margin-bottom:3.5mm;break-inside:avoid}
        .item .top{display:flex;justify-content:space-between;gap:4mm}
        .item .what{font-weight:700;color:var(--charcoal)}
        .item .where{color:var(--mute)}
        .item .when{color:var(--mute);white-space:nowrap;font-size:8.5pt}
        ul{margin:1.2mm 0 0 4.5mm}
        li{margin-bottom:0.6mm}
        .chips{display:flex;flex-wrap:wrap;gap:1.4mm}
        .chip{background:var(--card);border-radius:999px;padding:0.6mm 2.6mm;font-size:8.5pt;color:var(--body)}
        .group{display:grid;grid-template-columns:34mm 1fr;gap:2mm;margin-bottom:2mm;break-inside:avoid}
        .group .name{color:var(--mute);font-size:8.5pt;padding-top:0.6mm}
        .pairs{display:flex;flex-wrap:wrap;gap:1mm 6mm}
        .pairs b{color:var(--charcoal)}
        a{color:inherit;text-decoration:none}
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
        html.Append("<header><div class=\"who\">");
        html.Append(CultureInfo.InvariantCulture, $"<h1>{E(me.Name)}</h1>");
        if (cv.Headline.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"headline\">{E(cv.Headline)}</p>");
        }

        var contacts = new[] { me.Email, me.Phone, me.Location }.Concat(me.Links).Where(c => c.Length > 0).ToList();
        if (contacts.Count > 0)
        {
            html.Append("<p class=\"contacts\">");
            foreach (var contact in contacts)
            {
                html.Append(CultureInfo.InvariantCulture, $"<span>{E(contact)}</span>");
            }

            html.Append("</p>");
        }

        html.Append("</div>");
        if (photo is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<img class=\"photo\" src=\"{E(photo)}\" alt=\"\">");
        }

        html.Append("</header>");
        if (cv.Summary.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(labels.Profile)}</h2><p class=\"summary\">{E(cv.Summary)}</p></section>");
        }

        Items(html, labels.Experience, cv.Experience.Select(j => (j.Role, Join(" · ", j.Company, j.Place), j.Period, j.Points)));
        Items(html, labels.Projects, cv.Projects.Select(p => (p.Name, Join(" · ", p.Kind, p.Link), p.Period, p.Points)));
        Items(html, labels.Education, cv.Education.Select(s => (s.Title, Join(" · ", s.School, s.Note), s.Period, (IReadOnlyList<string>)[])));

        if (cv.Skills.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(labels.Skills)}</h2>");
            foreach (var group in cv.Skills.Where(g => g.Items.Count > 0))
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"group\"><span class=\"name\">{E(group.Group)}</span><span class=\"chips\">");
                foreach (var item in group.Items)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<span class=\"chip\">{E(item)}</span>");
                }

                html.Append("</span></div>");
            }

            html.Append("</section>");
        }

        if (cv.Languages.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(labels.Languages)}</h2><p class=\"pairs\">");
            foreach (var language in cv.Languages)
            {
                html.Append(CultureInfo.InvariantCulture, $"<span><b>{E(language.Name)}</b> {E(language.Level)}</span>");
            }

            html.Append("</p></section>");
        }

        if (cv.Certifications.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(labels.Certifications)}</h2>");
            foreach (var certificate in cv.Certifications)
            {
                html.Append(CultureInfo.InvariantCulture, $"<p><b>{E(certificate.Name)}</b> · {E(Join(", ", certificate.Issuer, certificate.Year))}</p>");
            }

            html.Append("</section>");
        }

        if (cv.Interests.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(labels.Interests)}</h2><p>{E(cv.Interests)}</p></section>");
        }

        return html.Append("</body></html>").ToString();
    }

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

        html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(heading)}</h2>");
        foreach (var (what, where, when, points) in list)
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"item\"><div class=\"top\"><span><span class=\"what\">{E(what)}</span>");
            if (where.Length > 0)
            {
                html.Append(CultureInfo.InvariantCulture, $" <span class=\"where\">· {E(where)}</span>");
            }

            html.Append(CultureInfo.InvariantCulture, $"</span><span class=\"when\">{E(when)}</span></div>");
            Points(html, points);
            html.Append("</div>");
        }

        html.Append("</section>");
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
