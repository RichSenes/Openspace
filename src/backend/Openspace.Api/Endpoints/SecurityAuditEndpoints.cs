using Microsoft.Extensions.Options;
using Openspace.Application.Security;
using Openspace.Application.Work;
using Openspace.Domain.Offices;

namespace Openspace.Api.Endpoints;

internal static class SecurityAuditEndpoints
{
    /// <param name="DeskId">The desk to run it at; the office's auditor desk when left out.</param>
    /// <param name="Language">The language its report and findings are written in, such as Italian; English when left out.</param>
    /// <param name="HandOff">Whether its findings go to the supervisor on their own once it finishes.</param>
    public sealed record StartRequest(Guid? DeskId, SecurityAuditProfile Profile = SecurityAuditProfile.Quick, string? Language = null, bool HandOff = true);

    /// <param name="Language">The language of the tasks' titles and descriptions; English when left out.</param>
    public sealed record HandOffRequest(string? Language);

    /// <param name="Model">The Claude model of the new Auditor desk; sonnet when left out.</param>
    public sealed record DeskRequest(string? Model);

    private static SupervisorOutcome Outcome(SecurityHandOff handOff) => new(handOff.Tasks, handOff.EpicId);

    public static void MapSecurityAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var audits = app.MapGroup("/api/offices/{officeId:guid}/security-audits").WithTags("Security audits");

        audits.MapGet("/", (Guid officeId, SecurityAudits security, CancellationToken ct) =>
            security.ListAsync(new OfficeId(officeId), ct));

        audits.MapPost("/", async (Guid officeId, StartRequest r, SecurityAudits security, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            var id = await security.StartAsync(new OfficeId(officeId), r.DeskId is { } desk ? new DeskId(desk) : null, r.Profile, me.Value.Name, ct, r.Language, r.HandOff);
            return Results.Created($"/api/tasks/{id.Value}", new { taskId = id.Value });
        });

        audits.MapGet("/{run:int}/report", async (Guid officeId, int run, SecurityAudits security, CancellationToken ct) =>
            await security.ReportAsync(new OfficeId(officeId), run, ct) is { } report
                ? Results.Text(report, "text/markdown; charset=utf-8")
                : Results.NotFound());

        audits.MapGet("/{run:int}/findings", (Guid officeId, int run, SecurityAudits security, CancellationToken ct) =>
            security.FindingsAsync(new OfficeId(officeId), run, ct));

        // Handing findings over asks the supervisor for a plan: it runs in the background.
        audits.MapPost("/{run:int}/findings/{fingerprint}/fix", async (
            Guid officeId, int run, string fingerprint, HandOffRequest? r, SecurityAudits security, SupervisorJobs jobs, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            var finding = await security.OpenFindingAsync(new OfficeId(officeId), run, fingerprint, ct);
            var by = me.Value.Name;
            var job = jobs.Start(officeId, "security", $"Fix: {finding.Title}", SecurityAudits.JobKey(officeId, run), async (services, token) =>
                Outcome(await services.GetRequiredService<SecurityAudits>().ProposeFixAsync(new OfficeId(officeId), run, fingerprint, by, token, r?.Language)));
            return Results.Accepted($"/api/supervisor/jobs?officeId={officeId}", job);
        });

        audits.MapPost("/{run:int}/hand-off", (
            Guid officeId, int run, HandOffRequest? r, SupervisorJobs jobs, IOptions<OperatorOptions> me) =>
        {
            var by = me.Value.Name;
            var job = jobs.Start(officeId, "security", $"Findings of security audit run {run}", SecurityAudits.JobKey(officeId, run), async (services, token) =>
                Outcome(await services.GetRequiredService<SecurityAudits>().HandToSupervisorAsync(new OfficeId(officeId), run, by, token, r?.Language)));
            return Results.Accepted($"/api/supervisor/jobs?officeId={officeId}", job);
        });

        app.MapPost("/api/offices/{officeId:guid}/security-desk", async (Guid officeId, DeskRequest r, SecurityAudits security, CancellationToken ct) =>
            Results.Ok(new { deskId = (await security.AddDeskAsync(new OfficeId(officeId), r.Model, ct)).Value })).WithTags("Security audits");
    }
}
