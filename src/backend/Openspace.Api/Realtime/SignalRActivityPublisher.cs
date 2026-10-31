using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.Guests;

namespace Openspace.Api.Realtime;

/// <summary>Sends <c>DeskActivity(activity)</c> to browsers: the data behind speech bubbles.</summary>
internal sealed class SignalRActivityPublisher(IHubContext<BuildingHub> hub) : IActivityPublisher
{
    public Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("DeskActivity", activity, cancellationToken);

    public Task LogChangedAsync(Guid taskId, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("TaskLogChanged", taskId, cancellationToken);

    public Task ContextChangedAsync(DeskContext context, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("DeskContext", context, cancellationToken);

    public Task InternsChangedAsync(DeskInterns interns, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("DeskInterns", interns, cancellationToken);

    public Task GuestsChangedAsync(OfficeGuests guests, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("OfficeGuests", guests, cancellationToken);
}
