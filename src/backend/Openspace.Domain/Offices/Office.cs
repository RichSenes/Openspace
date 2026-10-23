using Openspace.Domain.Common;
using Openspace.Domain.Roles;

namespace Openspace.Domain.Offices;

/// <summary>A project: a set of rooms, each with its desks.</summary>
public sealed class Office : AggregateRoot<OfficeId>
{
    internal const int MaxNameLength = 80;

    /// <summary>The mood offices start with.</summary>
    public const string DefaultTheme = "cozy";

    private readonly List<Room> _rooms = [];
    private List<FurniturePiece> _furniture = [];
    private List<FloorCut> _cuts = [];

    private Office(OfficeId id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public IReadOnlyList<Room> Rooms => _rooms;

    /// <summary>
    /// The mood the office is drawn with: floors, walls, desks and furniture. A short name
    /// the frontend knows; one it does not know is drawn with the default.
    /// </summary>
    public string Theme { get; private set; } = DefaultTheme;

    /// <summary>Tiles from the west to the east wall of the building; null while it is laid out automatically.</summary>
    public int? FloorWidth { get; private set; }

    /// <summary>Tiles from the north to the south wall of the building; null while it is laid out automatically.</summary>
    public int? FloorDepth { get; private set; }

    /// <summary>The furniture of the common floor, placed by hand.</summary>
    public IReadOnlyList<FurniturePiece> Furniture => _furniture;

    /// <summary>The pieces cut out of the building's rectangle, giving it its shape.</summary>
    public IReadOnlyList<FloorCut> Cuts => _cuts;

    /// <summary>Absolute path of the git repository agents work on; null until set.</summary>
    public string? RepositoryPath { get; private set; }

    public static Office Open(string name, DateTimeOffset now)
    {
        var office = new Office(OfficeId.New(), Guard.NotBlank(name, "Office name", MaxNameLength));
        office.Raise(new OfficeOpened(office.Id, office.Name, now));
        return office;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = Guard.NotBlank(name, "Office name", MaxNameLength);
        Raise(new OfficeRenamed(Id, Name, now));
    }

    public void ChangeTheme(string theme, DateTimeOffset now)
    {
        var name = Guard.NotBlank(theme, "Theme", 40);
        if (!name.All(char.IsAsciiLetterOrDigit))
        {
            throw new DomainException($"Theme '{name}' must be a single word of letters and digits.");
        }

        Theme = name;
        Raise(new OfficeThemeChanged(Id, name, now));
    }

    public void SetRepository(string path, DateTimeOffset now)
    {
        var trimmed = Guard.NotBlank(path, "Repository path", 1024);
        if (!System.IO.Path.IsPathFullyQualified(trimmed))
        {
            throw new DomainException($"Repository path '{trimmed}' must be absolute.");
        }

        RepositoryPath = trimmed;
        Raise(new OfficeRepositorySet(Id, trimmed, now));
    }

    public Room OpenRoom(string name, DateTimeOffset now)
    {
        var roomName = Guard.NotBlank(name, "Room name", MaxNameLength);
        if (_rooms.Exists(r => string.Equals(r.Name, roomName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Office '{Name}' already has a room named '{roomName}'.");
        }

        var room = new Room(RoomId.New(), roomName);
        _rooms.Add(room);
        Raise(new RoomOpened(Id, room.Id, room.Name, now));
        return room;
    }

    public void SetRoomProcedure(RoomId roomId, IEnumerable<string> steps, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(steps);
        FindRoom(roomId).SetProcedure(steps);
        Raise(new RoomProcedureSet(Id, roomId, now));
    }

    /// <summary>Gives a room a size, places its desks and furnishes it. On a floor plan it must still fit.</summary>
    public void ArrangeRoom(RoomId roomId, RoomLayout layout, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var room = FindRoom(roomId);
        if (room.Left is { } left && room.Top is { } top)
        {
            var box = new PlannedRoom(roomId, left, top, layout).Box;
            if (box.Right > FloorWidth || box.Bottom > FloorDepth
                || _rooms.Exists(r => r != room && r.Box is { } other && FloorPlan.Overlap(box, other))
                || _cuts.Exists(c => FloorPlan.Overlap(box, c.Box)))
            {
                throw new DomainException($"Room '{room.Name}' no longer fits where it stands.");
            }
        }

        room.Arrange(layout);
        Raise(new RoomArranged(Id, roomId, now));
    }

    /// <summary>Lets the room be laid out automatically again, without its furniture; it leaves the floor plan.</summary>
    public void ClearRoomLayout(RoomId roomId, DateTimeOffset now)
    {
        var room = FindRoom(roomId);
        room.ClearLayout();
        room.PlaceAt(null, null);
        Raise(new RoomArranged(Id, roomId, now));
    }

    /// <summary>
    /// Draws the building by hand: its size, where each room stands and how it is arranged,
    /// and the furniture of the common floor. Rooms the plan leaves out wait for a place.
    /// </summary>
    public void PlanFloor(FloorPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var planned in plan.Rooms)
        {
            FindRoom(planned.Id);
        }

        FloorWidth = plan.Width;
        FloorDepth = plan.Depth;
        _furniture = [.. plan.Furniture];
        _cuts = [.. plan.Cuts];
        foreach (var room in _rooms)
        {
            var planned = plan.Rooms.FirstOrDefault(r => r.Id == room.Id);
            if (planned is null)
            {
                room.PlaceAt(null, null);
                continue;
            }

            room.Arrange(planned.Layout);
            room.PlaceAt(planned.Left, planned.Top);
        }

        Raise(new FloorPlanned(Id, now));
    }

    /// <summary>Goes back to the automatic building: every room and the common floor laid out by Openspace.</summary>
    public void ClearFloorPlan(DateTimeOffset now)
    {
        FloorWidth = null;
        FloorDepth = null;
        _furniture = [];
        _cuts = [];
        foreach (var room in _rooms)
        {
            room.ClearLayout();
            room.PlaceAt(null, null);
        }

        Raise(new FloorPlanned(Id, now));
    }

    public void UpdateRoomContext(RoomId roomId, string? context, DateTimeOffset now)
    {
        FindRoom(roomId).UpdateContext(context);
        Raise(new RoomContextUpdated(Id, roomId, now));
    }

    /// <param name="roleId">Must name an existing role; checked by the application, which sees all roles.</param>
    public Desk AddDesk(RoomId roomId, string name, AgentProfile agent, DateTimeOffset now, RoleId? roleId = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var desk = FindRoom(roomId).AddDesk(name, agent);
        desk.GiveRole(roleId);
        Raise(new DeskAdded(Id, roomId, desk.Id, agent, now));
        return desk;
    }

    public void ReassignDesk(DeskId deskId, AgentProfile agent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(agent);
        FindDesk(deskId).Reassign(agent);
        Raise(new DeskReassigned(Id, deskId, agent, now));
    }

    public void RenameDesk(DeskId deskId, string name, DateTimeOffset now)
    {
        var (room, desk) = Locate(deskId);
        room.RenameDesk(desk, name);
        Raise(new DeskRenamed(Id, deskId, desk.Name, now));
    }

    public void SetDeskRole(DeskId deskId, RoleId? roleId, DateTimeOffset now)
    {
        FindDesk(deskId).GiveRole(roleId);
        Raise(new DeskRoleChanged(Id, deskId, roleId, now));
    }

    public bool UsesRole(RoleId roleId) => _rooms.Exists(r => r.Desks.Any(d => d.RoleId == roleId));

    public bool HasDesk(DeskId deskId) => _rooms.Exists(r => r.Desks.Any(d => d.Id == deskId));

    /// <summary>The desk and the room it sits in.</summary>
    public (Room Room, Desk Desk) Locate(DeskId deskId)
    {
        foreach (var room in _rooms)
        {
            var desk = room.Desks.FirstOrDefault(d => d.Id == deskId);
            if (desk is not null)
            {
                return (room, desk);
            }
        }

        throw new DomainException($"Office '{Name}' has no desk {deskId.Value}.");
    }

    private Room FindRoom(RoomId roomId) =>
        _rooms.Find(r => r.Id == roomId)
        ?? throw new DomainException($"Office '{Name}' has no room {roomId.Value}.");

    private Desk FindDesk(DeskId deskId) => Locate(deskId).Desk;
}
