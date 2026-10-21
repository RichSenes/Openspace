using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>What a person can do with tasks: create, approve, review, discard.</summary>
public sealed class WorkTaskCommands(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    IWorkspaceManager workspaces,
    ActivityBoard activity,
    IAuditLog audit,
    TimeProvider clock)
{
    public async Task<WorkTaskId> AssignAsync(
        OfficeId officeId,
        DeskId deskId,
        string title,
        string? description,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var task = WorkTask.Assign(office, deskId, title, description, requestedBy, clock.GetUtcNow());
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return task.Id;
    }

    public async Task<WorkTaskId> ProposeAsync(
        OfficeId officeId, string title, string? description, string requestedBy, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var task = WorkTask.Propose(office, title, description, requestedBy, clock.GetUtcNow());
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return task.Id;
    }

    /// <summary>Approves a proposed task, optionally choosing its desk at the same time.</summary>
    public async Task ApproveAsync(WorkTaskId taskId, DeskId? deskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (deskId is { } desk)
        {
            var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
            task.AssignTo(office, desk);
        }

        task.Approve(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Accepts the work: the agent's changes are committed on the task branch.</summary>
    public async Task AcceptAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        task.Accept(now);

        if (task.Run is { } run)
        {
            var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
            await workspaces.CommitAsync(office.RepositoryPath!, run.WorkspacePath, task.Title, cancellationToken).ConfigureAwait(false);
            task.AddNote(NoteAuthor.System, $"Changes committed on branch {run.Branch}.", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activity.ClearTaskAsync(task.Id.Value, now, cancellationToken).ConfigureAwait(false);
    }

    public Task SendBackAsync(
        WorkTaskId taskId, string reviewer, string feedback, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.SendBack(NoteAuthor.Human(reviewer), feedback, now), cancellationToken);

    public Task RetryAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.Requeue(now), cancellationToken);

    /// <summary>Brings accepted work into one of your branches.</summary>
    public async Task MergeAsync(WorkTaskId taskId, string targetBranch, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        task.RecordMerge(targetBranch, now);

        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        await workspaces
            .MergeAsync(office.RepositoryPath!, task.Run!.Branch, task.MergedInto!, $"Merge task: {task.Title}", cancellationToken)
            .ConfigureAwait(false);

        task.AddNote(NoteAuthor.System, $"Merged into {task.MergedInto}; branch {task.Run.Branch} deleted.", now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops the task, stopping its agent and throwing its working copy away.</summary>
    public async Task DiscardAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var wasRunning = task.Status == WorkTaskStatus.Running;
        var now = clock.GetUtcNow();
        task.Discard(now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activity.ClearTaskAsync(task.Id.Value, now, cancellationToken).ConfigureAwait(false);

        if (task.Run is { } run)
        {
            if (wasRunning)
            {
                await runtime.AbortAsync(run.SessionId, run.WorkspacePath, cancellationToken).ConfigureAwait(false);
            }

            var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
            await workspaces.DiscardAsync(office.RepositoryPath!, new Workspace(run.WorkspacePath, run.Branch), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Answers an agent waiting for permission.</summary>
    public async Task ReplyToPermissionAsync(WorkTaskId taskId, bool allow, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var waiting = activity.ForTask(task.Id.Value);
        if (task.Run is null || waiting is not { Kind: ActivityKind.Waiting, PermissionId: { } permissionId })
        {
            throw new Domain.Common.DomainException("This task is not waiting for a permission.");
        }

        await runtime.ReplyToPermissionAsync(permissionId, task.Run.WorkspacePath, allow, cancellationToken).ConfigureAwait(false);
        audit.Add(AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Permission, allow ? "Permission granted once" : "Permission refused"));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activity.ResumeAsync(task.Id.Value, allow ? "Permission granted" : "Permission refused", clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task AddNoteAsync(WorkTaskId taskId, string author, string text, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.AddNote(NoteAuthor.Human(author), text, now), cancellationToken);

    private async Task UpdateAsync(
        WorkTaskId taskId, Action<WorkTask, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        change(task, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
