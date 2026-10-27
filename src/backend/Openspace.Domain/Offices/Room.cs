using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

public enum RoomKind
{
    /// <summary>Desks whose agents take tasks.</summary>
    Work,

    /// <summary>The office's routines, each at a place of its own: no desks.</summary>
    Routines,
}

/// <summary>An area of competence inside an office, with its own desks and context.</summary>
public sealed class Room
{
    private readonly List<Desk> _desks = [];
    private List<string> _procedure = [];
    private List<FurniturePiece> _furniture = [];
    private List<int> _windows = [];

    internal Room(RoomId id, string name, RoomKind kind = RoomKind.Work)
    {
        Id = id;
        Name = name;
        Kind = kind;
    }

    public RoomId Id { get; }

    public string Name { get; }

    public RoomKind Kind { get; private set; }

    /// <summary>Conventions, paths, do and don't: injected into every agent working here.</summary>
    public string Context { get; private set; } = string.Empty;

    /// <summary>
    /// A command telling whether the room's code still works, such as <c>dotnet test</c>: run on
    /// a branch after work was merged into it. Null when the room has none.
    /// </summary>
    public string? Check { get; private set; }

    /// <summary>The room's own look, one of the office themes; null to follow the office.</summary>
    public string? Mood { get; private set; }

    public IReadOnlyList<Desk> Desks => _desks;

    /// <summary>Tiles from wall to wall; null while the room is laid out automatically.</summary>
    public int? Width { get; private set; }

    /// <summary>Tiles of floor in front of the back wall; null while the room is laid out automatically.</summary>
    public int? Depth { get; private set; }

    /// <summary>Furniture placed by hand; empty while the room is laid out automatically.</summary>
    public IReadOnlyList<FurniturePiece> Furniture => _furniture;

    /// <summary>Where the door is; null for the middle of the front wall.</summary>
    public DoorPlacement? Door { get; private set; }

    /// <summary>Windows in the back wall, by the pixel where each one starts.</summary>
    public IReadOnlyList<int> Windows => _windows;

    /// <summary>The room's back left corner in tiles from the building's; null while the office is laid out automatically.</summary>
    public int? Left { get; private set; }

    /// <inheritdoc cref="Left"/>
    public int? Top { get; private set; }

    /// <summary>The tiles the room takes on a floor plan, when it has a place and a size.</summary>
    internal (int Left, int Top, int Right, int Bottom)? Box =>
        Left is { } left && Top is { } top && Width is { } width && Depth is { } depth
            ? (left, top, left + width, top + depth + RoomLayout.WallTiles)
            : null;

    /// <summary>Steps every task of this room must follow; checked before its work goes to review.</summary>
    public IReadOnlyList<string> Procedure => _procedure;

    internal void UpdateContext(string? context) => Context = context?.Trim() ?? string.Empty;

    internal void ChangeMood(string? mood) => Mood = mood;

    internal void SetCheck(string? command) => Check = command;

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
        Door = layout.Door;
        _windows = [.. layout.Windows];
        _desks.ForEach(d => d.MoveTo(layout.Desks.GetValueOrDefault(d.Id)));
    }

    internal void ClearLayout()
    {
        Width = null;
        Depth = null;
        _furniture = [];
        Door = null;
        _windows = [];
        _desks.ForEach(d => d.MoveTo(null));
    }

    internal void PlaceAt(int? left, int? top)
    {
        Left = left;
        Top = top;
    }

    internal Desk AddDesk(string name, AgentProfile agent)
    {
        if (Kind == RoomKind.Routines)
        {
            throw new DomainException($"'{Name}' holds the office's routines: desks go in the other rooms.");
        }

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
