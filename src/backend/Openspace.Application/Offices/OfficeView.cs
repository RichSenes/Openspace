using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

public sealed record OfficeView(Guid Id, string Name, string? RepositoryPath, IReadOnlyList<RoomView> Rooms)
{
    public static OfficeView From(Office office)
    {
        ArgumentNullException.ThrowIfNull(office);
        return new(office.Id.Value, office.Name, office.RepositoryPath, [.. office.Rooms.Select(RoomView.From)]);
    }
}

public sealed record RoomView(Guid Id, string Name, string Context, IReadOnlyList<string> Procedure, IReadOnlyList<DeskView> Desks)
{
    public static RoomView From(Room room) =>
        new(room.Id.Value, room.Name, room.Context, [.. room.Procedure], [.. room.Desks.Select(DeskView.From)]);
}

public sealed record DeskView(Guid Id, string Name, string AgentName, string? Model, Guid? RoleId)
{
    public static DeskView From(Desk desk) => new(desk.Id.Value, desk.Name, desk.Agent.AgentName, desk.Agent.Model, desk.RoleId?.Value);
}
