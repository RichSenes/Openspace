using System.Text;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Routines;

public readonly record struct RoutineRunId(Guid Value)
{
    public static RoutineRunId New() => new(Guid.CreateVersion7());
}

public enum RoutineRunStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>One time a routine ran: what it did, step by step, and how it ended.</summary>
public sealed class RoutineRun : AggregateRoot<RoutineRunId>
{
    public const int MaxLogLength = 40_000;

    private readonly StringBuilder _log = new();

    private RoutineRun(RoutineRunId id, RoutineId routineId, OfficeId officeId, bool byHand, DateTimeOffset startedAt)
        : base(id)
    {
        RoutineId = routineId;
        OfficeId = officeId;
        ByHand = byHand;
        StartedAt = startedAt;
    }

    public RoutineId RoutineId { get; }

    public OfficeId OfficeId { get; }

    /// <summary>Started by a person rather than by the schedule.</summary>
    public bool ByHand { get; }

    public RoutineRunStatus Status { get; private set; } = RoutineRunStatus.Running;

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>A line for people: what came out of the run, or why it failed.</summary>
    public string? Summary { get; private set; }

    /// <summary>What the run did, a line per step with its time.</summary>
    public string Log
    {
        get => _log.ToString();
        private set => _log.Clear().Append(value);
    }

    public static RoutineRun Start(Routine routine, bool byHand, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(routine);
        routine.Started(now, zone);
        var run = new RoutineRun(RoutineRunId.New(), routine.Id, routine.OfficeId, byHand, now);
        run.Raise(new RoutineRunChanged(run.Id, routine.Id, routine.OfficeId, now));
        return run;
    }

    public void Write(string line, DateTimeOffset now)
    {
        if (_log.Length >= MaxLogLength || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _log.Append(System.Globalization.CultureInfo.InvariantCulture, $"{now:HH:mm:ss} {line.Trim()}\n");
        if (_log.Length >= MaxLogLength)
        {
            _log.Length = MaxLogLength;
            _log.Append("\n[The log stops here.]\n");
        }
    }

    public void Succeed(string summary, DateTimeOffset now) => End(RoutineRunStatus.Succeeded, summary, now);

    public void Fail(string reason, DateTimeOffset now) => End(RoutineRunStatus.Failed, reason, now);

    private void End(RoutineRunStatus status, string summary, DateTimeOffset now)
    {
        if (Status != RoutineRunStatus.Running)
        {
            throw new DomainException("This run has already ended.");
        }

        Status = status;
        Summary = Guard.NotBlank(summary, "Run summary", 2000);
        FinishedAt = now;
        Raise(new RoutineRunChanged(Id, RoutineId, OfficeId, now));
    }
}

public sealed record RoutineRunChanged(RoutineRunId RunId, RoutineId RoutineId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;
