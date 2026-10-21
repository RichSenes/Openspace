using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

internal sealed class EpicRepository(OpenspaceDbContext db) : IEpicRepository
{
    public Task<Epic?> FindAsync(EpicId id, CancellationToken cancellationToken) =>
        db.Epics.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Epic>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        await db.Epics.Where(e => e.OfficeId == officeId).OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Epic epic) => db.Epics.Add(epic);
}
