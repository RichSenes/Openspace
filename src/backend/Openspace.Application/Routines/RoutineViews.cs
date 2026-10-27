using System.Globalization;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <param name="Days">Days of the week it runs on; empty means every day.</param>
/// <param name="At">Time of day on the person's clock, "HH:mm".</param>
/// <param name="Since">The week counting starts from, for schedules every few weeks.</param>
public sealed record ScheduleView(IReadOnlyList<DayOfWeek> Days, string At, int EveryWeeks, DateOnly? Since)
{
    public static ScheduleView Of(RoutineSchedule schedule) => new(
        schedule.Days, schedule.At.ToString("HH:mm", CultureInfo.InvariantCulture), schedule.EveryWeeks,
        schedule.Since == DateOnly.MinValue ? null : schedule.Since);

    public RoutineSchedule ToSchedule()
    {
        if (!TimeOnly.TryParseExact(At, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            throw new Domain.Common.DomainException($"'{At}' is not a time such as 07:30.");
        }

        return RoutineSchedule.Weekly(Days ?? [], at, EveryWeeks < 1 ? 1 : EveryWeeks, Since);
    }
}

public sealed record RoutineRunView(
    Guid Id, Guid RoutineId, RoutineRunStatus Status, bool ByHand, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? Summary, string Log)
{
    public static RoutineRunView Of(RoutineRun run) =>
        new(run.Id.Value, run.RoutineId.Value, run.Status, run.ByHand, run.StartedAt, run.FinishedAt, run.Summary, run.Log);
}

/// <param name="KindName">The kind's name for people; the key itself when Openspace no longer knows it.</param>
/// <param name="LastRun">The latest run, without its log.</param>
public sealed record RoutineView(
    Guid Id, Guid OfficeId, string Name, string Kind, string KindName, ScheduleView Schedule, string Settings, bool Enabled, bool AskFirst,
    DateTimeOffset? LastRunAt, DateTimeOffset? NextRunAt, RoutineRunView? LastRun);

public sealed record ProposalView(
    Guid Id, Guid RoutineId, string RoutineName, string Action, string Title, string Body, ProposalStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? Outcome);

/// <param name="DefaultSettings">The settings a new routine of this kind starts with.</param>
public sealed record RoutineKindView(string Key, string Name, string Description, string DefaultSettings);
