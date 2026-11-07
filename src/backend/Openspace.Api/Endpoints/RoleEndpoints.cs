using Openspace.Application.Roles;
using Openspace.Domain.Roles;

namespace Openspace.Api.Endpoints;

internal static class RoleEndpoints
{
    public sealed record RoleRequest(
        string Name,
        string? Description,
        string? Instructions,
        string? Model,
        PermissionLevel Edit = PermissionLevel.Allow,
        PermissionLevel Bash = PermissionLevel.Allow,
        PermissionLevel Web = PermissionLevel.Allow,
        PermissionLevel Helpers = PermissionLevel.Deny)
    {
        // Helpers are on or off: asking each time would stop the agent at every hand-off.
        public RolePermissions Permissions => new(Edit, Bash, Web, Helpers == PermissionLevel.Allow ? PermissionLevel.Allow : PermissionLevel.Deny);
    }

    public sealed record DraftRequest(string Name, string Description);

    /// <param name="Skills">The skills the role's agents may load; null gives them every one.</param>
    public sealed record SkillsRequest(IReadOnlyList<string>? Skills);

    public static void MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var roles = app.MapGroup("/api/roles").WithTags("Roles");

        roles.MapGet("/", (RoleQueries queries, CancellationToken ct) => queries.ListAsync(ct));

        roles.MapPost("/", async (RoleRequest r, RoleCommands commands, CancellationToken ct) =>
        {
            var id = await commands.DefineAsync(r.Name, r.Description, r.Instructions, r.Model, r.Permissions, ct);
            return Results.Created($"/api/roles/{id.Value}", new { id = id.Value });
        });

        roles.MapPut("/{roleId:guid}", async (Guid roleId, RoleRequest r, RoleCommands commands, CancellationToken ct) =>
        {
            await commands.ChangeAsync(new RoleId(roleId), r.Name, r.Description, r.Instructions, r.Model, r.Permissions, ct);
            return Results.NoContent();
        });

        roles.MapDelete("/{roleId:guid}", async (Guid roleId, RoleCommands commands, CancellationToken ct) =>
        {
            await commands.DeleteAsync(new RoleId(roleId), ct);
            return Results.NoContent();
        });

        roles.MapPut("/{roleId:guid}/skills", async (Guid roleId, SkillsRequest r, RoleCommands commands, CancellationToken ct) =>
        {
            await commands.UseSkillsAsync(new RoleId(roleId), r.Skills, ct);
            return Results.NoContent();
        });

        app.MapGet("/api/skills", (Application.Abstractions.ISkillCatalog catalog) =>
            new { folder = catalog.Folder, skills = catalog.List() }).WithTags("Roles");

        roles.MapPost("/draft", async (DraftRequest r, RoleCommands commands, CancellationToken ct) =>
            Results.Ok(new { instructions = await commands.DraftInstructionsAsync(r.Name, r.Description, ct) }));
    }
}
