using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>
/// Reacts to what agents report while they work. Signals for sessions Openspace
/// did not start (e.g. someone using OpenCode directly) are ignored.
/// </summary>
public sealed class AgentSignals(
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

        if (step is not null && activity.ForTask(task.Id.Value)?.Step != step)
        {
            await LogAsync(AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Step, step), cancellationToken).ConfigureAwait(false);
        }

        await ShowAsync(task, ActivityKind.Working, step, doing, null, cancellationToken).ConfigureAwait(false);
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
        await ShowAsync(task, ActivityKind.Waiting, null, $"Asks permission: {description}", permissionId, cancellationToken)
            .ConfigureAwait(false);
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
