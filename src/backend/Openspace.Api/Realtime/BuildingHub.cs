using Microsoft.AspNetCore.SignalR;

namespace Openspace.Api.Realtime;

/// <summary>
/// Pushes building changes to browsers. Server to client only:
/// <c>OfficeChanged(officeId)</c> and <c>TaskChanged(taskId)</c>.
/// Clients refetch what they show; messages carry ids, not data.
/// </summary>
public sealed class BuildingHub : Hub
{
    public const string Path = "/hubs/building";
}
