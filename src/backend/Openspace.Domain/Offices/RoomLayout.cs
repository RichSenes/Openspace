using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>Where something stands in a room, in pixels from the room's top left corner (a tile is 16 pixels).</summary>
public sealed record Spot(int X, int Y);

/// <summary>A piece of furniture placed in a room: a prop the frontend knows by name.</summary>
public sealed record FurniturePiece(string Name, int X, int Y);

/// <summary>The walls of a room, as seen from above with the back wall at the top.</summary>
public enum Wall
{
    Front,
    Back,
    Left,
    Right,
}

/// <summary>
/// A room's door: the wall it is in and how many tiles from the start of that wall it opens,
/// from the left for the front and back walls, from the back for the side walls.
/// </summary>
public sealed record DoorPlacement(Wall Wall, int Offset)
{
    public const int Tiles = 2;
}

/// <summary>Furniture pieces checked one by one: a single word for a name, inside a box of pixels.</summary>
internal static class Furnishing
{
    public static List<FurniturePiece> Check(IEnumerable<FurniturePiece> furniture, int width, int height, int max)
    {
        var pieces = furniture.Select(f => f ?? throw new DomainException("A piece of furniture cannot be empty.")).ToList();
        if (pieces.Count > max)
        {
            throw new DomainException($"A space can hold at most {max} pieces of furniture.");
        }

        return pieces.Select(p =>
        {
            var name = Guard.NotBlank(p.Name, "Furniture name", 40);
            if (!name.All(char.IsAsciiLetterOrDigit))
            {
                throw new DomainException($"Furniture name '{name}' must be a single word of letters and digits.");
            }

            return p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height
                ? p with { Name = name }
                : throw new DomainException($"The {name} must stand inside.");
        }).ToList();
    }
}

/// <summary>
/// How a room is arranged: its size in tiles, where its desks stand, its furniture, its door
/// and its windows. Desks it leaves out are placed automatically.
/// </summary>
public sealed record RoomLayout
{
    public const int TileSize = 16;
    public const int WallTiles = 2;
    public const int MaxFurniture = 60;
    public const int MaxWindows = 12;

    private RoomLayout(
        int width, int depth, IReadOnlyDictionary<DeskId, Spot> desks, IReadOnlyList<FurniturePiece> furniture, DoorPlacement? door, IReadOnlyList<int> windows)
    {
        Width = width;
        Depth = depth;
        Desks = desks;
        Furniture = furniture;
        Door = door;
        Windows = windows;
    }

    /// <summary>Tiles from wall to wall.</summary>
    public int Width { get; }

    /// <summary>Tiles of floor in front of the back wall.</summary>
    public int Depth { get; }

    public IReadOnlyDictionary<DeskId, Spot> Desks { get; }

    public IReadOnlyList<FurniturePiece> Furniture { get; }

    /// <summary>Where the door is; null for the middle of the front wall.</summary>
    public DoorPlacement? Door { get; }

    /// <summary>Windows in the back wall, by the pixel where each one starts from the left.</summary>
    public IReadOnlyList<int> Windows { get; }

    public static RoomLayout Create(
        int width,
        int depth,
        IReadOnlyDictionary<DeskId, Spot> desks,
        IEnumerable<FurniturePiece> furniture,
        DoorPlacement? door = null,
        IEnumerable<int>? windows = null)
    {
        ArgumentNullException.ThrowIfNull(desks);
        ArgumentNullException.ThrowIfNull(furniture);
        if (width is < 4 or > 40 || depth is < 2 or > 30)
        {
            throw new DomainException("A room must be 4 to 40 tiles wide and 2 to 30 tiles deep.");
        }

        var pixels = (Width: width * TileSize, Height: (depth + WallTiles) * TileSize);
        if (desks.Values.Any(s => s is null || s.X < 0 || s.Y < 0 || s.X >= pixels.Width || s.Y >= pixels.Height))
        {
            throw new DomainException("Every desk must stand inside the room.");
        }

        var clean = Furnishing.Check(furniture, pixels.Width, pixels.Height, MaxFurniture);

        var wallLength = door?.Wall is Wall.Left or Wall.Right ? depth : width;
        if (door is not null && (!Enum.IsDefined(door.Wall) || door.Offset < 0 || door.Offset + DoorPlacement.Tiles > wallLength))
        {
            throw new DomainException("The door must open within its wall.");
        }

        var panes = (windows ?? []).ToList();
        if (panes.Count > MaxWindows || panes.Exists(x => x < 0 || x >= pixels.Width))
        {
            throw new DomainException($"A room can have at most {MaxWindows} windows, all in its back wall.");
        }

        return new RoomLayout(width, depth, new Dictionary<DeskId, Spot>(desks), clean, door, panes);
    }
}
