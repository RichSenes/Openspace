using Openspace.Domain.Common;
using Openspace.Domain.Roles;

namespace Openspace.Domain.Offices;

/// <summary>A project: a set of rooms, each with its desks.</summary>
public sealed class Office : AggregateRoot<OfficeId>
{
    internal const int MaxNameLength = 80;

    private readonly List<Room> _rooms = [];

    private Office(OfficeId id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public IReadOnlyList<Room> Rooms => _rooms;

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
