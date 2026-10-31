using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Abstractions;
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
        var signals = new Agents.AgentSignals(_offices, new Agents.AgentSetups(new InMemoryRoles()), _tasks, _unitOfWork, runtime, board, new Agents.ContextBoard(new RecordingPublisher()), audit, new RecordingPublisher(), new Review.PendingReviews(), new Agents.PermissionRequests(), new LiveOptions<Agents.AgentOptions>(() => new() { Autonomy = Agents.Autonomy.Ask }), new Agents.EngineLimits(_clock), new Agents.LoopGuard(), _clock);
        _commands = new WorkTaskCommands(_offices, _tasks, _unitOfWork, runtime, new FakeWorkspaces(), board, audit, signals, new FakePullRequests(), _clock);
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

        await _commands.SendBackAsync(id, "elliot", "Wrong colour.", withReview: false, default);

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

    [Fact]
    public async Task Merging_everything_goes_in_order_of_acceptance_and_skips_what_conflicts()
    {
        var h = new Harness();
        var tasks = new List<WorkTask>();
        foreach (var title in new[] { "First", "Second", "Third" })
        {
            var task = h.Enqueue(title);
            await h.Runner().RunQueueAsync(default);
            await h.Signals().FinishedAsync(task.Run!.SessionId, default);
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            await h.Commands().AcceptAsync(task.Id, default);
            tasks.Add(task);
        }

        h.Workspaces.Conflicting.Add(tasks[1].Run!.Branch);

        var outcomes = await h.Commands().MergeAllAsync(h.Office.Id, "main", null, default);

        Assert.Equal(
            [("First", MergeResult.Merged), ("Second", MergeResult.Conflict), ("Third", MergeResult.Merged)],
            outcomes.Select(o => (o.Title, o.Result)));
        Assert.Contains("src/cart.ts", outcomes[1].Message, StringComparison.Ordinal);
        Assert.Equal(("main", (string?)null, "main"), (tasks[0].MergedInto, tasks[1].MergedInto, tasks[2].MergedInto));
    }

    [Fact]
    public async Task Your_uncommitted_changes_stop_the_whole_round()
    {
        var (h, task) = await AcceptedAsync();
        var other = h.Enqueue("Second");
        await h.Runner().RunQueueAsync(default);
        await h.Signals().FinishedAsync(other.Run!.SessionId, default);
        await h.Commands().AcceptAsync(other.Id, default);
        h.Workspaces.MergeProblem = new RejectedException("uncommitted-changes", "Commit or stash them.");

        var outcomes = await h.Commands().MergeAllAsync(h.Office.Id, "main", null, default);

        Assert.Equal([MergeResult.Failed, MergeResult.NotTried], outcomes.Select(o => o.Result));
        Assert.Null(task.MergedInto);
    }

    [Fact]
    public async Task Only_the_chosen_tasks_are_merged()
    {
        var (h, task) = await AcceptedAsync();

        var outcomes = await h.Commands().MergeAllAsync(h.Office.Id, "main", [WorkTaskId.New()], default);

        Assert.Empty(outcomes);
        Assert.Null(task.MergedInto);
    }

    [Fact]
    public async Task Conflicting_work_goes_back_to_its_agent_to_catch_up()
    {
        var (h, task) = await AcceptedAsync();

        await h.Commands().CatchUpAsync(task.Id, "main", "elliot", default);

        Assert.Equal((WorkTaskStatus.Queued, 2), (task.Status, task.Round));
        var (workspace, target) = Assert.Single(h.Workspaces.CatchUps);
        Assert.Equal((task.Run!.WorkspacePath, task.Run.Branch, "main"), (workspace.Path, workspace.Branch, target));

        await h.Runner().RunQueueAsync(default);
        var (session, _, text) = h.Runtime.Prompts[^1];
        Assert.Equal("ses_1", session);
        Assert.Contains("Resolve the conflicts marked in: src/cart.ts.", text, StringComparison.Ordinal);
        Assert.Contains("do not abort the merge", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merged_work_cannot_be_sent_to_catch_up()
    {
        var (h, task) = await AcceptedAsync();
        await h.Commands().MergeAsync(task.Id, "main", default);

        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => h.Commands().CatchUpAsync(task.Id, "main", "elliot", default));
        Assert.Empty(h.Workspaces.CatchUps);
    }

    [Fact]
    public async Task A_task_waits_in_the_queue_until_what_it_builds_on_is_accepted_and_merged()
    {
        var h = new Harness();
        var analysis = h.Enqueue("Analyse the checkout");
        var fix = h.Enqueue("Fix the checkout");
        await h.Commands().SetDependenciesAsync(fix.Id, [analysis.Id], default);

        await h.Runner(seats: 2).RunQueueAsync(default);
        Assert.Equal((WorkTaskStatus.Running, WorkTaskStatus.Queued), (analysis.Status, fix.Status));

        await h.Signals().FinishedAsync(analysis.Run!.SessionId, default);
        await h.Commands().AcceptAsync(analysis.Id, default);
        await h.Runner(seats: 2).RunQueueAsync(default);
        Assert.Equal(WorkTaskStatus.Queued, fix.Status);

        await h.Commands().MergeAsync(analysis.Id, "main", default);
        await h.Runner(seats: 2).RunQueueAsync(default);
        Assert.Equal(WorkTaskStatus.Running, fix.Status);
        Assert.Contains("builds on work already done and merged", h.Runtime.Prompts[^1].Text, StringComparison.Ordinal);
        Assert.Contains("- Analyse the checkout: Added the button.", h.Runtime.Prompts[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Waiting_in_a_loop_is_refused()
    {
        var h = new Harness();
        var (a, b, c) = (h.Enqueue("A"), h.Enqueue("B"), h.Enqueue("C"));
        await h.Commands().SetDependenciesAsync(b.Id, [a.Id], default);
        await h.Commands().SetDependenciesAsync(c.Id, [b.Id], default);

        var loop = await Assert.ThrowsAsync<DomainException>(() => h.Commands().SetDependenciesAsync(a.Id, [c.Id], default));

        Assert.Contains("none could ever start", loop.Message, StringComparison.Ordinal);
        Assert.Empty(a.DependsOn);
        await Assert.ThrowsAsync<DomainException>(() => h.Commands().SetDependenciesAsync(a.Id, [a.Id], default));
    }

    [Fact]
    public async Task Accepted_work_goes_up_as_a_pull_request_with_the_agent_s_summary_and_checks()
    {
        var (h, task) = await AcceptedAsync();

        var url = await h.Commands().OpenPullRequestAsync(task.Id, "main", default);

        Assert.Equal([task.Run!.Branch], h.Workspaces.Pushed);
        var (branch, target, title, body) = Assert.Single(h.PullRequests.Opened);
        Assert.Equal((task.Run.Branch, "main", task.Title), (branch, target, title));
        Assert.Contains("## What the agent did", body, StringComparison.Ordinal);
        Assert.Equal(url, task.PullRequestUrl);
        Assert.Empty(await h.Commands().MergeAllAsync(h.Office.Id, "main", null, default));
        await Assert.ThrowsAsync<DomainException>(() => h.Commands().MergeAsync(task.Id, "main", default));
    }

    [Fact]
    public async Task A_pull_request_merged_on_GitHub_counts_as_merged_and_the_branch_here_is_updated()
    {
        var (h, task) = await AcceptedAsync();
        await h.Commands().OpenPullRequestAsync(task.Id, "main", default);
        h.PullRequests.State = PullRequestState.Merged;

        Assert.Equal(PullRequestState.Merged, await h.Commands().CheckPullRequestAsync(task.Id, default));

        Assert.Equal(("main", (string?)null, true), (task.MergedInto, task.PullRequestUrl, task.IsDoneForOthers));
        Assert.Equal(["main"], h.Workspaces.Updated);
        Assert.StartsWith("Merged on GitHub into main", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pull_request_closed_unmerged_lets_the_work_be_merged_again()
    {
        var (h, task) = await AcceptedAsync();
        await h.Commands().OpenPullRequestAsync(task.Id, "main", default);
        h.PullRequests.State = PullRequestState.Closed;

        await h.Commands().CheckPullRequestAsync(task.Id, default);

        Assert.Null(task.PullRequestUrl);
        Assert.True(task.AwaitsMerge);
    }

    [Fact]
    public async Task An_office_kept_local_never_pushes_nor_opens_pull_requests()
    {
        var (h, task) = await AcceptedAsync();
        h.Office.KeepLocal(true, Start);

        var push = await Assert.ThrowsAsync<RejectedException>(() => h.Commands().PushAsync(h.Office.Id, "main", default));
        var pullRequest = await Assert.ThrowsAsync<RejectedException>(() => h.Commands().OpenPullRequestAsync(task.Id, "main", default));

        Assert.Equal(("local-only", "local-only"), (push.Code, pullRequest.Code));
        Assert.Empty(h.Workspaces.Pushed);
        Assert.Empty(h.PullRequests.Opened);
    }

    [Fact]
    public async Task A_branch_is_brought_into_another_only_in_an_office_with_a_repository()
    {
        var h = new Harness();

        Assert.Equal(BranchMerge.FastForward, await h.Commands().MergeBranchAsync(h.Office.Id, " beta ", "main", default));
        Assert.Equal(("beta", "main"), Assert.Single(h.Workspaces.BranchMerges));

        var bare = new Harness(withRepository: false);
        var error = await Assert.ThrowsAsync<RejectedException>(() => bare.Commands().MergeBranchAsync(bare.Office.Id, "beta", "main", default));
        Assert.Equal("no-repository", error.Code);
        Assert.Empty(bare.Workspaces.BranchMerges);
    }

    [Fact]
    public async Task Without_GitHub_signed_in_no_pull_request_is_tried()
    {
        var (h, task) = await AcceptedAsync();
        h.PullRequests.Available = false;

        var error = await Assert.ThrowsAsync<RejectedException>(() => h.Commands().OpenPullRequestAsync(task.Id, "main", default));

        Assert.Equal("no-pull-requests", error.Code);
        Assert.Empty(h.Workspaces.Pushed);
    }
}
