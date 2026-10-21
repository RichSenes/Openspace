using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>
/// Reacts to what agents report while they work. Signals for sessions Openspace
/// did not start (e.g. someone using OpenCode directly) are ignored.
/// </summary>
public sealed class AgentSignals(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    ActivityBoard activity,
    IAuditLog audit,
    IActivityPublisher publisher,
    TimeProvider clock)
{
    /// <param name="step">The agent's current todo item, if this update is about its plan.</param>
    /// <param name="doing">The concrete action, if this update is about a tool.</param>
    public async Task ProgressAsync(string sessionId, string? step, string? doing, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        await ShowProgressAsync(task, step, doing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent said, through the Openspace tools, what it is doing.</summary>
    public async Task AnnounceAsync(WorkTaskId taskId, string text, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var step = Guard(text, "Announcement", 200);
        await ShowProgressAsync(task, step, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent left a note on its task, through the Openspace tools.</summary>
    public async Task NoteAsync(WorkTaskId taskId, string text, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        task.AddNote(NoteAuthor.FromDesk(task.DeskId!.Value), Guard(text, "Note", 4000), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The brief again, with the notes written since: what the agent reads to get its bearings.</summary>
    public async Task<string> DescribeAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var (room, desk) = office.Locate(task.DeskId!.Value);
        return Briefing.ForNewTask(office, room, desk, task);
    }

    /// <summary>A tool call ended: it goes in the task's log, once, with its evidence.</summary>
    public async Task ToolFinishedAsync(
        string sessionId,
        string callId,
        AuditKind kind,
        string summary,
        string? detail,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null || await audit.ContainsAsync(task.Id, callId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Paths inside the private working copy read better relative to the repository.
        var workspace = task.Run!.WorkspacePath.TrimEnd('/') + "/";
        var relative = detail?.Replace(workspace, string.Empty, StringComparison.Ordinal);
        await LogAsync(
            AuditEntry.Record(task.Id, clock.GetUtcNow(), kind, summary, relative, succeeded, callId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PermissionAskedAsync(
        string sessionId, string permissionId, string description, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        await LogAsync(
            AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Permission, $"Asked permission: {description}", externalId: permissionId),
            cancellationToken).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Waiting, null, $"Wants to {description}", permissionId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>A permission was answered in OpenCode itself.</summary>
    public async Task PermissionRepliedAsync(string sessionId, string permissionId, bool allowed, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is not null)
        {
            await PermissionAnsweredAsync(task, permissionId, allowed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After an answer the agent works again, unless OpenCode holds more requests for the
    /// same session: then the next one is shown. OpenCode is the source of truth here.
    /// </summary>
    public async Task PermissionAnsweredAsync(WorkTask task, string permissionId, bool allowed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        var now = clock.GetUtcNow();
        await activity.ResumeAsync(task.Id.Value, permissionId, allowed ? "Permission granted" : "Permission refused", now, cancellationToken)
            .ConfigureAwait(false);

        var pending = await runtime.PendingPermissionsAsync(task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (pending.FirstOrDefault(p => p.SessionId == task.Run.SessionId && p.Id != permissionId) is { } next)
        {
            await ShowAsync(task, ActivityKind.Waiting, null, $"Wants to {next.Description}", next.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The agent stopped talking: its work goes to review with its summary as a note.</summary>
    public async Task FinishedAsync(string sessionId, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var summary = await runtime.GetLastReplyAsync(sessionId, task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            task.AddNote(NoteAuthor.FromDesk(task.DeskId!.Value), summary, now);
        }

        task.SubmitForReview(now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Stopped, null, "Done: waiting for review", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task FailedAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        task.Fail(reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Stopped, null, "Stuck: needs a look", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowProgressAsync(WorkTask task, string? step, string? doing, CancellationToken cancellationToken)
    {
        if (step is not null && activity.ForTask(task.Id.Value)?.Step != step)
        {
            await LogAsync(AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Step, step), cancellationToken).ConfigureAwait(false);
        }

        await ShowAsync(task, ActivityKind.Working, step, doing, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tools only act on a task while its agent is working on it.</summary>
    private async Task<WorkTask> WorkingTaskAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        return task is { Status: WorkTaskStatus.Running, DeskId: not null, Run: not null }
            ? task
            : throw new RejectedException("task-not-running", $"Task '{task.Title}' is {task.Status}: Openspace tools only work while its agent is working on it.");
    }

    private static string Guard(string? text, string what, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RejectedException("empty-text", $"{what} cannot be empty.");
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        audit.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.LogChangedAsync(entry.TaskId.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkTask?> RunningTaskAsync(string sessionId, CancellationToken cancellationToken)
    {
        var task = await tasks.FindBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return task is { Status: WorkTaskStatus.Running, DeskId: not null } ? task : null;
    }

    private Task ShowAsync(
        WorkTask task, ActivityKind kind, string? step, string? doing, string? permissionId, CancellationToken cancellationToken) =>
        activity.UpdateAsync(
            new DeskActivity(task.OfficeId.Value, task.DeskId!.Value.Value, task.Id.Value, kind, step, doing, permissionId, clock.GetUtcNow()),
            cancellationToken);
}
