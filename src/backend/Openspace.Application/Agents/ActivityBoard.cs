using System.Collections.Concurrent;
using Openspace.Application.Abstractions;

namespace Openspace.Application.Agents;

/// <summary>The latest activity of every desk, kept in memory and pushed on change.</summary>
public sealed class ActivityBoard(IActivityPublisher publisher)
{
    private readonly ConcurrentDictionary<Guid, DeskActivity> _byDesk = new();

    public IReadOnlyList<DeskActivity> Snapshot(Guid? officeId = null) =>
        [.. _byDesk.Values.Where(a => officeId is null || a.OfficeId == officeId).OrderBy(a => a.At)];

    public DeskActivity? ForTask(Guid taskId) => _byDesk.Values.FirstOrDefault(a => a.TaskId == taskId);

    /// <summary>The task left the desk (accepted or discarded): its bubble goes away.</summary>
    public async Task ClearTaskAsync(Guid taskId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (ForTask(taskId) is { } current)
        {
            await UpdateAsync(
                current with { Kind = ActivityKind.Stopped, Step = null, Doing = null, PermissionId = null, At = now },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Merges an update into the desk's current activity and publishes it if anything changed.</summary>
    public async Task UpdateAsync(DeskActivity update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var merged = _byDesk.TryGetValue(update.DeskId, out var current) && current.TaskId == update.TaskId
            ? update with
            {
                Step = update.Step ?? (update.Kind == ActivityKind.Working ? current.Step : null),
                Doing = update.Doing ?? (update.Kind == ActivityKind.Working ? current.Doing : null),
            }
            : update;

        if (current is not null && current with { At = merged.At } == merged)
        {
            return;
        }

        _byDesk[update.DeskId] = merged;
        await publisher.PublishAsync(merged, cancellationToken).ConfigureAwait(false);
    }
}
