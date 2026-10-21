using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Reports;

namespace Openspace.Infrastructure.Persistence;

internal sealed class ReportRepository(OpenspaceDbContext db) : IReportRepository
{
    public Task<Report?> FindAsync(ReportId id, CancellationToken cancellationToken) =>
        db.Reports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Report>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        await db.Reports.Where(r => r.OfficeId == officeId).OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Report report) => db.Reports.Add(report);
}
