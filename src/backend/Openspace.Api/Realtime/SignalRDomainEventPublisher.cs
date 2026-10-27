using Microsoft.AspNetCore.SignalR;
using Openspace.Application.Abstractions;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

namespace Openspace.Api.Realtime;

internal sealed class SignalRDomainEventPublisher(IHubContext<BuildingHub> hub, IServiceScopeFactory scopes) : IDomainEventPublisher
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
        var ended = events.OfType<WorkTaskStatusChanged>().Where(c => c.To is WorkTaskStatus.Review or WorkTaskStatus.Failed).ToList();
        foreach (var change in ended)
        {
            await CueAsync(change.TaskId, change.OfficeId, change.DeskId, change.To == WorkTaskStatus.Review ? "review" : "failed", cancellationToken).ConfigureAwait(false);
        }

        // Claude's verdict arrives a while after the work reached review.
        foreach (var note in events.OfType<WorkTaskNoteAdded>().Where(n => n.Note.Author.Kind == NoteAuthorKind.Reviewer))
        {
            await CueAsync(note.TaskId, note.OfficeId, note.DeskId, "verdict", cancellationToken).ConfigureAwait(false);
        }

        // An agent stopping may leave its office with nothing to do: worth knowing from afar.
        foreach (var office in ended.Where(c => c.From == WorkTaskStatus.Running).Select(c => c.OfficeId).OfType<OfficeId>().Distinct())
        {
            if (!await BusyAsync(office, cancellationToken).ConfigureAwait(false))
            {
                await CueAsync(null, office, null, "all-done", cancellationToken).ConfigureAwait(false);
            }
        }

        if (events.Any(e => e is Domain.Reports.ReportWritten))
        {
            await hub.Clients.All.SendAsync("ReportsChanged", cancellationToken).ConfigureAwait(false);
        }

        if (events.Any(e => e is EpicOpened))
        {
            await hub.Clients.All.SendAsync("EpicsChanged", cancellationToken).ConfigureAwait(false);
        }

        // Routines, their runs and their proposals: the Routines view of that office reloads.
        foreach (var office in events.Select(e => e switch
                 {
                     Domain.Routines.RoutineChanged x => x.OfficeId,
                     Domain.Routines.RoutineRunChanged x => x.OfficeId,
                     Domain.Routines.ProposalChanged x => (OfficeId?)x.OfficeId,
                     _ => null,
                 }).OfType<OfficeId>().Distinct())
        {
            await hub.Clients.All.SendAsync("RoutinesChanged", office.Value, cancellationToken).ConfigureAwait(false);
        }

        if (events.Any(e => e is RoleDefined or RoleChanged))
        {
            await hub.Clients.All.SendAsync("RolesChanged", cancellationToken).ConfigureAwait(false);
        }
    }

    private Task CueAsync(WorkTaskId? task, OfficeId? office, DeskId? desk, string cue, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("TaskCue", new { taskId = task?.Value, officeId = office?.Value, deskId = desk?.Value, cue }, cancellationToken);

    /// <summary>Whether an office still has tasks running or queued.</summary>
    private async Task<bool> BusyAsync(OfficeId office, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
        return (await tasks.ListAsync(office, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false)).Count > 0
            || (await tasks.ListAsync(office, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false)).Count > 0;
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
        OfficeReserveModelsSet x => x.OfficeId,
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
