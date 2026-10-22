using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Offices;

public class RoomLayoutTests
{
    private static RoomLayout Layout(int width, int depth, Dictionary<DeskId, Spot>? desks = null, params FurniturePiece[] furniture) =>
        RoomLayout.Create(width, depth, desks ?? [], furniture);

    [Fact]
    public void Arranging_a_room_sizes_it_places_its_desks_and_furnishes_it()
    {
        var (office, desk) = ECorpWithDesk();
        var room = office.Rooms[0];

        office.ArrangeRoom(room.Id, Layout(10, 4, new() { [desk.Id] = new Spot(40, 20) }, new FurniturePiece(" plant ", 130, 70)), Now);

        Assert.Equal((10, 4), (room.Width, room.Depth));
        Assert.Equal(new Spot(40, 20), desk.Spot);
        Assert.Equal(new FurniturePiece("plant", 130, 70), Assert.Single(room.Furniture));
        Assert.IsType<RoomArranged>(office.DomainEvents[^1]);
    }

    [Fact]
    public void Desks_left_out_of_a_layout_go_back_to_automatic()
    {
        var (office, desk) = ECorpWithDesk();
        var room = office.Rooms[0];
        office.ArrangeRoom(room.Id, Layout(10, 4, new() { [desk.Id] = new Spot(40, 20) }), Now);

        office.ArrangeRoom(room.Id, Layout(10, 4), Now);

        Assert.Null(desk.Spot);
    }

    [Fact]
    public void Clearing_a_layout_goes_back_to_the_automatic_room()
    {
        var (office, desk) = ECorpWithDesk();
        var room = office.Rooms[0];
        office.ArrangeRoom(room.Id, Layout(10, 4, new() { [desk.Id] = new Spot(40, 20) }, new FurniturePiece("rug", 16, 40)), Now);

        office.ClearRoomLayout(room.Id, Now);

        Assert.Equal(((int?)null, (int?)null, (Spot?)null), (room.Width, room.Depth, desk.Spot));
        Assert.Empty(room.Furniture);
    }

    [Fact]
    public void A_room_cannot_place_a_desk_of_another_room()
    {
        var (office, desk) = ECorpWithDesk();
        var other = office.OpenRoom("Identity", Now);

        Assert.Throws<DomainException>(() => office.ArrangeRoom(other.Id, Layout(10, 4, new() { [desk.Id] = new Spot(0, 0) }), Now));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(41, 4)]
    [InlineData(10, 1)]
    [InlineData(10, 31)]
    public void A_room_has_a_sensible_size(int width, int depth)
    {
        Assert.Throws<DomainException>(() => Layout(width, depth));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(160, 0)]
    [InlineData(0, 96)]
    public void Everything_stands_inside_the_room(int x, int y)
    {
        // 10 tiles by 4, plus the 2 tiles of wall: 160 by 96 pixels.
        Assert.Throws<DomainException>(() => Layout(10, 4, new() { [DeskId.New()] = new Spot(x, y) }));
        Assert.Throws<DomainException>(() => Layout(10, 4, null, new FurniturePiece("plant", x, y)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("../plant")]
    public void Furniture_is_named_with_a_single_word(string name)
    {
        Assert.Throws<DomainException>(() => Layout(10, 4, null, new FurniturePiece(name, 0, 0)));
    }

    [Fact]
    public void A_room_holds_a_reasonable_amount_of_furniture()
    {
        var pieces = Enumerable.Range(0, RoomLayout.MaxFurniture + 1).Select(_ => new FurniturePiece("plant", 0, 40)).ToArray();

        Assert.Throws<DomainException>(() => Layout(10, 4, null, pieces));
    }
}
