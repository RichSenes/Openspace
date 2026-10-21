using Openspace.Domain.Common;

namespace Openspace.Application.Abstractions;

/// <summary>Receives domain events once the changes that raised them are saved.</summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}
