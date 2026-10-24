using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

/// <param name="FloorWidth">The building's size in tiles, like <paramref name="FloorDepth"/>; null while it is laid out automatically.</param>
/// <param name="Furniture">The furniture of the common floor, in pixels from the building's corner.</param>
/// <param name="PrivatePaths">Folders no agent of the office may touch in any way.</param>
/// <param name="Cuts">The pieces cut out of the building's rectangle, in tiles from its corner.</param>
public sealed record OfficeView(
    Guid Id,
    string Name,
    string? RepositoryPath,
    string Theme,
    IReadOnlyList<RoomView> Rooms,
    int? FloorWidth,
    int? FloorDepth,
    IReadOnlyList<FurniturePiece> Furniture,
    IReadOnlyList<FloorCut> Cuts,
    IReadOnlyList<string> PrivatePaths)
{
    public static OfficeView From(Office office)
    {
        ArgumentNullException.ThrowIfNull(office);
        return new(
            office.Id.Value,
            office.Name,
            office.RepositoryPath,
            office.Theme,
            [.. office.Rooms.Select(RoomView.From)],
            office.FloorWidth,
            office.FloorDepth,
            [.. office.Furniture],
            [.. office.Cuts],
            [.. office.PrivatePaths]);
    }
}

/// <param name="Mood">The room's own theme; null when it follows the office's.</param>
/// <param name="Check">The command run on a branch after the room's work is merged into it.</param>
/// <param name="Width">Tiles from wall to wall; null while the room is laid out automatically, like <paramref name="Depth"/>.</param>
public sealed record RoomView(
    Guid Id,
    string Name,
    string Context,
    IReadOnlyList<string> Procedure,
    IReadOnlyList<DeskView> Desks,
    int? Width,
    int? Depth,
    IReadOnlyList<FurniturePiece> Furniture,
    DoorPlacement? Door,
    IReadOnlyList<int> Windows,
    int? Left,
    int? Top,
    string? Mood,
    string? Check)
{
    public static RoomView From(Room room) =>
        new(
            room.Id.Value,
            room.Name,
            room.Context,
            [.. room.Procedure],
            [.. room.Desks.Select(DeskView.From)],
            room.Width,
            room.Depth,
            [.. room.Furniture],
            room.Door,
            [.. room.Windows],
            room.Left,
            room.Top,
            room.Mood,
            room.Check);
}

/// <param name="Spot">Where the desk stands in its room; null when it is placed automatically.</param>
/// <param name="Look">How its agent looks, chosen by hand; null when picked from the desk's id.</param>
/// <param name="Engine">What runs its agent: OpenCode or Claude Code.</param>
public sealed record DeskView(Guid Id, string Name, string AgentName, string? Model, Guid? RoleId, Spot? Spot, DeskLook? Look, AgentEngine Engine)
{
    public static DeskView From(Desk desk) =>
        new(desk.Id.Value, desk.Name, desk.Agent.AgentName, desk.Agent.Model, desk.RoleId?.Value, desk.Spot, desk.Look, desk.Agent.Engine);
}
