using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Infrastructure.Persistence;

internal sealed class OfficeRepository(OpenspaceDbContext db) : IOfficeRepository
{
    public Task<Office?> FindAsync(OfficeId id, CancellationToken cancellationToken) =>
        db.Offices.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Office>> ListAsync(CancellationToken cancellationToken) =>
        await db.Offices.OrderBy(o => o.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Office office) => db.Offices.Add(office);
}
