using Openspace.Application.Routines;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Api.Endpoints;

internal static class RoutineEndpoints
{
    public sealed record CreateRoutineRequest(string Name, string Kind, ScheduleView Schedule, string? Settings);

    public sealed record UpdateRoutineRequest(string? Name, ScheduleView? Schedule, string? Settings);

    public sealed record EnabledRequest(bool Enabled);

    public sealed record AskFirstRequest(bool AskFirst);

    public static void MapRoutineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/routine-kinds", (RoutineQueries queries) => queries.Kinds()).WithTags("Routines");

        var offices = app.MapGroup("/api/offices/{officeId:guid}").WithTags("Routines");
        offices.MapGet("/routines", (Guid officeId, RoutineQueries queries, CancellationToken ct) =>
            queries.ListAsync(new OfficeId(officeId), ct));

        offices.MapPost("/routines", async (Guid officeId, CreateRoutineRequest request, RoutineCommands commands, CancellationToken ct) =>
        {
            var id = await commands.CreateAsync(new OfficeId(officeId), request.Name, request.Kind, request.Schedule, request.Settings, ct);
            return Results.Created($"/api/routines/{id.Value}", new { id = id.Value });
        });

        // ?status=Pending for the proposals waiting for an answer.
        offices.MapGet("/proposals", (Guid officeId, ProposalStatus? status, RoutineQueries queries, CancellationToken ct) =>
            queries.ProposalsAsync(new OfficeId(officeId), status, ct));

        var routines = app.MapGroup("/api/routines/{routineId:guid}").WithTags("Routines");
        routines.MapPut("/", async (Guid routineId, UpdateRoutineRequest request, RoutineCommands commands, CancellationToken ct) =>
        {
            await commands.UpdateAsync(new RoutineId(routineId), request.Name, request.Schedule, request.Settings, ct);
            return Results.NoContent();
        });

        routines.MapPut("/enabled", async (Guid routineId, EnabledRequest request, RoutineCommands commands, CancellationToken ct) =>
        {
            await commands.TurnOnAsync(new RoutineId(routineId), request.Enabled, ct);
            return Results.NoContent();
        });

        routines.MapPut("/ask-first", async (Guid routineId, AskFirstRequest request, RoutineCommands commands, CancellationToken ct) =>
        {
            await commands.AskFirstAsync(new RoutineId(routineId), request.AskFirst, ct);
            return Results.NoContent();
        });

        routines.MapDelete("/", async (Guid routineId, RoutineCommands commands, CancellationToken ct) =>
        {
            await commands.DeleteAsync(new RoutineId(routineId), ct);
            return Results.NoContent();
        });

        routines.MapPost("/run", async (Guid routineId, RoutineCommands commands, CancellationToken ct) =>
        {
            await commands.RunNowAsync(new RoutineId(routineId), ct);
            return Results.Accepted();
        });

        routines.MapGet("/runs", (Guid routineId, RoutineQueries queries, CancellationToken ct) => queries.RunsAsync(new RoutineId(routineId), ct));

        var proposals = app.MapGroup("/api/proposals/{proposalId:guid}").WithTags("Routines");
        proposals.MapPost("/approve", async (Guid proposalId, ProposalCommands commands, CancellationToken ct) =>
        {
            await commands.ApproveAsync(new ProposalId(proposalId), ct);
            return Results.NoContent();
        });

        proposals.MapPost("/reject", async (Guid proposalId, ProposalCommands commands, CancellationToken ct) =>
        {
            await commands.RejectAsync(new ProposalId(proposalId), ct);
            return Results.NoContent();
        });
    }
}
