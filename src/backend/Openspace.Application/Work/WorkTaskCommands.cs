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
    AgentSignals signals,
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

    /// <param name="withReview">Sends the reviewer's latest note along, as the feedback or before it.</param>
    public Task SendBackAsync(
        WorkTaskId taskId, string reviewer, string? feedback, bool withReview, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.SendBack(NoteAuthor.Human(reviewer), Feedback(task, feedback, withReview), now), cancellationToken);

    /// <summary>
    /// Sets the tasks this one waits for; refused when it would make a loop, where tasks wait
    /// for each other and none could ever start.
    /// </summary>
    public async Task SetDependenciesAsync(WorkTaskId taskId, IReadOnlyCollection<WorkTaskId> dependsOn, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var others = new List<WorkTask>();
        foreach (var id in dependsOn.Distinct())
        {
            others.Add(await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false));
        }

        // Walk what the new ones wait for in turn: reaching this task again would be a loop.
        var seen = new HashSet<WorkTaskId>();
        var next = new Queue<WorkTask>(others);
        while (next.TryDequeue(out var current))
        {
            if (current.Id == task.Id)
            {
                throw new Domain.Common.DomainException($"\"{task.Title}\" cannot wait for that: those tasks already wait for it, and none could ever start.");
            }

            foreach (var id in current.DependsOn.Where(seen.Add))
            {
                if (await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } further)
                {
                    next.Enqueue(further);
                }
            }
        }

        task.DependOn(others, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A person tried a check by hand, or puts it back on the list.</summary>
    public Task TickManualCheckAsync(WorkTaskId taskId, int number, bool done, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.TickManualCheck(number, done, now), cancellationToken);

    private static string Feedback(WorkTask task, string? feedback, bool withReview)
    {
        var own = feedback?.Trim() ?? string.Empty;
        if (!withReview)
        {
            return own;
        }

        var review = task.Notes.LastOrDefault(n => n.Author.Kind == NoteAuthorKind.Reviewer && n.WrittenAt >= task.StatusChangedAt)
            ?? throw new Domain.Common.DomainException("There is no review of this work to send back.");
        var text = $"Apply this review of your work:\n{review.Text}";
        return own.Length > 0 ? $"{own}\n\n{text}" : text;
    }

    public Task RetryAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.Requeue(now), cancellationToken);

    /// <summary>Brings accepted work into one of your branches.</summary>
    public async Task MergeAsync(WorkTaskId taskId, string targetBranch, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        await MergeOneAsync(office, task, targetBranch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings all accepted work of an office into one branch, in the order it was accepted.
    /// Work that conflicts is skipped and said so; a branch that cannot take anything (your
    /// uncommitted changes, say) stops the round.
    /// </summary>
    /// <param name="only">The tasks to merge; all those waiting when null.</param>
    public async Task<IReadOnlyList<MergeOutcome>> MergeAllAsync(
        OfficeId officeId, string targetBranch, IReadOnlyCollection<WorkTaskId>? only, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var waiting = (await tasks.ListAsync(officeId, WorkTaskStatus.Done, cancellationToken).ConfigureAwait(false))
            .Where(t => t.AwaitsMerge && (only is null || only.Contains(t.Id)))
            .OrderBy(t => t.StatusChangedAt)
            .ToList();

        var outcomes = new List<MergeOutcome>();
        var stopped = false;
        foreach (var task in waiting)
        {
            if (stopped)
            {
                outcomes.Add(new MergeOutcome(task.Id.Value, task.Title, MergeResult.NotTried, "Not tried: the branch could not take the work above."));
                continue;
            }

            try
            {
                await MergeOneAsync(office, task, targetBranch, cancellationToken).ConfigureAwait(false);
                outcomes.Add(new MergeOutcome(task.Id.Value, task.Title, MergeResult.Merged, null));
            }
            catch (RejectedException ex) when (ex.Code == "merge-conflict")
            {
                outcomes.Add(new MergeOutcome(task.Id.Value, task.Title, MergeResult.Conflict, ex.Message));
            }
            catch (RejectedException ex)
            {
                // A missing target or your own uncommitted changes: the next ones would fail the same way.
                outcomes.Add(new MergeOutcome(task.Id.Value, task.Title, MergeResult.Failed, ex.Message));
                stopped = ex.Code != "task-branch-missing";
            }
        }

        return outcomes;
    }

    private async Task MergeOneAsync(Office office, WorkTask task, string targetBranch, CancellationToken cancellationToken)
    {
        // Checked before git is touched, recorded only once git succeeded.
        var target = task.CheckMerge(targetBranch);
        await workspaces
            .MergeAsync(office.RepositoryPath!, task.Run!.Branch, target, $"Merge task: {task.Title}", cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        task.RecordMerge(target, now);
        task.AddNote(NoteAuthor.System, $"Merged into {target}; branch {task.Run.Branch} deleted.", now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends accepted work that conflicts with <paramref name="targetBranch"/> back to its desk:
    /// Openspace starts the merge in a fresh working copy and the agent resolves what conflicts.
    /// </summary>
    public async Task CatchUpAsync(WorkTaskId taskId, string targetBranch, string requestedBy, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var target = task.CheckMerge(targetBranch);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var run = task.Run!;
        var files = await workspaces
            .StartCatchUpAsync(office.RepositoryPath!, new Workspace(run.WorkspacePath, run.Branch), target, cancellationToken)
            .ConfigureAwait(false);

        task.ReopenToCatchUp(NoteAuthor.Human(requestedBy), Briefing.ForCatchUp(target, files), clock.GetUtcNow());
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
        await signals.PermissionAnsweredAsync(task, permissionId, allow, cancellationToken).ConfigureAwait(false);
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
