using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed class WorkTaskQueries(
    IOfficeRepository offices, IWorkTaskRepository tasks, IWorkspaceManager workspaces, IAuditLog audit)
{
    public async Task<IReadOnlyList<WorkTaskView>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken)
    {
        var found = await tasks.ListAsync(officeId, status, cancellationToken).ConfigureAwait(false);
        return [.. found.Select(WorkTaskView.From)];
    }

    /// <summary>What the agent changed so far; empty when it has not started.</summary>
    public async Task<string> DiffAsync(WorkTaskId id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return task.Run is null || task.IsClosed
            ? string.Empty
            : await workspaces.DiffAsync(task.Run.WorkspacePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The task's audit log, oldest first.</summary>
    public async Task<IReadOnlyList<AuditEntryView>> LogAsync(WorkTaskId id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var log = await audit.ForTaskAsync(task.Id, cancellationToken).ConfigureAwait(false);
        return [.. log.Select(AuditEntryView.From)];
    }

    /// <summary>A Markdown report of the task, to keep or to share.</summary>
    public async Task<string> ReportAsync(WorkTaskId id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var log = await audit.ForTaskAsync(task.Id, cancellationToken).ConfigureAwait(false);
        return TaskReport.Build(office, task, log);
    }

    public async Task<WorkTaskView> GetAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        WorkTaskView.From(await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
