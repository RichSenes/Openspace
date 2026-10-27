using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Agents;
using Openspace.Application.LocalModels;
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

    public FakeCheckRunner Checks { get; } = new();

    public FakePullRequests PullRequests { get; } = new();

    public RecordingPublisher Publisher { get; } = new();

    public InMemoryAuditLog Audit { get; } = new();

    public InMemoryRoles Roles { get; } = new();

    public InMemoryEpics Epics { get; } = new();

    public InMemoryReports Reports { get; } = new();

    public Review.PendingReviews Reviews { get; } = new();

    public PermissionRequests Permissions { get; } = new();

    public EngineLimits Limits { get; }

    /// <summary>Local models are off unless a test turns them on.</summary>
    public LocalModelOptions LocalModels { get; } = new();

    public GpuArbiter Gpu { get; }

    public LoopGuard Loops { get; } = new();

    public InMemoryRoutines Routines { get; } = new();

    public Routines.RoutineRequests RoutineRequests { get; } = new();

    public RecordingOutbox Outbox { get; } = new();

    public FakeOffersKind OffersKind { get; } = new();

    public FakeGpuProbe GpuProbe { get; } = new();

    public FakeModelServer ModelServer { get; } = new();

    public ActivityBoard Activity { get; }

    /// <summary>A person answers every request unless a test says otherwise.</summary>
    public Autonomy Autonomy { get; set; } = Autonomy.Ask;

    public Office Office { get; }

    public Room Room { get; }

    public Desk Desk { get; }

    public Harness(bool withRepository = true)
    {
        Limits = new EngineLimits(Clock);
        Gpu = new GpuArbiter(new LiveOptions<LocalModelOptions>(() => LocalModels), Clock);
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
        new QueueDispatcher(Tasks, Offices, Limits, Gpu, UnitOfWork, new LiveOptions<QueueOptions>(() => new QueueOptions { Seats = seats }), Clock),
        Offices, new AgentSetups(Roles), Tasks, UnitOfWork, Runtime, Workspaces, Activity, Signals(), Loops, Clock, NullLogger<TaskRunner>.Instance);

    public Routines.RoutineRunner RoutineRunner() =>
        new(Routines, [OffersKind], RoutineRequests, Outbox, UnitOfWork, Clock, NullLogger<Routines.RoutineRunner>.Instance);

    public Routines.RoutineCommands RoutineCommands() => new(Offices, Routines, [OffersKind], RoutineRequests, UnitOfWork, Clock);

    public Routines.RoutineQueries RoutineQueries() => new(Routines, [OffersKind]);

    public Routines.ProposalCommands ProposalCommands() =>
        new(Routines, [OffersKind], Outbox, UnitOfWork, Clock, NullLogger<Routines.ProposalCommands>.Instance);

    public GpuShift Shift() => new(
        Gpu, GpuProbe, ModelServer, Tasks, Offices, Runtime, Activity, UnitOfWork, new LiveOptions<LocalModelOptions>(() => LocalModels), Clock);

    public AgentSignals Signals() => new(
        Offices, new AgentSetups(Roles), Tasks, UnitOfWork, Runtime, Activity, Audit, Publisher, Reviews, Permissions,
        new LiveOptions<AgentOptions>(() => new AgentOptions { Autonomy = Autonomy }), Limits, Loops, Clock);

    public Review.TaskReviewer Reviewer() => new(Offices, Tasks, UnitOfWork, Runtime, Audit, Clock, NullLogger<Review.TaskReviewer>.Instance);

    public OfficeCommands OfficeCommands() => new(Offices, Roles, UnitOfWork, Workspaces, Clock);

    public Application.Reports.ReportWriter ReportWriter() => new(
        Offices, Epics, Tasks, Audit, Reports, UnitOfWork, Runtime, Clock, NullLogger<Application.Reports.ReportWriter>.Instance);

    public Supervisor Supervisor() => new(Offices, Roles, Epics, Tasks, UnitOfWork, Runtime, Clock);

    public Roles.RoleCommands RoleCommands() => new(Roles, Offices, UnitOfWork, Runtime, Clock);

    public Application.Checks.MergeChecks MergeChecks() => new(
        Offices, Tasks, Workspaces, Checks, UnitOfWork, Microsoft.Extensions.Options.Options.Create(new Application.Checks.CheckOptions()), Clock);

    public WorkTaskCommands Commands() => new(Offices, Tasks, UnitOfWork, Runtime, Workspaces, Activity, Audit, Signals(), PullRequests, Clock);
}
