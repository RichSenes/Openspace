using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Offices;

public class FloorPlanTests
{
    private static RoomLayout Room(int width = 8, int depth = 4, DoorPlacement? door = null, params int[] windows) =>
        RoomLayout.Create(width, depth, new Dictionary<DeskId, Spot>(), [], door, windows);

    private static (Office Office, Room Web, Room Identity) Allsafe()
    {
        var office = Office.Open("Allsafe", Now);
        return (office, office.OpenRoom("Web", Now), office.OpenRoom("Identity", Now));
    }

    [Fact]
    public void Planning_the_floor_sizes_the_building_and_places_the_rooms()
    {
        var (office, web, identity) = Allsafe();

        office.PlanFloor(FloorPlan.Create(20, 12, [new("vendingMachine", 200, 150)], [
            new PlannedRoom(web.Id, 0, 0, Room(8, 4, new DoorPlacement(Wall.Right, 1), 16, 64)),
            new PlannedRoom(identity.Id, 8, 0, Room(6, 4)),
        ]), Now);

        Assert.Equal((20, 12), (office.FloorWidth, office.FloorDepth));
        Assert.Equal("vendingMachine", Assert.Single(office.Furniture).Name);
        Assert.Equal((0, 0, 8), (web.Left, web.Top, web.Width));
        Assert.Equal(new DoorPlacement(Wall.Right, 1), web.Door);
        Assert.Equal([16, 64], web.Windows);
        Assert.Equal((8, 0), (identity.Left, identity.Top));
        Assert.IsType<FloorPlanned>(office.DomainEvents[^1]);
    }

    [Fact]
    public void Rooms_left_out_of_a_plan_wait_for_a_place()
    {
        var (office, web, identity) = Allsafe();
        office.PlanFloor(FloorPlan.Create(20, 12, [], [new PlannedRoom(web.Id, 0, 0, Room()), new PlannedRoom(identity.Id, 8, 0, Room())]), Now);

        office.PlanFloor(FloorPlan.Create(20, 12, [], [new PlannedRoom(web.Id, 2, 2, Room())]), Now);

        Assert.Equal((2, 2), (web.Left, web.Top));
        Assert.Equal(((int?)null, (int?)null), (identity.Left, identity.Top));
    }

    [Fact]
    public void Clearing_the_plan_goes_back_to_the_automatic_building()
    {
        var (office, web, _) = Allsafe();
        office.PlanFloor(FloorPlan.Create(20, 12, [new("plant", 0, 0)], [new PlannedRoom(web.Id, 0, 0, Room(windows: 16))]), Now);

        office.ClearFloorPlan(Now);

        Assert.Equal(((int?)null, (int?)null, (int?)null, (int?)null), (office.FloorWidth, office.FloorDepth, web.Left, web.Width));
        Assert.Empty(office.Furniture);
        Assert.Empty(web.Windows);
    }

    [Fact]
    public void Rooms_stand_inside_the_building_without_overlapping()
    {
        var (_, web, identity) = Allsafe();

        Assert.Throws<DomainException>(() => FloorPlan.Create(10, 10, [], [new PlannedRoom(web.Id, 4, 0, Room(8))]));
        Assert.Throws<DomainException>(() => FloorPlan.Create(10, 10, [], [new PlannedRoom(web.Id, 0, 5, Room(8, 4))]));
        Assert.Throws<DomainException>(() => FloorPlan.Create(20, 10, [], [new PlannedRoom(web.Id, 0, 0, Room()), new PlannedRoom(identity.Id, 7, 2, Room())]));
        FloorPlan.Create(20, 10, [], [new PlannedRoom(web.Id, 0, 0, Room()), new PlannedRoom(identity.Id, 8, 0, Room())]);
    }

    [Fact]
    public void A_room_that_grows_into_another_on_the_plan_is_refused()
    {
        var (office, web, identity) = Allsafe();
        office.PlanFloor(FloorPlan.Create(20, 12, [], [new PlannedRoom(web.Id, 0, 0, Room()), new PlannedRoom(identity.Id, 8, 0, Room())]), Now);

        Assert.Throws<DomainException>(() => office.ArrangeRoom(web.Id, Room(10), Now));
        office.ArrangeRoom(web.Id, Room(8, 8), Now);
    }

    [Fact]
    public void A_plan_names_rooms_of_the_office()
    {
        var (office, _, _) = Allsafe();
        var stranger = ECorp().OpenRoom("Elsewhere", Now);

        Assert.Throws<DomainException>(() => office.PlanFloor(FloorPlan.Create(20, 12, [], [new PlannedRoom(stranger.Id, 0, 0, Room())]), Now));
    }

    [Theory]
    [InlineData(Wall.Front, 7)]
    [InlineData(Wall.Left, 3)]
    [InlineData(Wall.Back, -1)]
    public void A_door_opens_within_its_wall(Wall wall, int offset)
    {
        // 8 tiles wide, 4 deep: a two-tile door fits up to 6 along the front, 2 along a side.
        Assert.Throws<DomainException>(() => Room(8, 4, new DoorPlacement(wall, offset)));
    }

    [Fact]
    public void Windows_are_in_the_back_wall()
    {
        Assert.Throws<DomainException>(() => Room(8, 4, null, 128));
        Assert.Throws<DomainException>(() => Room(8, 4, null, [.. Enumerable.Repeat(0, RoomLayout.MaxWindows + 1)]));
    }
}
