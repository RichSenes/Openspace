using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.Guests;

namespace Openspace.Infrastructure.Persistence;

/// <summary>Default when nobody watches desk activity, e.g. tools and tests.</summary>
internal sealed class NullActivityPublisher : IActivityPublisher
{
    public Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task LogChangedAsync(Guid taskId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ContextChangedAsync(DeskContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InternsChangedAsync(DeskInterns interns, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task GuestsChangedAsync(OfficeGuests guests, CancellationToken cancellationToken) => Task.CompletedTask;
}
