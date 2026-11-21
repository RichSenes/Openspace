using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>Something that really happened to an inhabitant's work, as the audit log recorded it.</summary>
public enum LifeEventKind
{
    /// <summary>Its accepted work went into a branch.</summary>
    Merged,

    /// <summary>A person or the reviewer sent its work back.</summary>
    SentBack,

    /// <summary>It could not finish a task.</summary>
    Failed,

    /// <summary>Its accepted work no longer merged: the branch had changed under it.</summary>
    Conflict,
}

/// <param name="Desk">The desk whose task it was.</param>
public sealed record LifeEvent(DateTimeOffset At, LifeEventKind Kind, DeskId Desk, WorkTaskId Task, string Title);

/// <summary>Files a task changed, and when it last did.</summary>
public sealed record TaskFiles(WorkTask Task, IReadOnlySet<string> Files, DateTimeOffset First, DateTimeOffset Last);

/// <summary>
/// The facts of office life, read from the tasks and their audit log. Numbers about inhabitants
/// are computed from these only: nothing is drawn at random.
/// </summary>
public static class LifeEvents
{
    /// <summary>The events of the office's tasks, oldest first. Lines of tasks without a desk are left out.</summary>
    public static IReadOnlyList<LifeEvent> From(IEnumerable<WorkTask> tasks, IEnumerable<AuditEntry> log)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(log);
        var byId = tasks.Where(t => t.DeskId is not null).ToDictionary(t => t.Id);
        var events = new List<LifeEvent>();
        foreach (var entry in log.Where(e => e.Kind == AuditKind.Lifecycle))
        {
            if (!byId.TryGetValue(entry.TaskId, out var task) || KindOf(entry.Summary) is not { } kind)
            {
                continue;
            }

            events.Add(new LifeEvent(entry.At, kind, task.DeskId!.Value, task.Id, task.Title));
        }

        return [.. events.OrderBy(e => e.At)];
    }

    /// <summary>The kind of event a lifecycle line tells, or null when it tells none.</summary>
    public static LifeEventKind? KindOf(string summary) => summary switch
    {
        AuditTrail.SentBack => LifeEventKind.SentBack,
        AuditTrail.Failed => LifeEventKind.Failed,
        AuditTrail.CatchUp => LifeEventKind.Conflict,
        _ when summary.StartsWith(AuditTrail.MergedInto, StringComparison.Ordinal) => LifeEventKind.Merged,
        _ => null,
    };

    /// <summary>The files each task changed, as its "Changed x" and "Wrote x" lines name them.</summary>
    public static IReadOnlyList<TaskFiles> Files(IEnumerable<WorkTask> tasks, IEnumerable<AuditEntry> log)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(log);
        var changes = log.Where(e => e.Kind == AuditKind.Change && e.Succeeded != false).ToLookup(e => e.TaskId);
        var files = new List<TaskFiles>();
        foreach (var task in tasks.Where(t => t.DeskId is not null))
        {
            var lines = changes[task.Id].ToList();
            var names = lines.Select(e => FileOf(e.Summary)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            if (names.Count > 0)
            {
                files.Add(new TaskFiles(task, names, lines.Min(e => e.At), lines.Max(e => e.At)));
            }
        }

        return files;
    }

    /// <summary>"header.tsx" from "Changed header.tsx"; null for "Changed a file".</summary>
    public static string? FileOf(string summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        foreach (var verb in (string[])["Changed ", "Wrote "])
        {
            if (summary.StartsWith(verb, StringComparison.Ordinal))
            {
                var name = summary[verb.Length..].Trim();
                return name.Length == 0 || name == "a file" ? null : name;
            }
        }

        return null;
    }
}
