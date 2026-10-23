using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>A room placed on the floor: its back left corner, in tiles from the building's, and how it is arranged.</summary>
public sealed record PlannedRoom(RoomId Id, int Left, int Top, RoomLayout Layout)
{
    /// <summary>The tiles the room takes, its back wall included.</summary>
    internal (int Left, int Top, int Right, int Bottom) Box => (Left, Top, Left + Layout.Width, Top + Layout.Depth + RoomLayout.WallTiles);
}

/// <summary>
/// A piece of the building's rectangle left out of it, in tiles from the building's corner:
/// cut at an edge it gives the building an L, T or U shape, inside it a courtyard.
/// </summary>
public sealed record FloorCut(int Left, int Top, int Width, int Height)
{
    internal (int Left, int Top, int Right, int Bottom) Box => (Left, Top, Left + Width, Top + Height);
}

/// <summary>
/// The building of an office, drawn by hand: its size in tiles, where each room stands and
/// the furniture of the common floor, which is everything that is not a room, and the pieces
/// cut out of its rectangle to give it another shape.
/// </summary>
public sealed record FloorPlan
{
    public const int MaxFurniture = 120;
    public const int MaxCuts = 12;

    private FloorPlan(int width, int depth, IReadOnlyList<FurniturePiece> furniture, IReadOnlyList<PlannedRoom> rooms, IReadOnlyList<FloorCut> cuts)
    {
        Cuts = cuts;
        Width = width;
        Depth = depth;
        Furniture = furniture;
        Rooms = rooms;
    }

    public int Width { get; }

    public int Depth { get; }

    public IReadOnlyList<FurniturePiece> Furniture { get; }

    public IReadOnlyList<PlannedRoom> Rooms { get; }

    public IReadOnlyList<FloorCut> Cuts { get; }

    public static FloorPlan Create(
        int width, int depth, IEnumerable<FurniturePiece> furniture, IEnumerable<PlannedRoom> rooms, IEnumerable<FloorCut>? cuts = null)
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

        var shape = Shape(width, depth, cuts ?? []);
        if (planned.Exists(r => shape.Exists(c => Overlap(r.Box, c.Box))))
        {
            throw new DomainException("Rooms cannot stand where the building is cut away.");
        }

        var clean = Furnishing.Check(furniture, width * RoomLayout.TileSize, depth * RoomLayout.TileSize, MaxFurniture);
        if (clean.Exists(p => shape.Exists(c => Inside(c.Box, p.X / RoomLayout.TileSize, p.Y / RoomLayout.TileSize))))
        {
            throw new DomainException("Furniture cannot stand where the building is cut away.");
        }

        return new FloorPlan(width, depth, clean, planned, shape);
    }

    /// <summary>The cuts checked: inside the building, apart from each other, and leaving some of it standing.</summary>
    private static List<FloorCut> Shape(int width, int depth, IEnumerable<FloorCut> cuts)
    {
        var shape = cuts.Select(c => c ?? throw new DomainException("A cut cannot be empty.")).ToList();
        if (shape.Count > MaxCuts)
        {
            throw new DomainException($"A building can have at most {MaxCuts} cuts.");
        }

        if (shape.Exists(c => c.Width < 1 || c.Height < 1 || c.Left < 0 || c.Top < 0 || c.Left + c.Width > width || c.Top + c.Height > depth))
        {
            throw new DomainException("Every cut must be at least a tile, inside the building.");
        }

        if (shape.SelectMany((a, i) => shape.Skip(i + 1).Select(b => (a, b))).Any(p => Overlap(p.a.Box, p.b.Box)))
        {
            throw new DomainException("Cuts cannot overlap.");
        }

        if (shape.Sum(c => c.Width * c.Height) >= width * depth)
        {
            throw new DomainException("Cuts cannot take the whole building.");
        }

        return shape;
    }

    private static bool Inside((int Left, int Top, int Right, int Bottom) box, int x, int y) =>
        x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom;

    internal static bool Overlap((int Left, int Top, int Right, int Bottom) a, (int Left, int Top, int Right, int Bottom) b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
