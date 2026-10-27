using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Routines;
using Openspace.Domain.Routines;

namespace Openspace.Api.Realtime;

/// <summary>Tells the building what a running routine is doing; an empty line means it stopped.</summary>
internal sealed class SignalRRoutineProgress(IHubContext<BuildingHub> hub) : IRoutineProgress
{
    public void Report(Routine routine, string line) =>
        // Nobody waits for the bubbles: a lost one is replaced by the next line.
        _ = hub.Clients.All.SendAsync("RoutineProgress", new { officeId = routine.OfficeId.Value, routineId = routine.Id.Value, line });
}
