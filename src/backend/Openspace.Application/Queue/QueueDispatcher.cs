using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Queue;

/// <summary>
/// Hands free seats to queued tasks, oldest first. A desk works on one task at a
/// time, so a task waits while its desk is busy even if other seats are free.
/// </summary>
/// <remarks>Not thread-safe: call it from a single place (the queue runner).</remarks>
public sealed class QueueDispatcher(
    IWorkTaskRepository tasks,
    IOfficeRepository offices,
    EngineLimits limits,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<QueueOptions> options,
    TimeProvider clock)
{
    /// <returns>The tasks that just started and now need an agent.</returns>
    public async Task<IReadOnlyList<WorkTaskId>> DispatchAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var freeSeats = options.CurrentValue.Seats - running.Count;
        if (freeSeats <= 0)
        {
            return [];
        }

        var busyDesks = running.Select(t => t.DeskId).OfType<DeskId>().ToHashSet();
        var queued = await tasks.ListAsync(null, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var started = new List<WorkTaskId>();
        var anyPaused = limits.Pauses().Count > 0;
        var officeCache = new Dictionary<OfficeId, Office>();

        foreach (var task in queued)
        {
            if (started.Count == freeSeats)
            {
                break;
            }

            if (task.DeskId is not { } desk || busyDesks.Contains(desk))
            {
                continue;
            }

            // Work that builds on other tasks waits until they are accepted and merged.
            if (!await ReadyAsync(task, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            // Desks on an account over its limits keep their tasks queued until it is back.
            if (anyPaused && limits.PausedUntil(await ScopeOfAsync(task, desk, officeCache, cancellationToken).ConfigureAwait(false)) is not null)
            {
                continue;
            }

            busyDesks.Add(desk);

            task.Start(now);
            started.Add(task.Id);
        }

        if (started.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return started;
    }

    private async Task<bool> ReadyAsync(WorkTask task, CancellationToken cancellationToken)
    {
        foreach (var id in task.DependsOn)
        {
            // A task that no longer exists holds nothing up.
            if (await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { IsDoneForOthers: false })
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string?> ScopeOfAsync(WorkTask task, DeskId desk, Dictionary<OfficeId, Office> cache, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(task.OfficeId, out var office))
        {
            office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
            cache[task.OfficeId] = office;
        }

        return office.HasDesk(desk) ? EngineLimits.ScopeOf(office.Locate(desk).Desk.Agent) : null;
    }
}
