using System.Globalization;
using System.Text;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>A readable Markdown account of a task: what was asked, who did what, what changed.</summary>
public static class TaskReport
{
    public static string Build(Office office, WorkTask task, IReadOnlyList<AuditEntry> log)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(log);

        var desk = task.DeskId is { } id && office.HasDesk(id) ? office.Locate(id) : default;
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {task.Title}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- **Office:** {office.Name}")
            .AppendLine(CultureInfo.InvariantCulture, $"- **Desk:** {(desk.Desk is null ? "none" : $"{desk.Room.Name} / {desk.Desk.Name} ({desk.Desk.Agent.AgentName})")}")
            .AppendLine(CultureInfo.InvariantCulture, $"- **Asked by:** {task.RequestedBy}")
            .AppendLine(CultureInfo.InvariantCulture, $"- **Created:** {Stamp(task.CreatedAt)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- **Status:** {task.Status}");
        if (task.Run is { } run)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- **Branch:** `{run.Branch}`");
        }

        if (task.MergedInto is { } target)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- **Merged into:** `{target}`");
        }

        if (task.Description.Length > 0)
        {
            text.AppendLine().AppendLine("## Request").AppendLine().AppendLine(task.Description);
        }

        AppendList(text, "Files changed", Distinct(log, AuditKind.Change));
        AppendList(text, "Commands run", Distinct(log, AuditKind.Command));

        text.AppendLine().AppendLine("## Timeline").AppendLine();
        foreach (var entry in log.Where(e => e.Kind is not (AuditKind.Read or AuditKind.Search)))
        {
            var failed = entry.Succeeded == false ? " (failed)" : string.Empty;
            text.AppendLine(CultureInfo.InvariantCulture, $"- {Stamp(entry.At)} — {entry.Summary}{failed}");
            if (entry.Kind == AuditKind.Note && entry.Detail is { } note)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  > {note.ReplaceLineEndings("\n  > ")}");
            }
        }

        return text.ToString();
    }

    private static List<string> Distinct(IReadOnlyList<AuditEntry> log, AuditKind kind) =>
        [.. log.Where(e => e.Kind == kind).Select(e => e.Summary).Distinct(StringComparer.Ordinal)];

    private static void AppendList(StringBuilder text, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {title}").AppendLine();
        items.ForEach(i => text.AppendLine(CultureInfo.InvariantCulture, $"- {i}"));
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
