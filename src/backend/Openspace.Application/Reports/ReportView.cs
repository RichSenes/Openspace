using Openspace.Domain.Reports;

namespace Openspace.Application.Reports;

public sealed record ReportView(
    Guid Id,
    Guid OfficeId,
    ReportKind Kind,
    string Title,
    string Body,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    Guid? EpicId,
    bool WrittenByAi)
{
    public static ReportView From(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new(report.Id.Value, report.OfficeId.Value, report.Kind, report.Title, report.Body, report.RequestedBy,
            report.CreatedAt, report.From, report.To, report.EpicId?.Value, report.WrittenByAi);
    }
}
