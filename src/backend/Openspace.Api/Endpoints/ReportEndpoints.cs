using Microsoft.Extensions.Options;
using Openspace.Application.Reports;
using Openspace.Domain.Offices;
using Openspace.Domain.Reports;
using Openspace.Domain.Work;

namespace Openspace.Api.Endpoints;

internal static class ReportEndpoints
{
    /// <param name="Scope">How people call the period, e.g. "21 October 2026".</param>
    /// <param name="Language">The language to write in, e.g. "Italian".</param>
    public sealed record PeriodRequest(DateTimeOffset From, DateTimeOffset To, string Scope, string Language = "English");

    public sealed record EpicReportRequest(string Language = "English");

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/offices/{officeId:guid}/reports", (
            Guid officeId, PeriodRequest r, ReportWriter writer, IOptions<OperatorOptions> me, CancellationToken ct) =>
            writer.WriteForPeriodAsync(new OfficeId(officeId), r.From, r.To, r.Scope, r.Language, me.Value.Name, ct)).WithTags("Reports");

        app.MapPost("/api/epics/{epicId:guid}/report", (
            Guid epicId, EpicReportRequest r, ReportWriter writer, IOptions<OperatorOptions> me, CancellationToken ct) =>
            writer.WriteForEpicAsync(new EpicId(epicId), r.Language, me.Value.Name, ct)).WithTags("Reports");

        app.MapGet("/api/offices/{officeId:guid}/reports", (Guid officeId, ReportQueries queries, CancellationToken ct) =>
            queries.ListAsync(new OfficeId(officeId), ct)).WithTags("Reports");

        app.MapGet("/api/reports/{reportId:guid}/download", async (Guid reportId, ReportQueries queries, CancellationToken ct) =>
        {
            var report = await queries.GetAsync(new ReportId(reportId), ct);
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(report.Body), "text/markdown; charset=utf-8", $"report-{report.CreatedAt:yyyy-MM-dd}.md");
        }).WithTags("Reports");
    }
}
