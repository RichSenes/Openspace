using Openspace.Application.Abstractions;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

public sealed class RoutineCommands(
    IOfficeRepository offices,
    IRoutineRepository routines,
    IEnumerable<IRoutineKind> kinds,
    RoutineRequests requests,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<RoutineId> CreateAsync(
        OfficeId officeId, string name, string kind, ScheduleView schedule, string? settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var known = kinds.FirstOrDefault(k => k.Key == kind?.Trim())
            ?? throw new DomainException($"Openspace knows no routine of kind '{kind}'.");
        var routine = Routine.Create(
            office, name, known.Key, schedule.ToSchedule(), string.IsNullOrWhiteSpace(settings) ? known.DefaultSettings : settings,
            clock.GetUtcNow(), clock.LocalTimeZone);
        routines.Add(routine);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return routine.Id;
    }

    /// <summary>Changes what is given; what is null stays as it is.</summary>
    public async Task UpdateAsync(
        RoutineId id, string? name, ScheduleView? schedule, string? settings, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (name is not null)
        {
            routine.Rename(name, now);
        }

        if (schedule is not null)
        {
            routine.Reschedule(schedule.ToSchedule(), now, clock.LocalTimeZone);
        }

        if (settings is not null)
        {
            routine.Configure(settings, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TurnOnAsync(RoutineId id, bool enabled, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        routine.TurnOn(enabled, clock.GetUtcNow(), clock.LocalTimeZone);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the routine with its runs, proposals and what it remembers.</summary>
    public async Task DeleteAsync(RoutineId id, CancellationToken cancellationToken)
    {
        routines.Remove(await routines.GetAsync(id, cancellationToken).ConfigureAwait(false));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs it as soon as the runner is free, whatever its schedule.</summary>
    public async Task RunNowAsync(RoutineId id, CancellationToken cancellationToken)
    {
        var routine = await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        requests.Ask(routine.Id);
    }
}
