using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
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
    IUnitOfWork unitOfWork,
    IOptions<QueueOptions> options,
    TimeProvider clock)
{
    /// <returns>The tasks that just started and now need an agent.</returns>
    public async Task<IReadOnlyList<WorkTaskId>> DispatchAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var freeSeats = options.Value.Seats - running.Count;
        if (freeSeats <= 0)
        {
            return [];
        }

        var busyDesks = running.Select(t => t.DeskId).OfType<DeskId>().ToHashSet();
        var queued = await tasks.ListAsync(null, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var started = new List<WorkTaskId>();

        foreach (var task in queued)
        {
            if (started.Count == freeSeats)
            {
                break;
            }

            if (task.DeskId is not { } desk || !busyDesks.Add(desk))
            {
                continue;
            }

            task.Start(now);
            started.Add(task.Id);
        }

        if (started.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return started;
    }
}
