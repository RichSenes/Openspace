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
            .AddDeskAsync(office, room, "Frontend", "frontend-dev", null, default));
        var backend = await InScope(sp => sp.GetRequiredService<OfficeCommands>()
            .AddDeskAsync(office, room, "Backend", "backend-dev", "local/tiny-model", default));
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
    public async Task Repository_path_and_agent_run_survive_a_round_trip()
    {
        var (office, frontend, _) = await FurnishECorpAsync();
        await InScope(sp => sp.GetRequiredService<OfficeCommands>().SetRepositoryAsync(office, "/srv/repos/e-corp", default));
        var taskId = await InScope(sp => sp.GetRequiredService<WorkTaskCommands>()
            .AssignAsync(office, frontend, "Add a logout button", null, "elliot", default));
        await InScope(async sp =>
        {
            var tasks = sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>();
            var task = (await tasks.FindAsync(taskId, default))!;
            task.Start(_db.Clock.GetUtcNow());
            task.AttachRun(AgentRun.Create("ses_fsociety", "/srv/worktrees/abc", "openspace/add-a-logout-button-abc"));
            await sp.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
        });

        var found = await InScope(sp => sp.GetRequiredService<Application.Abstractions.IWorkTaskRepository>()
            .FindBySessionAsync("ses_fsociety", default));
        var view = await InScope(sp => sp.GetRequiredService<OfficeQueries>().GetAsync(office, default));

        Assert.Equal(taskId, found?.Id);
        Assert.Equal("/srv/worktrees/abc", found?.Run?.WorkspacePath);
        Assert.Equal("/srv/repos/e-corp", view.RepositoryPath);
    }
}
