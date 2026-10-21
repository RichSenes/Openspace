using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Agents;
using Openspace.Application.Offices;
using Openspace.Application.Queue;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>An E Corp office with one furnished room, plus every fake an application service needs.</summary>
internal sealed class Harness
{
    public static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(Start);

    public InMemoryOffices Offices { get; } = new();

    public InMemoryTasks Tasks { get; } = new();

    public CountingUnitOfWork UnitOfWork { get; } = new();

    public FakeRuntime Runtime { get; } = new();

    public FakeWorkspaces Workspaces { get; } = new();

    public RecordingPublisher Publisher { get; } = new();

    public InMemoryAuditLog Audit { get; } = new();

    public ActivityBoard Activity { get; }

    public Office Office { get; }

    public Room Room { get; }

    public Desk Desk { get; }

    public Harness(bool withRepository = true)
    {
        Activity = new ActivityBoard(Publisher);
        Office = Office.Open("E Corp", Start);
        Room = Office.OpenRoom("Web", Start);
        Office.UpdateRoomContext(Room.Id, "Never touch the payments module.", Start);
        Desk = Office.AddDesk(Room.Id, "Frontend", AgentProfile.Create("frontend-dev"), Start);
        if (withRepository)
        {
            Office.SetRepository("/srv/repos/e-corp", Start);
        }

        Offices.Add(Office);
    }

    public WorkTask Enqueue(string title = "Add a logout button")
    {
        var task = WorkTask.Assign(Office, Desk.Id, title, "Top right corner.", "elliot", Clock.GetUtcNow());
        Tasks.Add(task);
        Clock.Advance(TimeSpan.FromSeconds(1));
        return task;
    }

    public TaskRunner Runner(int seats = 1) => new(
        new QueueDispatcher(Tasks, UnitOfWork, Options.Create(new QueueOptions { Seats = seats }), Clock),
        Offices, Tasks, UnitOfWork, Runtime, Workspaces, Activity, Clock, NullLogger<TaskRunner>.Instance);

    public AgentSignals Signals() => new(Tasks, UnitOfWork, Runtime, Activity, Audit, Publisher, Clock);

    public OfficeCommands OfficeCommands() => new(Offices, UnitOfWork, Workspaces, Clock);

    public WorkTaskCommands Commands() => new(Offices, Tasks, UnitOfWork, Runtime, Workspaces, Activity, Audit, Clock);
}
