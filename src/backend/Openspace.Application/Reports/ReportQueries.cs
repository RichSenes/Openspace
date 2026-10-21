using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Reports;

namespace Openspace.Application.Reports;

public sealed class ReportQueries(IReportRepository reports)
{
    public async Task<IReadOnlyList<ReportView>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        [.. (await reports.ListAsync(officeId, cancellationToken).ConfigureAwait(false)).Select(ReportView.From)];

    public async Task<ReportView> GetAsync(ReportId id, CancellationToken cancellationToken) =>
        ReportView.From(await reports.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
