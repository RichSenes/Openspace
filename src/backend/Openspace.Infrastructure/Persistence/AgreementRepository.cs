using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

internal sealed class AgreementRepository(OpenspaceDbContext db) : IAgreementRepository
{
    public Task<Agreement?> FindAsync(AgreementId id, CancellationToken cancellationToken) =>
        db.Agreements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Agreement>> ListAsync(OfficeId? officeId, CancellationToken cancellationToken) =>
        await db.Agreements.Where(a => officeId == null || a.OfficeId == officeId).OrderByDescending(a => a.OpenedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Agreement agreement) => db.Agreements.Add(agreement);
}
