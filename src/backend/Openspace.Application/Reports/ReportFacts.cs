using System.Globalization;
using System.Text;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Reports;

/// <summary>A task and the lines of its log that fall in the report.</summary>
public sealed record TaskActivity(WorkTask Task, IReadOnlyList<AuditEntry> Log);

/// <summary>
/// The facts behind a report, as a plain Markdown list built only from what Openspace
/// recorded. It is what the model writes from, and the report itself when no model answers.
/// </summary>
public static class ReportFacts
{
    private const int MaxCommands = 10;
    private const int MaxNoteLength = 600;

    public static string Build(Office office, string scope, IReadOnlyList<TaskActivity> tasks, Epic? epic = null)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(tasks);
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {office.Name}: {scope}")
            .AppendLine();

        if (epic is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Request by {epic.RequestedBy}: {epic.Request}").AppendLine();
        }

        if (tasks.Count == 0)
        {
            return text.AppendLine("No task was worked on in this period.").ToString();
        }

        var byStatus = tasks.GroupBy(t => t.Task.Status).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Tasks: {tasks.Count} ({string.Join(", ", byStatus)})");

        foreach (var (task, log) in tasks)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {task.Title}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Status: {task.Status}");
            if (task.DeskId is { } deskId && office.HasDesk(deskId))
            {
                var (room, desk) = office.Locate(deskId);
                text.AppendLine(CultureInfo.InvariantCulture, $"- Desk: {desk.Name} (room {room.Name})");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"- Asked by: {task.RequestedBy}");
            if (task.Description.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Request: {Short(task.Description)}");
            }

            if (task.MergedInto is { } target)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Merged into {target}");
            }
            else if (task.Run?.Branch is { } branch)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Branch: {branch}");
            }

            List("Files changed", text, Distinct(log, AuditKind.Change));
            var commands = log.Where(e => e.Kind == AuditKind.Command).ToList();
            List(
                "Commands",
                text,
                [.. commands.Take(MaxCommands).Select(c => c.Succeeded == false ? $"{c.Summary} (failed)" : c.Summary)],
                commands.Count > MaxCommands ? $"and {commands.Count - MaxCommands} more" : null);

            if (task.Procedure.Count > 0)
            {
                List("Required procedure", text, [.. task.Procedure.Select(s => s.IsDone ? $"{s.Text}: done ({Short(s.Evidence!)})" : $"{s.Text}: NOT done")]);
            }

            var permissions = log.Count(e => e.Kind == AuditKind.Permission && e.Summary.StartsWith("Asked", StringComparison.Ordinal));
            if (permissions > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Permissions asked: {permissions}");
            }

            List("Notes", text, [.. log.Where(e => e.Kind == AuditKind.Note && e.Detail is not null).Select(e => $"{e.Summary}: {Short(e.Detail!)}")]);
        }

        return text.ToString();
    }

    private static List<string> Distinct(IReadOnlyList<AuditEntry> log, AuditKind kind) =>
        [.. log.Where(e => e.Kind == kind).Select(e => e.Summary).Distinct(StringComparer.Ordinal)];

    private static void List(string title, StringBuilder text, List<string> items, string? more = null)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"- {title}:");
        items.ForEach(i => text.AppendLine(CultureInfo.InvariantCulture, $"  - {i}"));
        if (more is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  - {more}");
        }
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxNoteLength ? line : string.Concat(line.AsSpan(0, MaxNoteLength), "…");
    }
}
