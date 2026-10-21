using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed class WorkTaskQueries(IWorkTaskRepository tasks, IWorkspaceManager workspaces)
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

    public async Task<WorkTaskView> GetAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        WorkTaskView.From(await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
