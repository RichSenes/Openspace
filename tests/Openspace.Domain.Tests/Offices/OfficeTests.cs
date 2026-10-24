using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Offices;

public class OfficeTests
{
    [Fact]
    public void Opening_an_office_trims_its_name()
    {
        Assert.Equal("Allsafe", Office.Open("  Allsafe ", Now).Name);
    }

    [Fact]
    public void Opening_an_office_raises_an_event()
    {
        var office = Office.Open("Allsafe", Now);

        var opened = Assert.IsType<OfficeOpened>(Assert.Single(office.DomainEvents));
        Assert.Equal((office.Id, "Allsafe"), (opened.OfficeId, opened.Name));
    }

    [Fact]
    public void Renaming_an_office_raises_an_event()
    {
        var office = ECorp();

        office.Rename(" Evil Corp ", Now);

        Assert.Equal("Evil Corp", office.Name);
        Assert.IsType<OfficeRenamed>(office.DomainEvents[^1]);
    }

    [Fact]
    public void An_office_starts_cozy_and_can_change_its_theme()
    {
        var office = ECorp();
        Assert.Equal(Office.DefaultTheme, office.Theme);

        office.ChangeTheme(" backRoomArcade ", Now);

        Assert.Equal("backRoomArcade", office.Theme);
        Assert.IsType<OfficeThemeChanged>(office.DomainEvents[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("../etc")]
    public void A_theme_is_a_single_word(string theme)
    {
        Assert.Throws<DomainException>(() => ECorp().ChangeTheme(theme, Now));
    }

    [Fact]
    public void A_room_can_have_its_own_mood_and_go_back_to_the_office_s()
    {
        var office = ECorp();
        var room = office.OpenRoom("Backend", Now);

        office.ChangeRoomMood(room.Id, " darkFocus ", Now);
        Assert.Equal("darkFocus", room.Mood);
        Assert.IsType<RoomMoodChanged>(office.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => office.ChangeRoomMood(room.Id, "dark focus", Now));

        office.ChangeRoomMood(room.Id, " ", Now);
        Assert.Null(room.Mood);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Office_needs_a_name(string name)
    {
        Assert.Throws<DomainException>(() => Office.Open(name, Now));
    }

    [Fact]
    public void Opening_a_room_adds_it_and_raises_an_event()
    {
        var office = ECorp();

        var room = office.OpenRoom("Identity", Now);

        Assert.Single(office.Rooms);
        var opened = Assert.IsType<RoomOpened>(office.DomainEvents[^1]);
        Assert.Equal(room.Id, opened.RoomId);
        Assert.Equal(Now, opened.OccurredAt);
    }

    [Fact]
    public void Room_names_are_unique_ignoring_case()
    {
        var office = ECorp();
        office.OpenRoom("Web", Now);

        Assert.Throws<DomainException>(() => office.OpenRoom(" web ", Now));
    }

    [Fact]
    public void Desks_are_added_to_their_room()
    {
        var office = ECorp();
        var web = office.OpenRoom("Web", Now);
        var identity = office.OpenRoom("Identity", Now);

        var desk = office.AddDesk(web.Id, "Frontend", AgentProfile.Create("frontend-dev"), Now);

        Assert.Same(desk, Assert.Single(web.Desks));
        Assert.Empty(identity.Desks);
        Assert.True(office.HasDesk(desk.Id));
        Assert.IsType<DeskAdded>(office.DomainEvents[^1]);
    }

    [Fact]
    public void Desk_names_are_unique_within_a_room_only()
    {
        var office = ECorp();
        var web = office.OpenRoom("Web", Now);
        var tests = office.OpenRoom("Tests", Now);
        office.AddDesk(web.Id, "Lead", AgentProfile.Create("web-lead"), Now);

        office.AddDesk(tests.Id, "Lead", AgentProfile.Create("test-lead"), Now);
        Assert.Throws<DomainException>(() =>
            office.AddDesk(web.Id, "LEAD", AgentProfile.Create("other"), Now));
    }

    [Fact]
    public void Adding_a_desk_to_an_unknown_room_fails()
    {
        var office = ECorp();

        Assert.Throws<DomainException>(() =>
            office.AddDesk(RoomId.New(), "Frontend", AgentProfile.Create("frontend-dev"), Now));
    }

    [Fact]
    public void A_desk_can_get_a_different_agent()
    {
        var (office, desk) = ECorpWithDesk();
        var replacement = AgentProfile.Create("frontend-dev", "local/bigger-model");

        office.ReassignDesk(desk.Id, replacement, Now);

        Assert.Equal(replacement, desk.Agent);
        Assert.IsType<DeskReassigned>(office.DomainEvents[^1]);
    }

    [Fact]
    public void Room_context_is_stored_trimmed()
    {
        var office = ECorp();
        var room = office.OpenRoom("Web", Now);

        office.UpdateRoomContext(room.Id, "  Never touch the production database.  ", Now);

        Assert.Equal("Never touch the production database.", room.Context);
        Assert.IsType<RoomContextUpdated>(office.DomainEvents[^1]);
    }

    [Fact]
    public void Repository_path_must_be_absolute()
    {
        var office = ECorp();

        office.SetRepository("/srv/repos/e-corp", Now);

        Assert.Equal("/srv/repos/e-corp", office.RepositoryPath);
        Assert.IsType<OfficeRepositorySet>(office.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => office.SetRepository("repos/e-corp", Now));
        Assert.Throws<DomainException>(() => office.SetRepository(" ", Now));
    }

    [Fact]
    public void Locating_a_desk_returns_its_room()
    {
        var (office, desk) = ECorpWithDesk();

        var (room, found) = office.Locate(desk.Id);

        Assert.Equal("Web", room.Name);
        Assert.Same(desk, found);
        Assert.Throws<DomainException>(() => office.Locate(DeskId.New()));
    }

    [Fact]
    public void A_desk_can_be_renamed_but_not_to_a_name_taken_in_its_room()
    {
        var (office, desk) = ECorpWithDesk();
        var other = office.AddDesk(office.Rooms[0].Id, "Tester", AgentProfile.Create("build"), Now);

        office.RenameDesk(desk.Id, " Theme developer ", Now);

        Assert.Equal("Theme developer", desk.Name);
        Assert.IsType<DeskRenamed>(office.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => office.RenameDesk(other.Id, "theme DEVELOPER", Now));
        office.RenameDesk(desk.Id, "Theme developer", Now);
    }

    [Fact]
    public void A_desk_can_be_dressed_by_hand_and_given_back_its_picked_look()
    {
        var (office, desk) = ECorpWithDesk();

        office.DressDesk(desk.Id, DeskLook.Create("tan", "black", "bun", "red", "glasses", "hoodie"), Now);

        Assert.Equal(("tan", "bun", "glasses", "hoodie", (string?)null), (desk.Look!.Skin, desk.Look.HairStyle, desk.Look.Accessory, desk.Look.Outfit, desk.Look.Costume));
        Assert.IsType<DeskDressed>(office.DomainEvents[^1]);
        office.DressDesk(desk.Id, null, Now);
        Assert.Null(desk.Look);
    }

    [Theory]
    [InlineData("", "black", "bun", "red", null)]
    [InlineData("tan", "black", "bun", "red", "pointy hat")]
    [InlineData("tan", "black", "bun", "<script>", null)]
    public void A_look_is_made_of_single_words(string skin, string hair, string style, string cloth, string? accessory)
    {
        Assert.Throws<DomainException>(() => DeskLook.Create(skin, hair, style, cloth, accessory));
    }

    [Fact]
    public void Blank_optional_parts_of_a_look_mean_none()
    {
        var look = DeskLook.Create("fair", "grey", "short", "blue", " ", "", null);

        Assert.Equal(((string?)null, (string?)null), (look.Accessory, look.Outfit));
    }

    [Fact]
    public void Private_folders_are_absolute_kept_once_and_without_a_trailing_slash()
    {
        var office = ECorp();

        office.SetPrivatePaths(["/srv/media/library/", "/srv/media/library", " ", "/opt/trainer"], Now);

        Assert.Equal(["/srv/media/library", "/opt/trainer"], office.PrivatePaths);
        Assert.IsType<OfficePrivatePathsSet>(office.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => office.SetPrivatePaths(["media/library"], Now));
        Assert.Throws<DomainException>(() => office.SetPrivatePaths(["/"], Now));
        Assert.Throws<DomainException>(() => office.SetPrivatePaths(Enumerable.Range(0, 21).Select(i => $"/srv/p{i}"), Now));
    }
}
