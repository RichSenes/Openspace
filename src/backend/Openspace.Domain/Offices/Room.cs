using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>An area of competence inside an office, with its own desks and context.</summary>
public sealed class Room
{
    private readonly List<Desk> _desks = [];

    internal Room(RoomId id, string name)
    {
        Id = id;
        Name = name;
    }

    public RoomId Id { get; }

    public string Name { get; }

    /// <summary>Conventions, paths, do and don't: injected into every agent working here.</summary>
    public string Context { get; private set; } = string.Empty;

    public IReadOnlyList<Desk> Desks => _desks;

    internal void UpdateContext(string? context) => Context = context?.Trim() ?? string.Empty;

    internal Desk AddDesk(string name, AgentProfile agent)
    {
        var deskName = Guard.NotBlank(name, "Desk name", Office.MaxNameLength);
        if (_desks.Exists(d => string.Equals(d.Name, deskName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Room '{Name}' already has a desk named '{deskName}'.");
        }

        var desk = new Desk(DeskId.New(), deskName, agent);
        _desks.Add(desk);
        return desk;
    }
}
