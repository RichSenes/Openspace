using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Abstractions;

public interface IWorkTaskRepository
{
    Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken);

    /// <summary>Tasks matching the optional filters, oldest first.</summary>
    Task<IReadOnlyList<WorkTask>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken);

    Task<WorkTask?> FindBySessionAsync(string sessionId, CancellationToken cancellationToken);

    void Add(WorkTask task);
}
