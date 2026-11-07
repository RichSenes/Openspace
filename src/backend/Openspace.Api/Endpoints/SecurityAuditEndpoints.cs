using Microsoft.Extensions.Options;
using Openspace.Application.Security;
using Openspace.Domain.Offices;

namespace Openspace.Api.Endpoints;

internal static class SecurityAuditEndpoints
{
    /// <param name="DeskId">The desk to run it at; the office's auditor desk when left out.</param>
    /// <param name="Language">The language its report and findings are written in, such as Italian; English when left out.</param>
    public sealed record StartRequest(Guid? DeskId, SecurityAuditProfile Profile = SecurityAuditProfile.Quick, string? Language = null);

    /// <param name="Model">The Claude model of the new Auditor desk; sonnet when left out.</param>
    public sealed record DeskRequest(string? Model);

    public static void MapSecurityAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var audits = app.MapGroup("/api/offices/{officeId:guid}/security-audits").WithTags("Security audits");

        audits.MapGet("/", (Guid officeId, SecurityAudits security, CancellationToken ct) =>
            security.ListAsync(new OfficeId(officeId), ct));

        audits.MapPost("/", async (Guid officeId, StartRequest r, SecurityAudits security, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            var id = await security.StartAsync(new OfficeId(officeId), r.DeskId is { } desk ? new DeskId(desk) : null, r.Profile, me.Value.Name, ct, r.Language);
            return Results.Created($"/api/tasks/{id.Value}", new { taskId = id.Value });
        });

        audits.MapGet("/{run:int}/report", async (Guid officeId, int run, SecurityAudits security, CancellationToken ct) =>
            await security.ReportAsync(new OfficeId(officeId), run, ct) is { } report
                ? Results.Text(report, "text/markdown; charset=utf-8")
                : Results.NotFound());

        audits.MapGet("/{run:int}/findings", (Guid officeId, int run, SecurityAudits security, CancellationToken ct) =>
            security.FindingsAsync(new OfficeId(officeId), run, ct));

        audits.MapPost("/{run:int}/findings/{fingerprint}/fix", async (
            Guid officeId, int run, string fingerprint, SecurityAudits security, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            var id = await security.ProposeFixAsync(new OfficeId(officeId), run, fingerprint, me.Value.Name, ct);
            return Results.Created($"/api/tasks/{id.Value}", new { taskId = id.Value });
        });

        app.MapPost("/api/offices/{officeId:guid}/security-desk", async (Guid officeId, DeskRequest r, SecurityAudits security, CancellationToken ct) =>
            Results.Ok(new { deskId = (await security.AddDeskAsync(new OfficeId(officeId), r.Model, ct)).Value })).WithTags("Security audits");
    }
}
