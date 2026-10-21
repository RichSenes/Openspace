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
    IRoleRepository roles,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    IWorkspaceManager workspaces,
    ActivityBoard activity,
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
            var problem = await CheckAsync(office, desk, cancellationToken).ConfigureAwait(false);
            if (problem is not null)
            {
                await FailAsync(task, problem, cancellationToken).ConfigureAwait(false);
                return;
            }

            var setup = await SetupAsync(desk, cancellationToken).ConfigureAwait(false);
            string message;
            if (task.Run is null)
            {
                var workspace = await workspaces
                    .PrepareAsync(office.RepositoryPath!, task.Id.Value.ToString("N")[..8], task.Title, cancellationToken)
                    .ConfigureAwait(false);
                var sessionId = await runtime
                    .CreateSessionAsync(workspace.Path, task.Title, setup, cancellationToken)
                    .ConfigureAwait(false);

                // Saved before prompting, so the agent's first events can find their task.
                task.AttachRun(AgentRun.Create(sessionId, workspace.Path, workspace.Branch), clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                message = Briefing.ForNewTask(office, room, desk, task);
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLaunchFailed(logger, ex, task.Id.Value);
            await FailAsync(task, $"The agent could not start: {ex.Message}", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The desk's agent and model, completed by its role: the desk's own model wins over the role's.</summary>
    private async Task<AgentSetup> SetupAsync(Desk desk, CancellationToken cancellationToken)
    {
        var role = desk.RoleId is { } id ? await roles.FindAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return new AgentSetup(
            desk.Agent.AgentName,
            desk.Agent.Model ?? role?.Model,
            string.IsNullOrEmpty(role?.Instructions) ? null : role.Instructions,
            role?.Permissions);
    }

    private async Task<string?> CheckAsync(Office office, Desk desk, CancellationToken cancellationToken)
    {
        if (office.RepositoryPath is null)
        {
            return $"Office '{office.Name}' has no repository yet. Set its folder in Rooms, then try again.";
        }

        if (await workspaces.CheckRepositoryAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return $"{problem.Message} Fix the repository in Rooms, then try again.";
        }

        return await runtime.HasAgentAsync(desk.Agent.AgentName, office.RepositoryPath, cancellationToken).ConfigureAwait(false)
            ? null
            : $"OpenCode has no agent named '{desk.Agent.AgentName}'. Create it in OpenCode or give the desk another agent, then try again.";
    }

    private async Task FailAsync(WorkTask task, string reason, CancellationToken cancellationToken)
    {
        task.Fail(reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Openspace tools are not available to the agent of task {TaskId}")]
    private static partial void LogToolsUnavailable(ILogger logger, Exception exception, Guid taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not launch an agent for task {TaskId}")]
    private static partial void LogLaunchFailed(ILogger logger, Exception exception, Guid taskId);
}
