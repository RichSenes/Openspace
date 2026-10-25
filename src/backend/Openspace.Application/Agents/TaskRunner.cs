using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Application.Queue;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>Turns started tasks into working agents: workspace, session, briefing.</summary>
public sealed partial class TaskRunner(
    QueueDispatcher dispatcher,
    IOfficeRepository offices,
    AgentSetups setups,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    IWorkspaceManager workspaces,
    ActivityBoard activity,
    AgentSignals signals,
    TimeProvider clock,
    ILogger<TaskRunner> logger)
{
    /// <summary>Fills free seats and launches an agent for each task that got one.</summary>
    public async Task RunQueueAsync(CancellationToken cancellationToken)
    {
        foreach (var taskId in await dispatcher.DispatchAsync(cancellationToken).ConfigureAwait(false))
        {
            await LaunchAsync(taskId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After a restart nobody is following the running tasks: put them back in the queue,
    /// where they resume in their own session.
    /// </summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        foreach (var task in running)
        {
            task.AddNote(NoteAuthor.System, "Openspace restarted while this task was running: it went back to the queue.", now);
            task.Requeue(now);
        }

        if (running.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LaunchAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var (room, desk) = office.Locate(task.DeskId!.Value);

        try
        {
            var problem = await CheckAsync(office, AgentSetups.AgentFor(desk, task), cancellationToken).ConfigureAwait(false);
            if (problem is not null)
            {
                await FailAsync(task, problem, cancellationToken).ConfigureAwait(false);
                return;
            }

            var setup = await setups.ForTaskAsync(desk, task, cancellationToken, office.PrivatePaths).ConfigureAwait(false);
            string message;
            if (task.Run is null)
            {
                var workspace = await workspaces
                    .PrepareAsync(office.RepositoryPath!, task.Key, task.Title, cancellationToken)
                    .ConfigureAwait(false);
                var sessionId = await runtime
                    .CreateSessionAsync(workspace.Path, task.Title, setup, cancellationToken)
                    .ConfigureAwait(false);

                // Saved before prompting, so the agent's first events can find their task.
                task.AdoptProcedure(room.Procedure);
                task.AttachRun(AgentRun.Create(sessionId, workspace.Path, workspace.Branch), clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                message = Briefing.ForNewTask(office, room, desk, task, await BeforeAsync(task, cancellationToken).ConfigureAwait(false));
            }
            else if (runtime.EngineOf(task.Run.SessionId) != setup.Engine)
            {
                // The desk changed engine since: its new agent cannot pick up the old
                // session, so it opens its own in the same working copy and reads it all.
                var sessionId = await runtime
                    .CreateSessionAsync(task.Run.WorkspacePath, task.Title, setup, cancellationToken)
                    .ConfigureAwait(false);
                task.AttachRun(AgentRun.Create(sessionId, task.Run.WorkspacePath, task.Run.Branch), clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                message = Briefing.ForTakeOver(office, room, desk, task, await BeforeAsync(task, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                message = Briefing.ForResume(task);
            }

            try
            {
                await runtime.ConnectToolsAsync(task.Run!.WorkspacePath, task.Id.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The tools help but are not required: the agent still works without them.
                LogToolsUnavailable(logger, ex, task.Id.Value);
            }

            await activity.UpdateAsync(
                new DeskActivity(office.Id.Value, desk.Id.Value, task.Id.Value, ActivityKind.Working, null, "Reading the brief", null, clock.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
            await runtime
                .PromptAsync(task.Run!.SessionId, task.Run.WorkspacePath, setup, message, cancellationToken)
                .ConfigureAwait(false);
            await RestorePendingPermissionAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLaunchFailed(logger, ex, task.Id.Value);
            await FailAsync(task, $"The agent could not start: {ex.Message}", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A resumed session may still be blocked on permissions asked before Openspace
    /// restarted; OpenCode will not ask again, so they are picked up from it: answered on
    /// Openspace's own when it may, the first one left shown to a person.
    /// </summary>
    private async Task RestorePendingPermissionAsync(WorkTask task, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await runtime.PendingPermissionsAsync(task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
            foreach (var permission in pending.Where(p => p.SessionId == task.Run.SessionId))
            {
                await signals.PermissionAskedAsync(task.Run.SessionId, permission.Id, permission.Action, cancellationToken).ConfigureAwait(false);
                if (activity.ForTask(task.Id.Value) is { Kind: ActivityKind.Waiting })
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPendingUnknown(logger, ex, task.Id.Value);
        }
    }

    private async Task<string?> CheckAsync(Office office, AgentProfile agent, CancellationToken cancellationToken)
    {
        if (office.RepositoryPath is null)
        {
            return $"Office '{office.Name}' has no repository yet. Set its folder in Rooms, then try again.";
        }

        if (await workspaces.CheckRepositoryAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return $"{problem.Message} Fix the repository in Rooms, then try again.";
        }

        if (await runtime.HasAgentAsync(agent, office.RepositoryPath, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return agent.Engine == AgentEngine.ClaudeCode
            ? "Claude Code is not available on this machine. Install it and sign in, or give the desk OpenCode, then try again."
            : $"OpenCode has no agent named '{agent.AgentName}'. Create it in OpenCode or give the desk another agent, then try again.";
    }

    private async Task FailAsync(WorkTask task, string reason, CancellationToken cancellationToken)
    {
        task.Fail(reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check pending permissions of task {TaskId}")]
    private static partial void LogPendingUnknown(ILogger logger, Exception exception, Guid taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Openspace tools are not available to the agent of task {TaskId}")]
    private static partial void LogToolsUnavailable(ILogger logger, Exception exception, Guid taskId);

    /// <summary>The tasks this one builds on, for its brief.</summary>
    private async Task<List<WorkTask>> BeforeAsync(WorkTask task, CancellationToken cancellationToken)
    {
        var before = new List<WorkTask>();
        foreach (var id in task.DependsOn)
        {
            if (await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } done)
            {
                before.Add(done);
            }
        }

        return before;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not launch an agent for task {TaskId}")]
    private static partial void LogLaunchFailed(ILogger logger, Exception exception, Guid taskId);
}
