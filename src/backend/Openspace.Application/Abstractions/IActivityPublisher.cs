using Openspace.Application.Agents;

namespace Openspace.Application.Abstractions;

/// <summary>Pushes what desks are doing right now to whoever is watching.</summary>
public interface IActivityPublisher
{
    Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken);

    /// <summary>New lines were added to a task's audit log.</summary>
    Task LogChangedAsync(Guid taskId, CancellationToken cancellationToken);
}
