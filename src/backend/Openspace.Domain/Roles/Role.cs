using Openspace.Domain.Common;

namespace Openspace.Domain.Roles;

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.CreateVersion7());
}

/// <summary>What an agent may do without asking.</summary>
public enum PermissionLevel
{
    Allow,
    Ask,
    Deny,
}

/// <param name="Edit">Creating and changing files.</param>
/// <param name="Bash">Running shell commands.</param>
/// <param name="Web">Fetching pages from the internet.</param>
/// <param name="Helpers">
/// Whether a Claude Code agent may hand part of its work to helper agents of its own
/// (subagents): Allow or Deny, off unless the role turns it on. OpenCode agents always may.
/// </param>
public sealed record RolePermissions(PermissionLevel Edit, PermissionLevel Bash, PermissionLevel Web, PermissionLevel Helpers = PermissionLevel.Deny)
{
    /// <summary>OpenCode's own defaults: work freely.</summary>
    public static RolePermissions Default { get; } = new(PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Allow);
}

/// <summary>
/// The definition of a kind of agent, reusable across desks: what it is for, how it
/// works, which model it prefers and what it may do without asking.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    internal const int MaxNameLength = 80;
    private const int MaxDescriptionLength = 300;
    private const int MaxInstructionsLength = 20_000;

    // Used by persistence, which sets Permissions right after construction.
    private Role(RoleId id, string name, string description, string instructions, string? model)
        : this(id, name, description, instructions, model, null!)
    {
    }

    private Role(RoleId id, string name, string description, string instructions, string? model, RolePermissions permissions)
        : base(id)
    {
        Name = name;
        Description = description;
        Instructions = instructions;
        Model = model;
        Permissions = permissions;
    }

    public string Name { get; private set; }

    /// <summary>One line, for people choosing a role.</summary>
    public string Description { get; private set; }

    /// <summary>How the agent works: given to it as system instructions in every session.</summary>
    public string Instructions { get; private set; }

    /// <summary>Preferred model in <c>provider/model</c> form; a desk can still override it.</summary>
    public string? Model { get; private set; }

    public RolePermissions Permissions { get; private set; }

    public static Role Define(
        string name, string? description, string? instructions, string? model, RolePermissions permissions, DateTimeOffset now)
    {
        var role = new Role(RoleId.New(), string.Empty, string.Empty, string.Empty, null, RolePermissions.Default);
        role.Apply(name, description, instructions, model, permissions);
        role.Raise(new RoleDefined(role.Id, role.Name, now));
        return role;
    }

    public void Change(
        string name, string? description, string? instructions, string? model, RolePermissions permissions, DateTimeOffset now)
    {
        Apply(name, description, instructions, model, permissions);
        Raise(new RoleChanged(Id, now));
    }

    private void Apply(string name, string? description, string? instructions, string? model, RolePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        Name = Guard.NotBlank(name, "Role name", MaxNameLength);
        Description = Limit(description, "Role description", MaxDescriptionLength);
        Instructions = Limit(instructions, "Role instructions", MaxInstructionsLength);
        Model = Offices.AgentProfile.ValidModel(model);
        Permissions = permissions;
    }

    private static string Limit(string? text, string what, int max)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length <= max ? trimmed : throw new DomainException($"{what} cannot be longer than {max} characters.");
    }
}

public sealed record RoleDefined(RoleId RoleId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RoleChanged(RoleId RoleId, DateTimeOffset OccurredAt) : IDomainEvent;
