using Openspace.Domain.Roles;

namespace Openspace.Domain.Offices;

/// <summary>A seat in a room, bound to one OpenCode agent.</summary>
public sealed class Desk
{
    internal Desk(DeskId id, string name, AgentProfile agent)
    {
        Id = id;
        Name = name;
        Agent = agent;
    }

    // Used by persistence, which sets Agent right after construction.
    private Desk(DeskId id, string name)
        : this(id, name, null!)
    {
    }

    public DeskId Id { get; }

    public string Name { get; }

    public AgentProfile Agent { get; private set; }

    /// <summary>The role that tells the agent how to work; null for a plain OpenCode agent.</summary>
    public RoleId? RoleId { get; private set; }

    internal void Reassign(AgentProfile agent) => Agent = agent;

    internal void GiveRole(RoleId? roleId) => RoleId = roleId;
}
