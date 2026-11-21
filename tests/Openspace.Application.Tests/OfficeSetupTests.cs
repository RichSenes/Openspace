using System.Text.Json;
using Openspace.Application.Offices;
using Openspace.Application.Routines;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

public class OfficeSetupTests
{
    private readonly Harness _h = new();

    private OfficeSetups Setups() => new(_h.Offices, _h.Roles, _h.Routines, [_h.OffersKind], _h.UnitOfWork, _h.Clock);

    private static ScheduleView Weekdays => new([DayOfWeek.Monday, DayOfWeek.Friday], "07:30", 1, null);

    private static OfficeSetup Setup(
        IReadOnlyList<RoleSetup>? roles = null, IReadOnlyList<RoomSetup>? rooms = null, IReadOnlyList<RoutineSetup>? routines = null) =>
        new(OfficeSetup.FormatName, 1, "Allsafe", "night", true, false, ["opencode/free-model"], roles ?? [], rooms ?? [], routines ?? []);

    private async Task<Office> ImportedAsync(OfficeSetup setup, string? name = null)
    {
        var id = await Setups().ImportAsync(setup, name, default);
        return _h.Offices.Items.Single(o => o.Id == id);
    }

    [Fact]
    public async Task An_export_holds_rooms_desks_their_roles_and_routines_but_not_the_person_s_things()
    {
        var role = Role.Define("Tester", "Writes tests", "Test first.", null, new RolePermissions(PermissionLevel.Allow, PermissionLevel.Ask, PermissionLevel.Deny), Harness.Start);
        _h.Roles.Add(role);
        _h.Roles.Add(Role.Define("Unused", null, null, null, RolePermissions.Default, Harness.Start));
        _h.Office.SetRoomProcedure(_h.Room.Id, ["Write the test", "Make it pass"], Harness.Start);
        _h.Office.SetRoomCheck(_h.Room.Id, "npm test", Harness.Start);
        _h.Office.SetDeskRole(_h.Desk.Id, role.Id, Harness.Start);
        _h.Office.SetPrivatePaths(["/home/elliot/secrets"], Harness.Start);
        await _h.RoutineCommands().CreateAsync(_h.Office.Id, "Offers", "fake-offers", Weekdays, """{"country":"JP","account":"elliot@example.com"}""", default);

        var setup = await Setups().ExportAsync(_h.Office.Id, default);

        Assert.Equal((OfficeSetup.FormatName, 1, "E Corp"), (setup.Format, setup.Version, setup.Name));
        var room = Assert.Single(setup.Rooms!);
        Assert.Equal(("Web", "Never touch the payments module.", "npm test"), (room.Name, room.Context, room.Check));
        Assert.Equal(["Write the test", "Make it pass"], room.Procedure);
        var desk = Assert.Single(room.Desks!);
        Assert.Equal(("Frontend", "frontend-dev", "Tester"), (desk.Name, desk.AgentName, desk.Role));
        Assert.Equal("Tester", Assert.Single(setup.Roles!).Name);
        var routine = Assert.Single(setup.Routines!);
        Assert.Equal(("Offers", "fake-offers", "07:30"), (routine.Name, routine.Kind, routine.Schedule.At));
        Assert.Equal("JP", routine.Settings!.Value.GetProperty("country").GetString());

        // No repository, private folder, Google account, nor the routines room of its own.
        var json = JsonSerializer.Serialize(setup);
        Assert.DoesNotContain("/srv/repos", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", json, StringComparison.Ordinal);
        Assert.DoesNotContain("elliot@example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain(RoutineCommands.RoomName, setup.Rooms!.Select(r => r.Name));
    }

    [Fact]
    public async Task An_import_opens_a_new_office_with_its_rooms_desks_roles_and_routines()
    {
        var setup = Setup(
            roles: [new RoleSetup("Writer", "Writes docs", "Short sentences.", null, new RolePermissions(PermissionLevel.Allow, PermissionLevel.Ask, PermissionLevel.Deny), ["docs"])],
            rooms: [new RoomSetup("Docs", "Plain English.", ["Read the page first"], "npm run lint", null, [new DeskSetup("Writer", "writer", null, AgentEngine.OpenCode, "Writer")])],
            routines: [new RoutineSetup("Offers", "fake-offers", Weekdays, null, Enabled: false, AskFirst: true)]);

        var office = await ImportedAsync(setup, "  Allsafe docs ");

        Assert.Equal(("Allsafe docs", "night", true, false), (office.Name, office.Theme, office.LocalOnly, office.HoldOverlappingWork));
        Assert.Equal(["opencode/free-model"], office.ReserveModels);
        Assert.Null(office.RepositoryPath);
        var docs = office.Rooms.Single(r => r.Kind == RoomKind.Work);
        Assert.Equal(("Docs", "Plain English.", "npm run lint"), (docs.Name, docs.Context, docs.Check));
        var role = _h.Roles.Items.Single(r => r.Name == "Writer");
        Assert.Equal(["docs"], role.Skills);
        Assert.Equal(role.Id, Assert.Single(docs.Desks).RoleId);
        Assert.Contains(office.Rooms, r => r.Kind == RoomKind.Routines);
        var routine = Assert.Single(_h.Routines.Items);
        Assert.Equal((office.Id, false, true, """{"country":"JP"}"""), (routine.OfficeId, routine.Enabled, routine.AskFirst, routine.Settings));
        Assert.Equal(1, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task A_role_already_here_with_the_same_name_is_used_as_it_is()
    {
        var mine = Role.Define("Writer", "Mine", "My own words.", null, RolePermissions.Default, Harness.Start);
        _h.Roles.Add(mine);

        var office = await ImportedAsync(Setup(
            roles: [new RoleSetup("writer", "Theirs", "Their words.", null, null, null)],
            rooms: [new RoomSetup("Docs", null, null, null, null, [new DeskSetup("Writer", "writer", null, AgentEngine.OpenCode, "WRITER")])]));

        Assert.Single(_h.Roles.Items);
        Assert.Equal("My own words.", mine.Instructions);
        Assert.Equal(mine.Id, office.Rooms.Single().Desks.Single().RoleId);
    }

    [Fact]
    public async Task What_comes_out_goes_back_in_as_the_same_office()
    {
        _h.Office.SetRoomProcedure(_h.Room.Id, ["Read first"], Harness.Start);
        await _h.RoutineCommands().CreateAsync(_h.Office.Id, "Offers", "fake-offers", Weekdays, null, default);
        var setup = await Setups().ExportAsync(_h.Office.Id, default);

        // Through a file and back, as the person does it.
        var file = JsonSerializer.Serialize(setup, JsonSerializerOptions.Web);
        var read = JsonSerializer.Deserialize<OfficeSetup>(file, JsonSerializerOptions.Web)!;
        var copy = await ImportedAsync(read);

        Assert.NotEqual(_h.Office.Id, copy.Id);
        Assert.Equal("E Corp", copy.Name);
        var again = await Setups().ExportAsync(copy.Id, default);
        Assert.Equal(file, JsonSerializer.Serialize(again, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task A_file_that_is_not_an_office_or_too_new_is_refused()
    {
        var notOurs = await Assert.ThrowsAsync<RejectedException>(() => Setups().ImportAsync(Setup() with { Format = "something-else" }, null, default));
        var tooNew = await Assert.ThrowsAsync<RejectedException>(() => Setups().ImportAsync(Setup() with { Version = 99 }, null, default));

        Assert.Equal(("setup-format", "setup-version"), (notOurs.Code, tooNew.Code));
        Assert.Single(_h.Offices.Items);
    }

    [Fact]
    public async Task A_file_refused_halfway_leaves_nothing_behind()
    {
        var setup = Setup(
            roles: [new RoleSetup("Writer", null, null, null, null, null)],
            rooms: [new RoomSetup("Docs", null, null, null, null, [new DeskSetup("Writer", "writer", null, AgentEngine.OpenCode, "Writer")])],
            routines: [new RoutineSetup("Mystery", "no-such-kind", Weekdays)]);

        var error = await Assert.ThrowsAsync<RejectedException>(() => Setups().ImportAsync(setup, null, default));
        var badDesk = await Assert.ThrowsAsync<RejectedException>(() => Setups().ImportAsync(
            Setup(rooms: [new RoomSetup("Docs", null, null, null, null, [new DeskSetup("Ghost", "ghost", null, AgentEngine.OpenCode, "Nobody")])]), null, default));
        await Assert.ThrowsAsync<DomainException>(() => Setups().ImportAsync(
            Setup(rooms: [new RoomSetup("Docs", null, null, null, null, [new DeskSetup("Bad", "Bad Name!", null)])]), null, default));

        Assert.Equal(("setup-routine-kind", "setup-role-missing"), (error.Code, badDesk.Code));
        Assert.Single(_h.Offices.Items);
        Assert.Empty(_h.Roles.Items);
        Assert.Empty(_h.Routines.Items);
        Assert.Equal(0, _h.UnitOfWork.Saves);
    }
}
