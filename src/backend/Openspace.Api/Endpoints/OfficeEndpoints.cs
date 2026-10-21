using Openspace.Application.Offices;
using Openspace.Domain.Offices;

namespace Openspace.Api.Endpoints;

internal static class OfficeEndpoints
{
    public sealed record NameRequest(string Name);

    public sealed record ContextRequest(string? Context);

    /// <param name="Initialize">Turn the folder into a git repository if it is not one yet.</param>
    public sealed record RepositoryRequest(string Path, bool Initialize = false);

    public sealed record DeskRequest(string Name, string AgentName, string? Model);

    public static void MapOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        var offices = app.MapGroup("/api/offices").WithTags("Offices");

        offices.MapGet("/", (OfficeQueries queries, CancellationToken ct) => queries.ListAsync(ct));

        offices.MapGet("/{officeId:guid}", (Guid officeId, OfficeQueries queries, CancellationToken ct) =>
            queries.GetAsync(new OfficeId(officeId), ct));

        offices.MapGet("/{officeId:guid}/branches", (Guid officeId, OfficeQueries queries, CancellationToken ct) =>
            queries.BranchesAsync(new OfficeId(officeId), ct));

        offices.MapPost("/", async (NameRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            var id = await commands.OpenOfficeAsync(request.Name, ct);
            return Results.Created($"/api/offices/{id.Value}", new { id = id.Value });
        });

        offices.MapPut("/{officeId:guid}/repository", async (
            Guid officeId, RepositoryRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.SetRepositoryAsync(new OfficeId(officeId), request.Path, request.Initialize, ct);
            return Results.NoContent();
        });

        offices.MapPost("/{officeId:guid}/rooms", async (
            Guid officeId, NameRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            var id = await commands.OpenRoomAsync(new OfficeId(officeId), request.Name, ct);
            return Results.Ok(new { id = id.Value });
        });

        offices.MapPut("/{officeId:guid}/rooms/{roomId:guid}/context", async (
            Guid officeId, Guid roomId, ContextRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.UpdateRoomContextAsync(new OfficeId(officeId), new RoomId(roomId), request.Context, ct);
            return Results.NoContent();
        });

        offices.MapPost("/{officeId:guid}/rooms/{roomId:guid}/desks", async (
            Guid officeId, Guid roomId, DeskRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            var id = await commands.AddDeskAsync(
                new OfficeId(officeId), new RoomId(roomId), request.Name, request.AgentName, request.Model, ct);
            return Results.Ok(new { id = id.Value });
        });
    }
}
