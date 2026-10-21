using Openspace.Domain.Offices;

namespace Openspace.Application.Abstractions;

public interface IOfficeRepository
{
    Task<Office?> FindAsync(OfficeId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Office>> ListAsync(CancellationToken cancellationToken);

    void Add(Office office);
}
