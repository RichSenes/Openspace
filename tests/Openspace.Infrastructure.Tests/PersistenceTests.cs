using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Offices;
using Openspace.Application.Queue;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Tests;

/// <summary>Round-trips aggregates through SQLite, each step in its own scope.</summary>
public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly SqliteFixture _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _db.Scope();
        return await action(scope.ServiceProvider);
    }

    private async Task InScope(Func<IServiceProvider, Task> action)
    {
        await using var scope = _db.Scope();
        await action(scope.ServiceProvider);
    }

    private async Task<(OfficeId Office, DeskId Frontend, DeskId Backend)> FurnishECorpAsync()
    {
        var office = await InScope(sp => sp.GetRequiredService<OfficeCommands>().OpenOfficeAsync("E Corp", default));
        var room = await InScope(sp => sp.GetRequiredService<OfficeCommands>().OpenRoomAsync(office, "Web", default));
        var frontend = await InScope(sp => sp.GetRequiredService<OfficeCommands>()
            .AddDeskAsync(office, room, "Frontend", "frontend-dev", null, null, default));
        var backend = await InScope(sp => sp.GetRequiredService<OfficeCommands>()
            .AddDeskAsync(office, room, "Backend", "backend-dev", "local/tiny-model", null, default));
        return (office, frontend, backend);
    }

    [Fact]
    public async Task Office_with_rooms_and_desks_survives_a_round_trip()
    {
        var (officeId, _, _) = await FurnishECorpAsync();
        await InScope(async sp =>
        {
            var commands = sp.GetRequiredService<OfficeCommands>();
            var office = await sp.GetRequiredService<OfficeQueries>().GetAsync(officeId, default);
            await commands.UpdateRoomContextAsync(officeId, new RoomId(office.Rooms[0].Id), "PHP 8.3 only.", default);
        });

        var view = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(officeId, default));

        Assert.Equal("E Corp", view.Name);
        var room = Assert.Single(view.Rooms);
        Assert.Equal("PHP 8.3 only.", room.Context);
        Assert.Collection(room.Desks.OrderBy(d => d.Name),
            d => Assert.Equal(("Backend", "backend-dev", "local/tiny-model"), (d.Name, d.AgentName, d.Model)),
            d => Assert.Equal(("Frontend", "frontend-dev", (string?)null), (d.Name, d.AgentName, d.Model)));
    }

    [Fact]
    public async Task Task_status_and_notes_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Add a logout button", "Top right corner", "elliot", default));
        await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AddNoteAsync(taskId, "elliot", "Use the existing icon set.", default));

        var task = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().GetAsync(taskId, default));

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(frontend.Value, task.DeskId);
        Assert.Equal(_db.Clock.GetUtcNow(), task.CreatedAt);
        var note = Assert.Single(task.Notes);
        Assert.Equal((NoteAuthorKind.Human, "elliot", "Use the existing icon set."), (note.AuthorKind, note.AuthorName, note.Text));
    }

    [Fact]
    public async Task Tasks_are_listed_oldest_first_and_filtered_by_status()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var commands = (Func<string, Task<WorkTaskId>>)(title =>
            InScope(sp => sp.GetRequiredService<WorkTaskCommands>().AssignAsync(office, frontend, title, null, "elliot", default)));
        var first = await commands("First");
        _db.Clock.Advance(TimeSpan.FromMinutes(1));
        await commands("Second");
        _db.Clock.Advance(TimeSpan.FromMinutes(1));
        var proposed = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .ProposeAsync(office, "Third", null, "darlene", default));

        var queued = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().ListAsync(office, WorkTaskStatus.Queued, default));
        var all = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().ListAsync(null, null, default));

        Assert.Equal(["First", "Second"], queued.Select(t => t.Title));
        Assert.Equal(first.Value, all[0].Id);
        Assert.Equal(proposed.Value, all[^1].Id);
    }

    [Fact]
    public async Task Dispatching_starts_tasks_and_persists_the_change()
    {
        var (office, frontend, backend) = await FurnishECorpAsync();
        foreach (var (desk, title) in new[] { (frontend, "A"), (frontend, "B"), (backend, "C") })
        {
            await InScope(sp => sp.GetRequiredService<WorkTaskCommands>().AssignAsync(office, desk, title, null, "elliot", default));
            _db.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var started = await InScope(sp => sp.GetRequiredService<QueueDispatcher>().DispatchAsync(default));

        var running = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().ListAsync(office, WorkTaskStatus.Running, default));
        Assert.Equal(2, started.Count);
        Assert.Equal(["A", "C"], running.Select(t => t.Title));
    }

    [Fact]
    public async Task The_office_theme_survives_a_round_trip()
    {
        var (office, _, _) = await FurnishECorpAsync();
        Assert.Equal("cozy", (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Theme);

        await InScope(sp => sp.GetRequiredService<OfficeCommands>().ChangeThemeAsync(office, "neonDistrict", default));

        Assert.Equal("neonDistrict", (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Theme);
    }

    [Fact]
    public async Task A_room_layout_survives_a_round_trip_and_can_be_rearranged()
    {
        var (office, frontend, backend) = await FurnishECorpAsync();
        var room = new RoomId((await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0].Id);
        var first = RoomLayout.Create(12, 5, new Dictionary<DeskId, Spot> { [frontend] = new(20, 24) }, [new("plant", 170, 80), new("rug", 30, 60)]);
        var second = RoomLayout.Create(9, 3, new Dictionary<DeskId, Spot> { [backend] = new(60, 24) }, [new("whiteboard", 40, 2)]);

        await InScope(sp => sp.GetRequiredService<OfficeCommands>().ArrangeRoomAsync(office, room, first, default));
        var arranged = (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0];
        await InScope(sp => sp.GetRequiredService<OfficeCommands>().ArrangeRoomAsync(office, room, second, default));
        var rearranged = (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0];

        Assert.Equal((12, 5), (arranged.Width, arranged.Depth));
        Assert.Equal(["plant", "rug"], arranged.Furniture.Select(f => f.Name).Order());
        Assert.Equal(new Spot(20, 24), arranged.Desks.Single(d => d.Id == frontend.Value).Spot);
        Assert.Equal((9, 3), (rearranged.Width, rearranged.Depth));
        Assert.Equal(new FurniturePiece("whiteboard", 40, 2), Assert.Single(rearranged.Furniture));
        Assert.Null(rearranged.Desks.Single(d => d.Id == frontend.Value).Spot);
        Assert.Equal(new Spot(60, 24), rearranged.Desks.Single(d => d.Id == backend.Value).Spot);
    }

    [Fact]
    public async Task A_floor_plan_survives_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var room = new RoomId((await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0].Id);
        var layout = RoomLayout.Create(10, 5, new Dictionary<DeskId, Spot> { [frontend] = new(20, 24) }, [], new DoorPlacement(Wall.Left, 2), [16, 80]);
        var plan = FloorPlan.Create(24, 14, [new("vendingMachine", 300, 150), new("plant", 20, 200)], [new PlannedRoom(room, 3, 1, layout)], [new FloorCut(16, 0, 8, 6)]);

        await InScope(sp => sp.GetRequiredService<OfficeCommands>().PlanFloorAsync(office, plan, default));
        var view = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default));

        Assert.Equal((24, 14), (view.FloorWidth, view.FloorDepth));
        Assert.Equal(["plant", "vendingMachine"], view.Furniture.Select(f => f.Name).Order());
        Assert.Equal(new FloorCut(16, 0, 8, 6), Assert.Single(view.Cuts));
        var planned = Assert.Single(view.Rooms);
        Assert.Equal((3, 1, 10, 5), (planned.Left, planned.Top, planned.Width, planned.Depth));
        Assert.Equal(new DoorPlacement(Wall.Left, 2), planned.Door);
        Assert.Equal([16, 80], planned.Windows);

        await InScope(sp => sp.GetRequiredService<OfficeCommands>().ClearFloorPlanAsync(office, default));
        var cleared = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default));
        Assert.Equal(((int?)null, (int?)null, (DoorPlacement?)null), (cleared.FloorWidth, cleared.Rooms[0].Left, cleared.Rooms[0].Door));
        Assert.Empty(cleared.Furniture);
        Assert.Empty(cleared.Cuts);
    }

    [Fact]
    public async Task Repository_path_and_agent_run_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var repository = Directory.CreateTempSubdirectory("openspace-repo-").FullName;
        await InScope(sp => sp.GetRequiredService<OfficeCommands>().SetRepositoryAsync(office, repository, initialize: true, default));
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Add a logout button", null, "elliot", default));
        await InScope(async sp =>
        {
            var tasks = sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>();
            var task = (await tasks.FindAsync(taskId, default))!;
            task.Start(_db.Clock.GetUtcNow());
            task.AttachRun(AgentRun.Create("ses_fsociety", "/srv/worktrees/abc", "openspace/add-a-logout-button-abc"), _db.Clock.GetUtcNow());
            await sp.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
        });

        var found = await InScope(sp => sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>()
            .FindBySessionAsync("ses_fsociety", default));
        var view = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default));

        Assert.Equal(taskId, found?.Id);
        Assert.Equal("/srv/worktrees/abc", found?.Run?.WorkspacePath);
        Assert.Equal(repository, view.RepositoryPath);
        Directory.Delete(repository, recursive: true);
    }

    [Fact]
    public async Task The_audit_trail_is_saved_with_the_changes_it_describes()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Add a logout button", null, "elliot", default));
        await InScope(sp => sp.GetRequiredService<WorkTaskCommands>().AddNoteAsync(taskId, "darlene", "Top right.", default));

        var log = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().LogAsync(taskId, default));

        Assert.Equal(["Task created", "Queued for its desk", "Note from darlene"], log.Select(e => e.Summary));
        Assert.Equal("Top right.", log[^1].Detail);
    }

    [Fact]
    public async Task The_same_source_event_cannot_be_stored_twice()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Add a logout button", null, "elliot", default));

        await InScope(async sp =>
        {
            var audit = sp.GetRequiredService<Application.Abstractions.IAuditLog>();
            audit.Add(AuditEntry.Record(taskId, _db.Clock.GetUtcNow(), AuditKind.Command, "Ran ls", externalId: "call_1"));
            audit.Add(AuditEntry.Record(taskId, _db.Clock.GetUtcNow(), AuditKind.Command, "Ran ls", externalId: "call_1"));
            await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() =>
                sp.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default));
        });
        var contains = await InScope(sp => sp.GetRequiredService<Application.Abstractions.IAuditLog>().ContainsAsync(taskId, "call_1", default));

        Assert.False(contains);
    }

    [Fact]
    public async Task Roles_and_desk_roles_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var careful = new Domain.Roles.RolePermissions(Domain.Roles.PermissionLevel.Ask, Domain.Roles.PermissionLevel.Deny, Domain.Roles.PermissionLevel.Allow);
        var roleId = await InScope(sp => sp.GetRequiredService<Application.Roles.RoleCommands>()
            .DefineAsync("Auditor", "Reviews changes", "Never change code.", "opencode/careful", careful, default));
        await InScope(sp => sp.GetRequiredService<OfficeCommands>().SetDeskRoleAsync(office, frontend, roleId, default));

        var roles = await InScope(sp => sp.GetRequiredService<Application.Roles.RoleQueries>().ListAsync(default));
        var view = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default));

        var role = Assert.Single(roles);
        Assert.Equal(("Auditor", "Never change code.", "opencode/careful"), (role.Name, role.Instructions, role.Model));
        Assert.Equal((Domain.Roles.PermissionLevel.Ask, Domain.Roles.PermissionLevel.Deny, Domain.Roles.PermissionLevel.Allow), (role.Edit, role.Bash, role.Web));
        Assert.Equal(roleId.Value, view.Rooms[0].Desks.Single(d => d.Id == frontend.Value).RoleId);
    }

    [Fact]
    public async Task Epics_and_their_tasks_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var epicId = await InScope(async sp =>
        {
            var offices = sp.GetRequiredService<Application.Abstractions.IOfficeRepository>();
            var e = (await offices.FindAsync(office, default))!;
            var epic = Epic.Open(e, "Contact page", "Add a contact page.", "elliot", _db.Clock.GetUtcNow());
            sp.GetRequiredService<Application.Abstractions.IEpicRepository>().Add(epic);
            var task = WorkTask.Propose(e, "Build the form", null, "elliot", _db.Clock.GetUtcNow(), epic);
            task.AssignTo(e, frontend);
            sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>().Add(task);
            await sp.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
            return epic.Id;
        });

        var epics = await InScope(sp => sp.GetRequiredService<EpicQueries>().ListAsync(office, default));
        var approved = await InScope(sp => sp.GetRequiredService<Supervisor>().ApproveAllAsync(epicId, default));
        var tasks = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().ListAsync(office, null, default));

        Assert.Equal(("Contact page", "Add a contact page."), (epics[0].Title, epics[0].Request));
        Assert.Equal(1, approved);
        Assert.Equal((WorkTaskStatus.Queued, (Guid?)epicId.Value), (tasks[0].Status, tasks[0].EpicId));
    }

    [Fact]
    public async Task Several_tasks_with_system_notes_can_be_saved_together()
    {
        var (office, frontend, backend) = await FurnishECorpAsync();
        foreach (var desk in new[] { frontend, backend })
        {
            await InScope(sp => sp.GetRequiredService<WorkTaskCommands>().AssignAsync(office, desk, "Work", null, "elliot", default));
        }

        // Starting both, then recovering both, writes two system notes in one save.
        await InScope(sp => sp.GetRequiredService<Application.Queue.QueueDispatcher>().DispatchAsync(default));
        await InScope(sp => sp.GetRequiredService<Application.Agents.TaskRunner>().RecoverAsync(default));

        var tasks = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().ListAsync(office, null, default));
        Assert.All(tasks, t => Assert.Equal(NoteAuthorKind.System, Assert.Single(t.Notes).AuthorKind));
    }

    [Fact]
    public async Task Room_procedures_and_task_checklists_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var roomId = (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0].Id;
        await InScope(sp => sp.GetRequiredService<OfficeCommands>()
            .SetRoomProcedureAsync(office, new RoomId(roomId), ["Back up first", "Log the change"], default));
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Patch the header", null, "elliot", default));
        await InScope(async sp =>
        {
            var task = (await sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>().FindAsync(taskId, default))!;
            task.Start(_db.Clock.GetUtcNow());
            task.AdoptProcedure(["Back up first", "Log the change"]);
            task.CompleteStep(2, "CHANGES.md updated", _db.Clock.GetUtcNow());
            await sp.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
        });

        var room = (await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default))).Rooms[0];
        var view = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().GetAsync(taskId, default));
        var log = await InScope(sp => sp.GetRequiredService<WorkTaskQueries>().LogAsync(taskId, default));

        Assert.Equal(["Back up first", "Log the change"], room.Procedure);
        Assert.Equal([(1, (string?)null), (2, "CHANGES.md updated")], view.Procedure.Select(s => (s.Number, s.Evidence)));
        Assert.Contains(log, e => e.Summary == "Procedure step 2 done: Log the change");
    }

    [Fact]
    public async Task Reports_are_written_from_the_log_and_kept()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        var since = _db.Clock.GetUtcNow();
        await InScope(sp => sp.GetRequiredService<WorkTaskCommands>().AssignAsync(office, frontend, "Add a logout button", null, "elliot", default));

        // OpenCode is not reachable in tests: the report falls back to the facts.
        var report = await InScope(sp => sp.GetRequiredService<Application.Reports.ReportWriter>()
            .WriteForPeriodAsync(office, since, since.AddDays(1), "24 June", "English", "elliot", default));
        var listed = await InScope(sp => sp.GetRequiredService<Application.Reports.ReportQueries>().ListAsync(office, default));

        Assert.False(report.WrittenByAi);
        Assert.Contains("## Add a logout button", report.Body, StringComparison.Ordinal);
        Assert.Equal(report.Id, Assert.Single(listed).Id);
    }
}
