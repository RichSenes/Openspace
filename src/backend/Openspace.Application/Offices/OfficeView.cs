using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

public sealed record OfficeView(Guid Id, string Name, string? RepositoryPath, string Theme, IReadOnlyList<RoomView> Rooms)
{
    public static OfficeView From(Office office)
    {
        ArgumentNullException.ThrowIfNull(office);
        return new(office.Id.Value, office.Name, office.RepositoryPath, office.Theme, [.. office.Rooms.Select(RoomView.From)]);
    }
}

/// <param name="Width">Tiles from wall to wall; null while the room is laid out automatically, like <paramref name="Depth"/>.</param>
public sealed record RoomView(
    Guid Id,
    string Name,
    string Context,
    IReadOnlyList<string> Procedure,
    IReadOnlyList<DeskView> Desks,
    int? Width,
    int? Depth,
    IReadOnlyList<FurniturePiece> Furniture)
{
    public static RoomView From(Room room) =>
        new(room.Id.Value, room.Name, room.Context, [.. room.Procedure], [.. room.Desks.Select(DeskView.From)], room.Width, room.Depth, [.. room.Furniture]);
}

/// <param name="Spot">Where the desk stands in its room; null when it is placed automatically.</param>
public sealed record DeskView(Guid Id, string Name, string AgentName, string? Model, Guid? RoleId, Spot? Spot)
{
    public static DeskView From(Desk desk) =>
        new(desk.Id.Value, desk.Name, desk.Agent.AgentName, desk.Agent.Model, desk.RoleId?.Value, desk.Spot);
}
