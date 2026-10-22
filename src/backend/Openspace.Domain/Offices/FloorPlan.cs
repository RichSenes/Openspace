using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>A room placed on the floor: its back left corner, in tiles from the building's, and how it is arranged.</summary>
public sealed record PlannedRoom(RoomId Id, int Left, int Top, RoomLayout Layout)
{
    /// <summary>The tiles the room takes, its back wall included.</summary>
    internal (int Left, int Top, int Right, int Bottom) Box => (Left, Top, Left + Layout.Width, Top + Layout.Depth + RoomLayout.WallTiles);
}

/// <summary>
/// The building of an office, drawn by hand: its size in tiles, where each room stands and
/// the furniture of the common floor, which is everything that is not a room.
/// </summary>
public sealed record FloorPlan
{
    public const int MaxFurniture = 120;

    private FloorPlan(int width, int depth, IReadOnlyList<FurniturePiece> furniture, IReadOnlyList<PlannedRoom> rooms)
    {
        Width = width;
        Depth = depth;
        Furniture = furniture;
        Rooms = rooms;
    }

    public int Width { get; }

    public int Depth { get; }

    public IReadOnlyList<FurniturePiece> Furniture { get; }

    public IReadOnlyList<PlannedRoom> Rooms { get; }

    public static FloorPlan Create(int width, int depth, IEnumerable<FurniturePiece> furniture, IEnumerable<PlannedRoom> rooms)
    {
        ArgumentNullException.ThrowIfNull(furniture);
        ArgumentNullException.ThrowIfNull(rooms);
        if (width is < 6 or > 120 || depth is < 5 or > 80)
        {
            throw new DomainException("A building must be 6 to 120 tiles wide and 5 to 80 tiles deep.");
        }

        var planned = rooms.Select(r => r ?? throw new DomainException("A planned room cannot be empty.")).ToList();
        if (planned.DistinctBy(r => r.Id).Count() != planned.Count)
        {
            throw new DomainException("Each room can stand in one place only.");
        }

        foreach (var room in planned)
        {
            var (left, top, right, bottom) = room.Box;
            if (left < 0 || top < 0 || right > width || bottom > depth)
            {
                throw new DomainException("Every room must stand inside the building.");
            }
        }

        if (planned.SelectMany((a, i) => planned.Skip(i + 1).Select(b => (a, b))).Any(p => Overlap(p.a.Box, p.b.Box)))
        {
            throw new DomainException("Rooms cannot overlap.");
        }

        var clean = Furnishing.Check(furniture, width * RoomLayout.TileSize, depth * RoomLayout.TileSize, MaxFurniture);
        return new FloorPlan(width, depth, clean, planned);
    }

    internal static bool Overlap((int Left, int Top, int Right, int Bottom) a, (int Left, int Top, int Right, int Bottom) b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
