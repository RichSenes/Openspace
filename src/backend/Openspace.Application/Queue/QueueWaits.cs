using Openspace.Application.Abstractions;

namespace Openspace.Application.Queue;

/// <summary>Why a queued task has not started yet, in a sentence for the board.</summary>
public sealed record QueueWait(Guid TaskId, string Reason);

/// <summary>
/// Why each queued task waits, as the queue saw it on its last round: its desk is busy, the
/// work it builds on is not merged, its account is over its limits, the graphics card is
/// taken. Kept in memory and pushed when it changes; a task that starts drops out.
/// </summary>
public sealed class QueueWaits(IActivityPublisher publisher)
{
    private readonly Lock _gate = new();
    private Dictionary<Guid, string> _reasons = [];

    public IReadOnlyList<QueueWait> Snapshot()
    {
        lock (_gate)
        {
            return [.. _reasons.Select(r => new QueueWait(r.Key, r.Value))];
        }
    }

    /// <summary>Takes the reasons of a whole round, and tells watchers only when something changed.</summary>
    public async Task ReplaceAsync(IReadOnlyDictionary<Guid, string> reasons, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        IReadOnlyList<QueueWait> now;
        lock (_gate)
        {
            if (reasons.Count == _reasons.Count && reasons.All(r => _reasons.TryGetValue(r.Key, out var was) && was == r.Value))
            {
                return;
            }

            _reasons = new Dictionary<Guid, string>(reasons);
            now = [.. _reasons.Select(r => new QueueWait(r.Key, r.Value))];
        }

        await publisher.QueueWaitsChangedAsync(now, cancellationToken).ConfigureAwait(false);
    }
}
