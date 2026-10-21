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

    public DeskId Id { get; }

    public string Name { get; }

    public AgentProfile Agent { get; private set; }

    internal void Reassign(AgentProfile agent) => Agent = agent;
}
