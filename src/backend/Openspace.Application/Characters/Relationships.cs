using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>What happened between two inhabitants.</summary>
public enum TieKind
{
    /// <summary>One left notes on the other's work.</summary>
    Reviewed,

    /// <summary>Both changed the same files within a few days.</summary>
    SameFiles,

    /// <summary>One's work went in on files where the other's had failed.</summary>
    FixedFailure,

    /// <summary>One's work stopped merging after the other's went in on the same files.</summary>
    MergeConflict,

    /// <summary>They clashed on planned work and the rules settled who goes first.</summary>
    Agreed,

    /// <summary>They clashed on planned work and had to ask the person.</summary>
    Disagreed,
}

/// <param name="Text">What happened, naming both desks and the work.</param>
public sealed record TieEvent(DateTimeOffset At, TieKind Kind, int Change, string Text);

/// <param name="First">The desk of the pair whose id comes first, so each pair is listed once.</param>
/// <param name="Affinity">The sum of the changes, from -100 to 100.</param>
/// <param name="History">The events that changed it, oldest first.</param>
public sealed record Relationship(DeskId First, DeskId Second, int Affinity, IReadOnlyList<TieEvent> History);

/// <summary>
/// Relationships between inhabitants, from real events only: reviews, shared files, a failure
/// one of them made good, a merge conflict. Only between desks, never with the person, and
/// nothing an agent does or delivers depends on them.
/// </summary>
public static class Relationships
{
    public const int Min = -100;
    public const int Max = 100;

    /// <summary>How far back relationships remember.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>Two tasks on the same files this close count as working together.</summary>
    public static readonly TimeSpan Together = TimeSpan.FromDays(3);

    /// <summary>Work that goes in on failed files this soon after counts as making good the failure.</summary>
    public static readonly TimeSpan FixWithin = TimeSpan.FromDays(14);

    public const int ReviewChange = 2;
    public const int SameFilesChange = 1;
    public const int FixChange = 3;
    public const int ConflictChange = -2;
    public const int AgreedChange = 1;
    public const int DisagreedChange = -1;

