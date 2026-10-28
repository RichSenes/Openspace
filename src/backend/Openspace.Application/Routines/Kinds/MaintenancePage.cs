using System.Globalization;
using System.Net;
using System.Text;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// The maintenance report for printing on A4: per project, the vulnerable packages and the ones
/// that moved on; then the tools and the system. Severity is written out in words, never told by
/// colour alone.
/// </summary>
internal static class MaintenancePage
{
    private const string Style = """
        :root{--canvas:#ffffff;--card:#f6f6f3;--hairline:#e5e5e0;--charcoal:#262622;--body:#33332e;--mute:#62625b;--primary:#e60023}
        @page{size:A4;margin:14mm 15mm}
        *{box-sizing:border-box;margin:0;padding:0}
        html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
        body{font-family:"Noto Sans","Noto Sans CJK JP",system-ui,sans-serif;color:var(--body);background:var(--canvas);font-size:8.5pt;line-height:1.4}
        header{padding-bottom:4mm;border-bottom:1px solid var(--hairline);margin-bottom:4mm}
        h1{font-size:20pt;color:var(--charcoal);font-weight:700}
        .date{color:var(--mute);margin-top:1mm}
        section{margin-top:5mm;break-inside:auto}
        h2{font-size:12pt;color:var(--charcoal);font-weight:700;margin-bottom:2mm}
        h3{font-size:8pt;text-transform:uppercase;letter-spacing:0.1em;color:var(--mute);font-weight:700;margin:3mm 0 1.5mm}
        table{width:100%;border-collapse:collapse}
        th{text-align:left;font-weight:600;color:var(--mute);border-bottom:1px solid var(--hairline);padding:1mm 1.5mm}
        td{border-bottom:1px solid var(--hairline);padding:1mm 1.5mm;vertical-align:top}
        tr{break-inside:avoid}
        .name{font-weight:600;color:var(--charcoal);word-break:break-all}
        .rate{font-weight:700;white-space:nowrap}
        .rate.top{color:var(--primary)}
        .tag{background:var(--card);border-radius:999px;padding:0 2mm;font-size:7.5pt;white-space:nowrap}
        .none{color:var(--mute)}
        ul{margin-left:4mm}
        """;

    public static string Render(
        string title, string date, MaintenanceWords words, IReadOnlyList<ProjectReport> reports, IReadOnlyList<Toolchain> tools, SystemUpdates? waiting)
    {
        var html = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{words.Culture.TwoLetterISOLanguageName}\"><head><meta charset=\"utf-8\"><title>{E(title)}</title><style>{Style}</style></head><body>")
            .Append(CultureInfo.InvariantCulture, $"<header><h1>{E(title)}</h1><div class=\"date\">{E(date)}</div></header>");

        foreach (var report in reports)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(report.Project.Name)}</h2>");
            html.Append(CultureInfo.InvariantCulture, $"<h3>{E(words.Security)}</h3>");
            if (report.Fixes.Count == 0)
            {
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"none\">{E(words.NoneKnown)}</p>");
            }
            else
            {
                html.Append(CultureInfo.InvariantCulture, $"<table><tr><th>{E(words.Package)}</th><th>{E(words.InUse)}</th><th>{E(words.Fixed)}</th><th></th><th></th></tr>");
                foreach (var fix in report.Fixes)
                {
                    var advisories = string.Join("<br>", fix.Advisories.Select(a =>
                        $"<span class=\"rate{(a.Severity >= Severity.High ? " top" : string.Empty)}\">{E(words.Rate(a.Severity))}</span> {E(a.Name)}: {E(a.Summary)}"));
                    html.Append(CultureInfo.InvariantCulture,
                        $"<tr><td class=\"name\">{E(fix.Name)}{(fix.Direct ? string.Empty : $" <span class=\"tag\">{E(words.Indirect.Trim('(', ')'))}</span>")}</td><td>{E(fix.From)}</td><td>{E(fix.To ?? words.NoFix)}</td><td colspan=\"2\">{advisories}</td></tr>");
                }

                html.Append("</table>");
            }

            if (report.Updates.Count > 0)
            {
                html.Append(CultureInfo.InvariantCulture, $"<h3>{E(words.Updates)}</h3><table><tr><th>{E(words.Package)}</th><th>{E(words.InUse)}</th><th>{E(words.Latest)}</th><th></th></tr>");
                foreach (var update in report.Updates)
                {
                    html.Append(CultureInfo.InvariantCulture,
                        $"<tr><td class=\"name\">{E(update.Name)}</td><td>{E(update.From)}</td><td>{E(update.To)}</td><td>{(update.Major ? $"<span class=\"tag\">{E(words.Major)}</span>" : string.Empty)}</td></tr>");
                }

                html.Append("</table>");
            }

            html.Append("</section>");
        }

        if (tools.Count > 0 || waiting is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section><h2>{E(words.Tools)} · {E(words.System)}</h2><ul>");
            foreach (var tool in tools)
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{E(tool.Name)} {E(tool.Installed)}{(tool.Latest is null ? $": {E(words.Current)}" : $" → {E(tool.Latest)}{(tool.Security ? $" · <b>{E(words.SecurityFix)}</b>" : string.Empty)}")}</li>");
            }

            if (waiting is not null)
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{E(waiting.Packages == 0 ? words.SystemCurrent : words.SystemPackages(waiting.Packages, waiting.Security))}</li>");
                if (waiting.Apps.Count > 0)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<li>{E(words.Apps)}: {E(string.Join(", ", waiting.Apps))}</li>");
                }

                if (waiting.Restart)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<li>{E(words.Restart)}</li>");
                }
            }

            html.Append("</ul></section>");
        }

        return html.Append("</body></html>").ToString();
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
