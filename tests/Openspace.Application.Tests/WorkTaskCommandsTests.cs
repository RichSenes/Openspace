using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Agents;
using Openspace.Application.Work;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class WorkTaskCommandsTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemoryOffices _offices = new();
    private readonly InMemoryTasks _tasks = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly Office _office = Office.Open("E Corp", Start);
    private readonly Desk _desk;
    private readonly WorkTaskCommands _commands;

    public WorkTaskCommandsTests()
    {
        var room = _office.OpenRoom("Web", _clock.GetUtcNow());
        _desk = _office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), _clock.GetUtcNow());
        _offices.Add(_office);
        var runtime = new FakeRuntime();
        var board = new Agents.ActivityBoard(new RecordingPublisher());
        var audit = new InMemoryAuditLog();
        var signals = new Agents.AgentSignals(_offices, new Agents.AgentSetups(new InMemoryRoles()), _tasks, _unitOfWork, runtime, board, audit, new RecordingPublisher(), _clock);
        _commands = new WorkTaskCommands(_offices, _tasks, _unitOfWork, runtime, new FakeWorkspaces(), board, audit, signals, _clock);
    }

    [Fact]
    public async Task Assigning_stores_a_queued_task_and_saves()
    {
        var id = await _commands.AssignAsync(_office.Id, _desk.Id, "Fix the footer", null, "elliot", default);

        var task = Assert.Single(_tasks.Items);
        Assert.Equal(id, task.Id);
        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(_clock.GetUtcNow(), task.CreatedAt);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Approving_can_pick_the_desk()
    {
        var id = await _commands.ProposeAsync(_office.Id, "Update the privacy page", null, "elliot", default);

        await _commands.ApproveAsync(id, _desk.Id, default);

        Assert.Equal((WorkTaskStatus.Queued, (DeskId?)_desk.Id), (_tasks.Items[0].Status, _tasks.Items[0].DeskId));
    }

    [Fact]
    public async Task Approving_without_any_desk_is_refused()
    {
        var id = await _commands.ProposeAsync(_office.Id, "Update the privacy page", null, "elliot", default);

        await Assert.ThrowsAsync<DomainException>(() => _commands.ApproveAsync(id, null, default));
    }

    [Fact]
    public async Task Sending_back_records_who_reviewed()
    {
        var id = await _commands.AssignAsync(_office.Id, _desk.Id, "Fix the footer", null, "elliot", default);
        _tasks.Items[0].Start(_clock.GetUtcNow());
        _tasks.Items[0].SubmitForReview(_clock.GetUtcNow());

        await _commands.SendBackAsync(id, "elliot", "Wrong colour.", default);

        var note = Assert.Single(_tasks.Items[0].Notes);
        Assert.Equal("elliot", note.Author.Name);
        Assert.Equal(WorkTaskStatus.Queued, _tasks.Items[0].Status);
    }

    [Fact]
    public async Task Unknown_office_or_task_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _commands.AssignAsync(OfficeId.New(), _desk.Id, "Anything", null, "elliot", default));
        await Assert.ThrowsAsync<NotFoundException>(() => _commands.DiscardAsync(WorkTaskId.New(), default));
    }

    [Fact]
    public async Task Accepting_commits_the_agent_work_on_its_branch()
    {
        var h = new Harness();
        var task = h.Enqueue();
        await h.Runner().RunQueueAsync(default);
        await h.Signals().FinishedAsync("ses_1", default);

        await h.Commands().AcceptAsync(task.Id, default);

        Assert.Equal(WorkTaskStatus.Done, task.Status);
        Assert.Equal((task.Run!.WorkspacePath, "Add a logout button"), Assert.Single(h.Workspaces.Commits));
        Assert.Contains(task.Run.Branch, task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discarding_a_running_task_stops_the_agent_and_cleans_up()
    {
        var h = new Harness();
        var task = h.Enqueue();
        await h.Runner().RunQueueAsync(default);

        await h.Commands().DiscardAsync(task.Id, default);

        Assert.Equal(WorkTaskStatus.Discarded, task.Status);
        Assert.Equal(["ses_1"], h.Runtime.Aborted);
        Assert.Single(h.Workspaces.Discarded);
    }

    [Fact]
    public async Task Answering_a_permission_nobody_asked_for_is_refused()
    {
        var h = new Harness();
        var task = h.Enqueue();
        await h.Runner().RunQueueAsync(default);

        await Assert.ThrowsAsync<DomainException>(() => h.Commands().ReplyToPermissionAsync(task.Id, true, default));
    }

    [Fact]
    public async Task Accepting_or_discarding_clears_the_desk_bubble()
    {
        var h = new Harness();
        var accepted = h.Enqueue("First");
        await h.Runner().RunQueueAsync(default);
        await h.Signals().FinishedAsync("ses_1", default);

        await h.Commands().AcceptAsync(accepted.Id, default);
        var afterAccept = h.Activity.ForTask(accepted.Id.Value);

        var discarded = h.Enqueue("Second");
        await h.Runner().RunQueueAsync(default);
        await h.Commands().DiscardAsync(discarded.Id, default);
        var afterDiscard = h.Activity.ForTask(discarded.Id.Value);

        Assert.Equal((ActivityKind.Stopped, string.Empty), (afterAccept?.Kind, afterAccept?.Headline));
        Assert.Equal((ActivityKind.Stopped, string.Empty), (afterDiscard?.Kind, afterDiscard?.Headline));
    }

    private static async Task<(Harness H, WorkTask Task)> AcceptedAsync()
    {
        var h = new Harness();
        var task = h.Enqueue();
        await h.Runner().RunQueueAsync(default);
        await h.Signals().FinishedAsync("ses_1", default);
        await h.Commands().AcceptAsync(task.Id, default);
        return (h, task);
    }

    [Fact]
    public async Task Merging_brings_the_task_branch_into_the_chosen_branch()
    {
        var (h, task) = await AcceptedAsync();

        await h.Commands().MergeAsync(task.Id, "develop", default);

        Assert.Equal((task.Run!.Branch, "develop"), Assert.Single(h.Workspaces.Merges));
        Assert.Equal("develop", task.MergedInto);
        Assert.Contains("Merged into develop", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_merge_changes_nothing()
    {
        var (h, task) = await AcceptedAsync();
        var saves = h.UnitOfWork.Saves;
        h.Workspaces.MergeProblem = new RejectedException("merge-conflict", "The work conflicts with main.");

        var error = await Assert.ThrowsAsync<RejectedException>(() => h.Commands().MergeAsync(task.Id, "main", default));

        Assert.Equal("merge-conflict", error.Code);
        Assert.Equal(saves, h.UnitOfWork.Saves);
    }
}
