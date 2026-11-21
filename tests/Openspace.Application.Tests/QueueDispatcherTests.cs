using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Queue;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class QueueDispatcherTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemoryTasks _tasks = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly Office _office = Office.Open("Allsafe", Start);
    private readonly Desk _alice;
    private readonly Desk _bob;

    public QueueDispatcherTests()
    {
        var room = _office.OpenRoom("Security", _clock.GetUtcNow());
        _alice = _office.AddDesk(room.Id, "Alice", AgentProfile.Create("pentester"), _clock.GetUtcNow());
        _bob = _office.AddDesk(room.Id, "Bob", AgentProfile.Create("auditor"), _clock.GetUtcNow());
    }

    private QueueWaits Waits { get; } = new(new RecordingPublisher());

    private WorkInProgress Work { get; } = new();

    private InMemoryOffices Offices { get; } = new();

    private QueueDispatcher Dispatcher(int seats) =>
        new(_tasks, Offices, new Agents.EngineLimits(_clock), new LocalModels.GpuArbiter(new LiveOptions<LocalModels.LocalModelOptions>(() => new()), _clock), Waits, Work, _unitOfWork, new LiveOptions<QueueOptions>(() => new QueueOptions { Seats = seats }), _clock);

    private WorkTask Enqueue(Desk desk, string title, string? description = null, Office? office = null)
    {
        var task = WorkTask.Assign(office ?? _office, desk.Id, title, description, "angela", _clock.GetUtcNow());
        _tasks.Add(task);
        _clock.Advance(TimeSpan.FromSeconds(1));
        return task;
    }

    [Fact]
    public async Task Starts_the_oldest_task_when_a_seat_is_free()
    {
        var older = Enqueue(_alice, "Scan the perimeter");
        var newer = Enqueue(_bob, "Review the firewall rules");

        var started = await Dispatcher(seats: 1).DispatchAsync(default);

        Assert.Equal([older.Id], started);
        Assert.Equal(WorkTaskStatus.Running, older.Status);
        Assert.Equal(WorkTaskStatus.Queued, newer.Status);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Fills_every_free_seat()
    {
        Enqueue(_alice, "One");
        Enqueue(_bob, "Two");

        var started = await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Equal(2, started.Count);
    }

    [Fact]
    public async Task A_busy_desk_does_not_take_a_second_task()
    {
        var first = Enqueue(_alice, "First for Alice");
        var second = Enqueue(_alice, "Second for Alice");
        var forBob = Enqueue(_bob, "First for Bob");

        var started = await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Equal([first.Id, forBob.Id], started);
        Assert.Equal(WorkTaskStatus.Queued, second.Status);
    }

    [Fact]
    public async Task Running_tasks_count_against_the_seats()
    {
        var running = Enqueue(_alice, "Already running");
        running.Start(_clock.GetUtcNow());
        Enqueue(_bob, "Waiting");

        var started = await Dispatcher(seats: 1).DispatchAsync(default);

        Assert.Empty(started);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Proposed_tasks_are_never_started()
    {
        _tasks.Add(WorkTask.Propose(_office, "Not approved yet", null, "angela", _clock.GetUtcNow()));

        Assert.Empty(await Dispatcher(seats: 2).DispatchAsync(default));
    }

    [Fact]
    public async Task Waiting_tasks_say_what_they_wait_for()
    {
        var running = Enqueue(_alice, "Already running");
        running.Start(_clock.GetUtcNow());
        var later = Enqueue(_bob, "Builds on it");
        later.DependOn([running], _clock.GetUtcNow());

        await Dispatcher(seats: 2).DispatchAsync(default);
        Assert.Equal("Waits for \u201cAlready running\u201d to be accepted and merged.", Waits.Snapshot().Single(w => w.TaskId == later.Id.Value).Reason);

        await Dispatcher(seats: 1).DispatchAsync(default);
        Assert.Equal("Waits for a free seat: 1 agent works at a time.", Waits.Snapshot().Single(w => w.TaskId == later.Id.Value).Reason);
    }

    /// <summary>A running task that has changed <paramref name="files"/> in its copy, as Now saw it.</summary>
    private WorkTask Changing(Desk desk, params string[] files)
    {
        var task = Enqueue(desk, "Rework the cart");
        task.Start(_clock.GetUtcNow());
        Work.Replace([NowKeeper.Describe(task, _office, files)]);
        return task;
    }

    [Fact]
    public async Task A_task_waits_while_another_changes_the_files_it_names()
    {
        Offices.Add(_office);
        Changing(_alice, "src/web/cart.ts", "src/web/cart.css");
        var queued = Enqueue(_bob, "Fix the total", "The total in `src/web/cart.ts` is rounded twice.");

        var started = await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Empty(started);
        Assert.Equal(WorkTaskStatus.Queued, queued.Status);
        Assert.Equal("Waits for Alice, who is changing src/web/cart.ts.", Waits.Snapshot().Single(w => w.TaskId == queued.Id.Value).Reason);
    }

    [Fact]
    public async Task A_task_naming_a_folder_waits_for_the_files_in_it()
    {
        Offices.Add(_office);
        Changing(_alice, "src/web/cart.ts", "src/web/cart.css");
        var queued = Enqueue(_bob, "Restyle", "Everything under src/web/** gets the new colours.");

        await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Equal("Waits for Alice, who is changing src/web/cart.ts and 1 more file.", Waits.Snapshot().Single(w => w.TaskId == queued.Id.Value).Reason);
    }

    [Fact]
    public async Task A_task_that_names_no_files_never_waits_on_the_room_alone()
    {
        Offices.Add(_office);
        Changing(_alice, "src/web/cart.ts");
        var queued = Enqueue(_bob, "Tidy up the cart", "Make it nicer.");

        var started = await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Equal([queued.Id], started);
    }

    [Fact]
    public async Task Other_files_do_not_hold_a_task()
    {
        Offices.Add(_office);
        Changing(_alice, "src/web/cart.ts");
        var queued = Enqueue(_bob, "Fix the login", "In src/web/login.ts.");

        Assert.Equal([queued.Id], await Dispatcher(seats: 3).DispatchAsync(default));
    }

    [Fact]
    public async Task Work_in_another_office_never_holds_a_task()
    {
        var other = Office.Open("Evil Corp", Start);
        var room = other.OpenRoom("Web", Start);
        var desk = other.AddDesk(room.Id, "Tyrell", AgentProfile.Create("frontend-dev"), Start);
        Offices.Add(_office);
        Offices.Add(other);
        Changing(_alice, "src/web/cart.ts");
        var queued = Enqueue(desk, "Fix the total", "In src/web/cart.ts.", other);

        Assert.Equal([queued.Id], await Dispatcher(seats: 3).DispatchAsync(default));
    }

    [Fact]
    public async Task An_office_can_let_overlapping_work_run()
    {
        _office.HoldOverlaps(false, Start);
        Offices.Add(_office);
        Changing(_alice, "src/web/cart.ts");
        var queued = Enqueue(_bob, "Fix the total", "In src/web/cart.ts.");

        Assert.Equal([queued.Id], await Dispatcher(seats: 3).DispatchAsync(default));
    }

    [Fact]
    public async Task Two_tasks_naming_the_same_file_do_not_start_in_the_same_round()
    {
        // Git has seen nothing of the first yet: what it names is enough to hold the second.
        Offices.Add(_office);
        var first = Enqueue(_alice, "Fix the total", "In src/web/cart.ts.");
        var second = Enqueue(_bob, "Translate the cart", "Strings of src/web/cart.ts.");

        var started = await Dispatcher(seats: 3).DispatchAsync(default);

        Assert.Equal([first.Id], started);
        Assert.Equal("Waits for Alice, who is changing src/web/cart.ts.", Waits.Snapshot().Single(w => w.TaskId == second.Id.Value).Reason);
    }

    [Fact]
    public async Task Work_that_finished_since_the_last_look_holds_nothing()
    {
        Offices.Add(_office);
        var done = Changing(_alice, "src/web/cart.ts");
        done.Fail("Stopped", _clock.GetUtcNow());
        var queued = Enqueue(_bob, "Fix the total", "In src/web/cart.ts.");

        Assert.Equal([queued.Id], await Dispatcher(seats: 3).DispatchAsync(default));
    }
}
