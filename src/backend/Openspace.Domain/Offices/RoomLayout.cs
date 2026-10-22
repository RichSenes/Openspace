using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>Where something stands in a room, in pixels from the room's top left corner (a tile is 16 pixels).</summary>
public sealed record Spot(int X, int Y);

/// <summary>A piece of furniture placed in a room: a prop the frontend knows by name.</summary>
public sealed record FurniturePiece(string Name, int X, int Y);

/// <summary>
/// How a room is arranged: its size in tiles, where its desks stand and its furniture.
/// Desks it leaves out are placed automatically.
/// </summary>
public sealed record RoomLayout
{
    public const int TileSize = 16;
    public const int WallTiles = 2;
    public const int MaxFurniture = 60;

    private RoomLayout(int width, int depth, IReadOnlyDictionary<DeskId, Spot> desks, IReadOnlyList<FurniturePiece> furniture)
    {
        Width = width;
        Depth = depth;
        Desks = desks;
        Furniture = furniture;
    }

    /// <summary>Tiles from wall to wall.</summary>
    public int Width { get; }

    /// <summary>Tiles of floor in front of the back wall.</summary>
    public int Depth { get; }

    public IReadOnlyDictionary<DeskId, Spot> Desks { get; }

    public IReadOnlyList<FurniturePiece> Furniture { get; }

    public static RoomLayout Create(int width, int depth, IReadOnlyDictionary<DeskId, Spot> desks, IEnumerable<FurniturePiece> furniture)
    {
        ArgumentNullException.ThrowIfNull(desks);
        ArgumentNullException.ThrowIfNull(furniture);
        if (width is < 4 or > 40 || depth is < 2 or > 30)
        {
            throw new DomainException("A room must be 4 to 40 tiles wide and 2 to 30 tiles deep.");
        }

        var pixels = (Width: width * TileSize, Height: (depth + WallTiles) * TileSize);
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < pixels.Width && y < pixels.Height;

        if (desks.Values.Any(s => s is null || !Inside(s.X, s.Y)))
        {
            throw new DomainException("Every desk must stand inside the room.");
        }

        var pieces = furniture.Select(f => f ?? throw new DomainException("A piece of furniture cannot be empty.")).ToList();
        if (pieces.Count > MaxFurniture)
        {
            throw new DomainException($"A room can hold at most {MaxFurniture} pieces of furniture.");
        }

        var clean = pieces.Select(p =>
        {
            var name = Guard.NotBlank(p.Name, "Furniture name", 40);
            if (!name.All(char.IsAsciiLetterOrDigit))
            {
                throw new DomainException($"Furniture name '{name}' must be a single word of letters and digits.");
            }

            return Inside(p.X, p.Y) ? p with { Name = name } : throw new DomainException($"The {name} must stand inside the room.");
        }).ToList();

        return new RoomLayout(width, depth, new Dictionary<DeskId, Spot>(desks), clean);
    }
}
