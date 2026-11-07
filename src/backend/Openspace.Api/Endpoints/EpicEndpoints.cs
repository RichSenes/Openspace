using Microsoft.Extensions.Options;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Api.Endpoints;

internal static class EpicEndpoints
{
    public sealed record PlanRequest(string Request);

    public static void MapEpicEndpoints(this IEndpointRouteBuilder app)
    {
        // A plan takes a minute or more: it runs in the background and shows up in SupervisorJobs.
        app.MapPost("/api/offices/{officeId:guid}/plan", async (
            Guid officeId, PlanRequest request, SupervisorJobs jobs, Supervisor supervisor, IOptions<OperatorOptions> me, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Request))
            {
                throw new Application.RejectedException("empty-text", "Write what you need first.");
            }

            await supervisor.CheckAsync(new OfficeId(officeId), token);

            var by = me.Value.Name;
            var job = jobs.Start(officeId, "plan", request.Request, $"plan:{officeId}:{request.Request.Trim().GetHashCode(StringComparison.Ordinal)}", async (services, ct) =>
            {
                var plan = await services.GetRequiredService<Supervisor>().PlanAsync(new OfficeId(officeId), request.Request, by, ct);
                return new SupervisorOutcome(plan.Tasks.Count, plan.Epic.Id);
            });
            return Results.Accepted($"/api/supervisor/jobs?officeId={officeId}", job);
        }).WithTags("Epics");

        app.MapGet("/api/supervisor/jobs", (Guid? officeId, SupervisorJobs jobs) => jobs.Snapshot(officeId)).WithTags("Epics");

        var epics = app.MapGroup("/api/epics").WithTags("Epics");

        epics.MapGet("/", (Guid officeId, EpicQueries queries, CancellationToken ct) => queries.ListAsync(new OfficeId(officeId), ct));

        epics.MapPost("/{epicId:guid}/approve", async (Guid epicId, Supervisor supervisor, CancellationToken ct) =>
            Results.Ok(new { approved = await supervisor.ApproveAllAsync(new EpicId(epicId), ct) }));
    }
}
