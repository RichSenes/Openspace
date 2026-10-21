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
        app.MapPost("/api/offices/{officeId:guid}/plan", (
            Guid officeId, PlanRequest request, Supervisor supervisor, IOptions<OperatorOptions> me, CancellationToken ct) =>
            supervisor.PlanAsync(new OfficeId(officeId), request.Request, me.Value.Name, ct)).WithTags("Epics");

        var epics = app.MapGroup("/api/epics").WithTags("Epics");

        epics.MapGet("/", (Guid officeId, EpicQueries queries, CancellationToken ct) => queries.ListAsync(new OfficeId(officeId), ct));

        epics.MapPost("/{epicId:guid}/approve", async (Guid epicId, Supervisor supervisor, CancellationToken ct) =>
            Results.Ok(new { approved = await supervisor.ApproveAllAsync(new EpicId(epicId), ct) }));
    }
}
