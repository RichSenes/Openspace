using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Abstractions;

public interface IEpicRepository
{
    Task<Epic?> FindAsync(EpicId id, CancellationToken cancellationToken);

    /// <summary>Epics of an office, newest first.</summary>
    Task<IReadOnlyList<Epic>> ListAsync(OfficeId officeId, CancellationToken cancellationToken);

    void Add(Epic epic);
}
