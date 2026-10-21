using Openspace.Domain.Offices;
using Openspace.Domain.Reports;

namespace Openspace.Application.Abstractions;

public interface IReportRepository
{
    Task<Report?> FindAsync(ReportId id, CancellationToken cancellationToken);

    /// <summary>Reports of an office, newest first.</summary>
    Task<IReadOnlyList<Report>> ListAsync(OfficeId officeId, CancellationToken cancellationToken);

    void Add(Report report);
}