    /// <param name="names">The desks' names, for the history's text; desks not in it are left out.</param>
    /// <param name="agreements">Agreements between desks: settled by the rules brings them closer, asking the person a little apart.</param>
    public static IReadOnlyList<Relationship> Of(
        IReadOnlyList<WorkTask> tasks, IReadOnlyList<AuditEntry> log, IReadOnlyDictionary<DeskId, string> names, DateTimeOffset now,
        IReadOnlyList<Agreement>? agreements = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(names);
        var since = now - Window;
        var known = tasks.Where(t => t.DeskId is { } d && names.ContainsKey(d)).ToList();
        var events = LifeEvents.From(known, log);
        var files = LifeEvents.Files(known, log);
        var ties = new List<(DeskId A, DeskId B, TieEvent Event)>();

        string Name(DeskId desk) => names[desk];

        // Notes one desk left on another's work.
        foreach (var task in known)
        {
            foreach (var note in task.Notes.Where(n => n.Author.Kind == NoteAuthorKind.Desk && n.Author.DeskId is { } by && by != task.DeskId && names.ContainsKey(by) && n.WrittenAt >= since))
            {
                var by = note.Author.DeskId!.Value;
                ties.Add((by, task.DeskId!.Value, new TieEvent(note.WrittenAt, TieKind.Reviewed, ReviewChange, $"{Name(by)} left notes on {Name(task.DeskId!.Value)}'s “{task.Title}”")));
            }
        }

        // Two desks on the same files within a few days.
        for (var i = 0; i < files.Count; i++)
        {
            for (var j = i + 1; j < files.Count; j++)
            {
                var (a, b) = (files[i], files[j]);
                if (a.Task.DeskId == b.Task.DeskId || a.First - b.Last > Together || b.First - a.Last > Together)
                {
                    continue;
                }

                var shared = a.Files.Where(b.Files.Contains).Order(StringComparer.Ordinal).ToList();
                if (shared.Count > 0)
                {
                    var at = a.Last > b.Last ? a.Last : b.Last;
                    ties.Add((a.Task.DeskId!.Value, b.Task.DeskId!.Value, new TieEvent(at, TieKind.SameFiles, SameFilesChange,
                        $"{Name(a.Task.DeskId!.Value)} and {Name(b.Task.DeskId!.Value)} both worked on {Files(shared)}")));
                }
            }
        }

        var filesOf = files.ToDictionary(f => f.Task.Id, f => f.Files);
        IReadOnlySet<string> FilesOf(WorkTaskId id) => filesOf.TryGetValue(id, out var set) ? set : new HashSet<string>();

        foreach (var failure in events.Where(e => e.Kind == LifeEventKind.Failed))
        {
            // The first work of another desk that went in on the failed task's files.
            var fix = events.FirstOrDefault(e => e.Kind == LifeEventKind.Merged && e.Desk != failure.Desk && e.At > failure.At && e.At - failure.At <= FixWithin
                && FilesOf(e.Task).Overlaps(FilesOf(failure.Task)));
            if (fix is not null)
            {
                ties.Add((fix.Desk, failure.Desk, new TieEvent(fix.At, TieKind.FixedFailure, FixChange,
                    $"{Name(fix.Desk)}'s “{fix.Title}” went in where {Name(failure.Desk)}'s “{failure.Title}” had failed")));
            }
        }

        foreach (var conflict in events.Where(e => e.Kind == LifeEventKind.Conflict))
        {
            // The last work of another desk merged before it, on the same files.
            var cause = events.LastOrDefault(e => e.Kind == LifeEventKind.Merged && e.Desk != conflict.Desk && e.At <= conflict.At
                && FilesOf(e.Task).Overlaps(FilesOf(conflict.Task)));
            if (cause is not null)
            {
                ties.Add((conflict.Desk, cause.Desk, new TieEvent(conflict.At, TieKind.MergeConflict, ConflictChange,
                    $"{Name(conflict.Desk)}'s “{conflict.Title}” no longer merged after {Name(cause.Desk)}'s “{cause.Title}” went in")));
            }
        }

        foreach (var a in agreements ?? [])
        {
            if (!names.ContainsKey(a.FirstDesk) || !names.ContainsKey(a.SecondDesk))
            {
                continue;
            }

            var pair = $"{Name(a.FirstDesk)} and {Name(a.SecondDesk)}";
            ties.Add(a.Status == AgreementStatus.Settled
                ? (a.FirstDesk, a.SecondDesk, new TieEvent(a.ClosedAt ?? a.OpenedAt, TieKind.Agreed, AgreedChange, $"{pair} agreed who goes first on {a.Subject}"))
                : (a.FirstDesk, a.SecondDesk, new TieEvent(a.OpenedAt, TieKind.Disagreed, DisagreedChange, $"{pair} could not agree who goes first on {a.Subject} and asked the person")));
        }

        return [.. ties
            .Where(t => t.Event.At >= since && t.Event.At <= now)
            .GroupBy(t => t.A.Value.CompareTo(t.B.Value) < 0 ? (t.A, t.B) : (t.B, t.A))
            .Select(g =>
            {
                var history = g.Select(t => t.Event).OrderBy(e => e.At).ToList();
                return new Relationship(g.Key.Item1, g.Key.Item2, Math.Clamp(history.Sum(e => e.Change), Min, Max), history);
            })
            .OrderByDescending(r => Math.Abs(r.Affinity))
            .ThenBy(r => r.First.Value)];
    }

    private static string Files(List<string> shared) =>
        shared.Count <= 2 ? string.Join(" and ", shared) : $"{shared[0]}, {shared[1]} and {shared.Count - 2} more";
}
