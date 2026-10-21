using Openspace.Application.Abstractions;
using Openspace.Domain.Common;

namespace Openspace.Infrastructure.Persistence;

/// <summary>Default publisher when nobody listens, e.g. design-time tooling.</summary>
internal sealed class NullDomainEventPublisher : IDomainEventPublisher
{
    public static NullDomainEventPublisher Instance { get; } = new();

    public Task PublishAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
