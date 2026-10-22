using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>An area of competence inside an office, with its own desks and context.</summary>
public sealed class Room
{
    private readonly List<Desk> _desks = [];
    private List<string> _procedure = [];
    private List<FurniturePiece> _furniture = [];

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

    /// <summary>Tiles from wall to wall; null while the room is laid out automatically.</summary>
    public int? Width { get; private set; }

    /// <summary>Tiles of floor in front of the back wall; null while the room is laid out automatically.</summary>
    public int? Depth { get; private set; }

    /// <summary>Furniture placed by hand; empty while the room is laid out automatically.</summary>
    public IReadOnlyList<FurniturePiece> Furniture => _furniture;

    /// <summary>Steps every task of this room must follow; checked before its work goes to review.</summary>
    public IReadOnlyList<string> Procedure => _procedure;

    internal void UpdateContext(string? context) => Context = context?.Trim() ?? string.Empty;

    internal void SetProcedure(IEnumerable<string> steps)
    {
        var clean = steps.Select(s => s?.Trim() ?? string.Empty).Where(s => s.Length > 0).ToList();
        if (clean.Count > 20)
        {
            throw new DomainException("A procedure can have at most 20 steps.");
        }

        if (clean.Exists(s => s.Length > 500))
        {
            throw new DomainException("Each procedure step must be at most 500 characters.");
        }

        _procedure = clean;
    }

    internal void Arrange(RoomLayout layout)
    {
        foreach (var id in layout.Desks.Keys.Where(id => !_desks.Exists(d => d.Id == id)))
        {
            throw new DomainException($"Room '{Name}' has no desk {id.Value}.");
        }

        Width = layout.Width;
        Depth = layout.Depth;
        _furniture = [.. layout.Furniture];
        _desks.ForEach(d => d.MoveTo(layout.Desks.GetValueOrDefault(d.Id)));
    }

    internal void ClearLayout()
    {
        Width = null;
        Depth = null;
        _furniture = [];
        _desks.ForEach(d => d.MoveTo(null));
    }

    internal Desk AddDesk(string name, AgentProfile agent)
    {
        var deskName = UniqueDeskName(name, null);
        var desk = new Desk(DeskId.New(), deskName, agent);
        _desks.Add(desk);
        return desk;
    }

    internal void RenameDesk(Desk desk, string name) => desk.Rename(UniqueDeskName(name, desk.Id));

    private string UniqueDeskName(string name, DeskId? self)
    {
        var deskName = Guard.NotBlank(name, "Desk name", Office.MaxNameLength);
        if (_desks.Exists(d => d.Id != self && string.Equals(d.Name, deskName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Room '{Name}' already has a desk named '{deskName}'.");
        }

        return deskName;
    }
}
