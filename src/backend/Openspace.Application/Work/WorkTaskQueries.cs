using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed class WorkTaskQueries(IWorkTaskRepository tasks)
{
    public async Task<IReadOnlyList<WorkTaskView>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken)
    {
        var found = await tasks.ListAsync(officeId, status, cancellationToken).ConfigureAwait(false);
        return [.. found.Select(WorkTaskView.From)];
    }

    public async Task<WorkTaskView> GetAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        WorkTaskView.From(await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
