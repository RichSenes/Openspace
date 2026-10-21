using Microsoft.AspNetCore.SignalR;

namespace Openspace.Api.Realtime;

/// <summary>
/// Pushes building changes to browsers. Server to client only:
/// <c>OfficeChanged(officeId)</c> and <c>TaskChanged(taskId)</c> carry ids and clients
/// refetch; <c>DeskActivity(activity)</c> carries the activity itself, as it is not stored;
/// <c>TaskLogChanged(taskId)</c> says a task's audit log grew; <c>RolesChanged()</c> says
/// a role was defined or changed (deleting one is told by the HTTP answer); <c>EpicsChanged()</c>
/// says the supervisor opened an epic.
/// </summary>
public sealed class BuildingHub : Hub
{
    public const string Path = "/hubs/building";
}
