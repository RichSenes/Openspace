using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;

namespace Openspace.Api.Realtime;

/// <summary>Sends <c>DeskActivity(activity)</c> to browsers: the data behind speech bubbles.</summary>
internal sealed class SignalRActivityPublisher(IHubContext<BuildingHub> hub) : IActivityPublisher
{
    public Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("DeskActivity", activity, cancellationToken);
}
