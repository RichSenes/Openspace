namespace Openspace.Domain.Offices;

public readonly record struct OfficeId(Guid Value)
{
    public static OfficeId New() => new(Guid.CreateVersion7());
}

public readonly record struct RoomId(Guid Value)
{
    public static RoomId New() => new(Guid.CreateVersion7());
}

public readonly record struct DeskId(Guid Value)
{
    public static DeskId New() => new(Guid.CreateVersion7());
}
