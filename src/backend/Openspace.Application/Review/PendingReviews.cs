using System.Threading.Channels;
using Openspace.Domain.Work;

namespace Openspace.Application.Review;

/// <summary>Tasks that just reached review, waiting for the reviewer to read them, one at a time.</summary>
public sealed class PendingReviews
{
    private readonly Channel<WorkTaskId> _waiting = Channel.CreateUnbounded<WorkTaskId>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(WorkTaskId taskId) => _waiting.Writer.TryWrite(taskId);

    public IAsyncEnumerable<WorkTaskId> ReadAllAsync(CancellationToken cancellationToken) => _waiting.Reader.ReadAllAsync(cancellationToken);
}
