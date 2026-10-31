using Openspace.Domain.Roles;

namespace Openspace.Application.Roles;

public sealed record RoleView(
    Guid Id,
    string Name,
    string Description,
    string Instructions,
    string? Model,
    PermissionLevel Edit,
    PermissionLevel Bash,
    PermissionLevel Web,
    PermissionLevel Helpers)
{
    public static RoleView From(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new(role.Id.Value, role.Name, role.Description, role.Instructions, role.Model,
            role.Permissions.Edit, role.Permissions.Bash, role.Permissions.Web, role.Permissions.Helpers);
    }
}
