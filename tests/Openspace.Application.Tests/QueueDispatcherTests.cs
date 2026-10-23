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

    private QueueDispatcher Dispatcher(int seats) =>
        new(_tasks, _unitOfWork, new LiveOptions<QueueOptions>(() => new QueueOptions { Seats = seats }), _clock);

    private WorkTask Enqueue(Desk desk, string title)
    {
        var task = WorkTask.Assign(_office, desk.Id, title, null, "angela", _clock.GetUtcNow());
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
}
