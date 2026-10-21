using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>What a person can do with tasks: create, approve, review, discard.</summary>
public sealed class WorkTaskCommands(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
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

    public Task AcceptAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.Accept(now), cancellationToken);

    public Task SendBackAsync(
        WorkTaskId taskId, string reviewer, string feedback, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.SendBack(NoteAuthor.Human(reviewer), feedback, now), cancellationToken);

    public Task RetryAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.Requeue(now), cancellationToken);

    public Task DiscardAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        UpdateAsync(taskId, (task, now) => task.Discard(now), cancellationToken);

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
