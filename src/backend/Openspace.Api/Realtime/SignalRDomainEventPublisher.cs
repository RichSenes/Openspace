using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Abstractions;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

namespace Openspace.Api.Realtime;

internal sealed class SignalRDomainEventPublisher(IHubContext<BuildingHub> hub) : IDomainEventPublisher
{
    public async Task PublishAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        var offices = events.Select(OfficeOf).OfType<OfficeId>().Distinct();
        var tasks = events.Select(TaskOf).OfType<WorkTaskId>().Distinct();

        foreach (var office in offices)
        {
            await hub.Clients.All.SendAsync("OfficeChanged", office.Value, cancellationToken).ConfigureAwait(false);
        }

        foreach (var task in tasks)
        {
            await hub.Clients.All.SendAsync("TaskChanged", task.Value, cancellationToken).ConfigureAwait(false);
        }

        // Moments worth a sound: work ready for review, or a task that failed.
        foreach (var change in events.OfType<WorkTaskStatusChanged>().Where(c => c.To is WorkTaskStatus.Review or WorkTaskStatus.Failed))
        {
            var cue = new { taskId = change.TaskId.Value, officeId = change.OfficeId?.Value, deskId = change.DeskId?.Value, cue = change.To == WorkTaskStatus.Review ? "review" : "failed" };
            await hub.Clients.All.SendAsync("TaskCue", cue, cancellationToken).ConfigureAwait(false);
        }

        if (events.Any(e => e is Domain.Reports.ReportWritten))
        {
            await hub.Clients.All.SendAsync("ReportsChanged", cancellationToken).ConfigureAwait(false);
        }

        if (events.Any(e => e is EpicOpened))
        {
            await hub.Clients.All.SendAsync("EpicsChanged", cancellationToken).ConfigureAwait(false);
        }

        if (events.Any(e => e is RoleDefined or RoleChanged))
        {
            await hub.Clients.All.SendAsync("RolesChanged", cancellationToken).ConfigureAwait(false);
        }
    }

    private static OfficeId? OfficeOf(IDomainEvent e) => e switch
    {
        OfficeOpened x => x.OfficeId,
        OfficeRenamed x => x.OfficeId,
        OfficeRepositorySet x => x.OfficeId,
        OfficeThemeChanged x => x.OfficeId,
        RoomOpened x => x.OfficeId,
        RoomContextUpdated x => x.OfficeId,
        RoomMoodChanged x => x.OfficeId,
        RoomCheckSet x => x.OfficeId,
        OfficePrivatePathsSet x => x.OfficeId,
        OfficeLocalOnlySet x => x.OfficeId,
        RoomProcedureSet x => x.OfficeId,
        RoomArranged x => x.OfficeId,
        FloorPlanned x => x.OfficeId,
        DeskAdded x => x.OfficeId,
        DeskReassigned x => x.OfficeId,
        DeskRoleChanged x => x.OfficeId,
        DeskRenamed x => x.OfficeId,
        DeskDressed x => x.OfficeId,
        _ => null,
    };

    private static WorkTaskId? TaskOf(IDomainEvent e) => e switch
    {
        WorkTaskCreated x => x.TaskId,
        WorkTaskStatusChanged x => x.TaskId,
        WorkTaskNoteAdded x => x.TaskId,
        WorkTaskMerged x => x.TaskId,
        WorkTaskDependenciesSet x => x.TaskId,
        WorkTaskPullRequestOpened x => x.TaskId,
        WorkTaskStepCompleted x => x.TaskId,
        WorkTaskCheckListed x => x.TaskId,
        WorkTaskCheckTicked x => x.TaskId,
        WorkTaskTokensSpent x => x.TaskId,
        _ => null,
    };
}
