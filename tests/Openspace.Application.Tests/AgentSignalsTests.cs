using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class AgentSignalsTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> RunningTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public async Task Progress_updates_the_desk_bubble()
    {
        await RunningTaskAsync();

        await _h.Signals().ProgressAsync("ses_1", "Adding the logout button to the header", null, default);
        await _h.Signals().ProgressAsync("ses_1", null, "Editing header.php", default);

        var activity = Assert.Single(_h.Activity.Snapshot());
        Assert.Equal("Adding the logout button to the header", activity.Step);
        Assert.Equal("Editing header.php", activity.Doing);
        Assert.Equal("Adding the logout button to the header", activity.Headline);
    }

    [Fact]
    public async Task Finishing_moves_the_task_to_review_with_the_agent_summary()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        var note = task.Notes[^1];
        Assert.Equal((NoteAuthorKind.Desk, "Added the button."), (note.Author.Kind, note.Text));
        Assert.Equal(ActivityKind.Stopped, _h.Activity.ForTask(task.Id.Value)?.Kind);
    }

    [Fact]
    public async Task An_agent_error_fails_the_task_with_the_reason()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().FailedAsync("ses_1", "The agent stopped with an error: rate limited", default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("rate limited", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_permission_request_makes_the_desk_wait_for_you()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash rm -rf build", default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Waiting, "per_1"), (activity?.Kind, activity?.PermissionId));
    }

    [Fact]
    public async Task Answering_a_permission_forwards_it_and_resumes_the_bubble()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash rm -rf build", default);

        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: true, default);

        Assert.Equal(("per_1", true), Assert.Single(_h.Runtime.PermissionReplies));
        Assert.Equal(ActivityKind.Working, _h.Activity.ForTask(task.Id.Value)?.Kind);
    }

    [Fact]
    public async Task Signals_for_unknown_or_finished_sessions_are_ignored()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().FinishedAsync("ses_1", default);
        var notes = task.Notes.Count;

        await _h.Signals().FinishedAsync("ses_1", default);
        await _h.Signals().FailedAsync("ses_someone_else", "boom", default);
        await _h.Signals().ProgressAsync("ses_someone_else", "Hacking the Gibson", null, default);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.Equal(notes, task.Notes.Count);
    }

    [Fact]
    public async Task A_wait_for_permission_survives_the_blocked_tool_reporting_itself_as_running()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash ls -la", default);

        // OpenCode reports the very tool that waits as running, right after asking.
        await _h.Signals().ProgressAsync("ses_1", null, "Running ls -la", default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Waiting, "per_1", "Wants to bash ls -la"), (activity?.Kind, activity?.PermissionId, activity?.Doing));
    }

    [Fact]
    public async Task A_permission_answered_in_OpenCode_itself_ends_the_wait()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash ls -la", default);

        await _h.Signals().PermissionRepliedAsync("ses_1", "per_1", allowed: false, default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Working, null, "Permission refused"), (activity?.Kind, activity?.PermissionId, activity?.Doing));
    }

    [Fact]
    public async Task Finishing_or_failing_still_ends_a_wait()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash ls -la", default);

        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(ActivityKind.Stopped, _h.Activity.ForTask(task.Id.Value)?.Kind);
    }

    [Fact]
    public async Task Two_requests_at_once_are_answered_one_after_the_other()
    {
        var task = await RunningTaskAsync();
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_1", "ses_1", "bash ls -la ../original"));
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_2", "ses_1", "bash pwd, git status"));
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash ls -la ../original", default);
        await _h.Signals().PermissionAskedAsync("ses_1", "per_2", "bash pwd, git status", default);

        var first = _h.Activity.ForTask(task.Id.Value);
        _h.Runtime.Pending.RemoveAt(0);
        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: true, default);
        var second = _h.Activity.ForTask(task.Id.Value);
        _h.Runtime.Pending.Clear();
        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: true, default);
        var done = _h.Activity.ForTask(task.Id.Value);

        Assert.Equal((ActivityKind.Waiting, "per_1"), (first?.Kind, first?.PermissionId));
        Assert.Equal((ActivityKind.Waiting, "per_2", "Wants to bash pwd, git status"), (second?.Kind, second?.PermissionId, second?.Doing));
        Assert.Equal((ActivityKind.Working, null), (done?.Kind, done?.PermissionId));
        Assert.Equal([("per_1", true), ("per_2", true)], _h.Runtime.PermissionReplies);
    }

    [Fact]
    public async Task An_answer_given_in_OpenCode_for_another_request_does_not_end_the_current_wait()
    {
        var task = await RunningTaskAsync();
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_1", "ses_1", "bash ls"));
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash ls", default);

        await _h.Signals().PermissionRepliedAsync("ses_1", "per_old", allowed: true, default);

        Assert.Equal((ActivityKind.Waiting, "per_1"), (_h.Activity.ForTask(task.Id.Value)?.Kind, _h.Activity.ForTask(task.Id.Value)?.PermissionId));
    }
}
