using Openspace.Application.Offices;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;

namespace Openspace.Api.Endpoints;

internal static class OfficeEndpoints
{
    public sealed record NameRequest(string Name);

    public sealed record ContextRequest(string? Context);

    /// <param name="Initialize">Turn the folder into a git repository if it is not one yet.</param>
    public sealed record ProcedureRequest(IReadOnlyList<string> Steps);

    public sealed record ThemeRequest(string Theme);

    public sealed record RepositoryRequest(string Path, bool Initialize = false);

    public sealed record DeskRequest(string Name, string AgentName, string? Model, Guid? RoleId = null);

    public sealed record DeskRoleRequest(Guid? RoleId);

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

        offices.MapPut("/{officeId:guid}/theme", async (
            Guid officeId, ThemeRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.ChangeThemeAsync(new OfficeId(officeId), request.Theme, ct);
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

        offices.MapPut("/{officeId:guid}/rooms/{roomId:guid}/procedure", async (
            Guid officeId, Guid roomId, ProcedureRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.SetRoomProcedureAsync(new OfficeId(officeId), new RoomId(roomId), request.Steps ?? [], ct);
            return Results.NoContent();
        });

        offices.MapPost("/{officeId:guid}/rooms/{roomId:guid}/desks", async (
            Guid officeId, Guid roomId, DeskRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            var id = await commands.AddDeskAsync(
                new OfficeId(officeId), new RoomId(roomId), request.Name, request.AgentName, request.Model,
                request.RoleId is { } role ? new RoleId(role) : null, ct);
            return Results.Ok(new { id = id.Value });
        });

        offices.MapPut("/{officeId:guid}/desks/{deskId:guid}/name", async (
            Guid officeId, Guid deskId, NameRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.RenameDeskAsync(new OfficeId(officeId), new DeskId(deskId), request.Name, ct);
            return Results.NoContent();
        });

        offices.MapPut("/{officeId:guid}/desks/{deskId:guid}/role", async (
            Guid officeId, Guid deskId, DeskRoleRequest request, OfficeCommands commands, CancellationToken ct) =>
        {
            await commands.SetDeskRoleAsync(
                new OfficeId(officeId), new DeskId(deskId), request.RoleId is { } role ? new RoleId(role) : null, ct);
            return Results.NoContent();
        });
    }
}
