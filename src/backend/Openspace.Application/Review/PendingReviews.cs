using System.Collections.Concurrent;
using System.Threading.Channels;
using Openspace.Domain.Work;

namespace Openspace.Application.Review;

/// <summary>
/// Tasks that just reached review, waiting for the reviewer to read them, one at a time; and
/// reviews put off while the reviewer's account is over its limits, until they are due again.
/// </summary>
public sealed class PendingReviews
{
    private readonly Channel<WorkTaskId> _waiting = Channel.CreateUnbounded<WorkTaskId>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<WorkTaskId, DateTimeOffset> _later = new();

    public void Enqueue(WorkTaskId taskId) => _waiting.Writer.TryWrite(taskId);

    public IAsyncEnumerable<WorkTaskId> ReadAllAsync(CancellationToken cancellationToken) => _waiting.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Tries the review again from <paramref name="notBefore"/> on.</summary>
    public void Defer(WorkTaskId taskId, DateTimeOffset notBefore) => _later[taskId] = notBefore;

    public bool IsDeferred(WorkTaskId taskId) => _later.ContainsKey(taskId);

    /// <summary>Puts the reviews that are due back in line; returns how many.</summary>
    public int EnqueueDue(DateTimeOffset now)
    {
        var due = _later.Where(r => r.Value <= now).Select(r => r.Key).ToList();
        foreach (var taskId in due)
        {
            if (_later.TryRemove(taskId, out _))
            {
                Enqueue(taskId);
            }
        }

        return due.Count;
    }
}
