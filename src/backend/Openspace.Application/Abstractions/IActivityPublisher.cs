using Openspace.Application.Agents;
using Openspace.Application.Guests;

namespace Openspace.Application.Abstractions;

/// <summary>Pushes what desks are doing right now to whoever is watching.</summary>
public interface IActivityPublisher
{
    Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken);

    /// <summary>New lines were added to a task's audit log.</summary>
    Task LogChangedAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>How full a desk's conversation is changed: the agent's energy.</summary>
    Task ContextChangedAsync(DeskContext context, CancellationToken cancellationToken);

    /// <summary>An intern arrived at a desk or left it.</summary>
    Task InternsChangedAsync(DeskInterns interns, CancellationToken cancellationToken);

    /// <summary>A guest came into an office, changed what it does, or left.</summary>
    Task GuestsChangedAsync(OfficeGuests guests, CancellationToken cancellationToken);
}
