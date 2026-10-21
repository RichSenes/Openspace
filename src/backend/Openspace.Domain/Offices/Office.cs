using Openspace.Domain.Common;

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

    public static Office Open(string name) =>
        new(OfficeId.New(), Guard.NotBlank(name, "Office name", MaxNameLength));

    public void Rename(string name) => Name = Guard.NotBlank(name, "Office name", MaxNameLength);

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

    public void UpdateRoomContext(RoomId roomId, string? context) => FindRoom(roomId).UpdateContext(context);

    public Desk AddDesk(RoomId roomId, string name, AgentProfile agent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var desk = FindRoom(roomId).AddDesk(name, agent);
        Raise(new DeskAdded(Id, roomId, desk.Id, agent, now));
        return desk;
    }

    public void ReassignDesk(DeskId deskId, AgentProfile agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        FindDesk(deskId).Reassign(agent);
    }

    public bool HasDesk(DeskId deskId) => _rooms.Exists(r => r.Desks.Any(d => d.Id == deskId));

    private Room FindRoom(RoomId roomId) =>
        _rooms.Find(r => r.Id == roomId)
        ?? throw new DomainException($"Office '{Name}' has no room {roomId.Value}.");

    private Desk FindDesk(DeskId deskId) =>
        _rooms.SelectMany(r => r.Desks).FirstOrDefault(d => d.Id == deskId)
        ?? throw new DomainException($"Office '{Name}' has no desk {deskId.Value}.");
}
