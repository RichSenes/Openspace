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
}
