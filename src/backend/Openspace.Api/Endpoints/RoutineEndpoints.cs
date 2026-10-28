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

    public sealed record KnownKanjiRequest(string Kanji);

    public sealed record BroughtOfferRequest(string Text, string? Link);

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

        routines.MapPost("/stop", async (Guid routineId, RoutineCommands commands, CancellationToken ct) =>
            await commands.StopAsync(new RoutineId(routineId), ct)
                ? Results.NoContent()
                : Results.Problem("This routine is not running.", statusCode: StatusCodes.Status409Conflict));

        routines.MapGet("/runs", (Guid routineId, RoutineQueries queries, CancellationToken ct) => queries.RunsAsync(new RoutineId(routineId), ct));

        // An offer the person found and pastes in: the job offers routine judges it on its next run, asked now.
        routines.MapPost("/offers", async (Guid routineId, BroughtOfferRequest request, OfferInbox inbox, CancellationToken ct) =>
        {
            await inbox.AddAsync(new RoutineId(routineId), request.Text, request.Link, ct);
            return Results.Accepted();
        });

        // The kanji of the day: the next one, how many are explained, and "I know it" to skip one.
        routines.MapGet("/kanji", (Guid routineId, KanjiCourse course, CancellationToken ct) => course.ViewAsync(new RoutineId(routineId), ct));

        routines.MapPost("/kanji/known", async (Guid routineId, KnownKanjiRequest request, KanjiCourse course, CancellationToken ct) =>
        {
            await course.KnownAsync(new RoutineId(routineId), request.Kanji, ct);
            return Results.NoContent();
        });

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
