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

        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("rm -rf build"), default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Waiting, "per_1"), (activity?.Kind, activity?.PermissionId));
    }

    [Fact]
    public async Task Answering_a_permission_forwards_it_and_resumes_the_bubble()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("rm -rf build"), default);

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
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("ls -la"), default);

        // OpenCode reports the very tool that waits as running, right after asking.
        await _h.Signals().ProgressAsync("ses_1", null, "Running ls -la", default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Waiting, "per_1", "Can I look through the files?"), (activity?.Kind, activity?.PermissionId, activity?.Doing));
    }

    [Fact]
    public async Task A_permission_answered_in_OpenCode_itself_ends_the_wait()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("ls -la"), default);

        await _h.Signals().PermissionRepliedAsync("ses_1", "per_1", allowed: false, default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Working, null, "Permission refused"), (activity?.Kind, activity?.PermissionId, activity?.Doing));
    }

    [Fact]
    public async Task Finishing_or_failing_still_ends_a_wait()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("ls -la"), default);

        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(ActivityKind.Stopped, _h.Activity.ForTask(task.Id.Value)?.Kind);
    }

    [Fact]
    public async Task Two_requests_at_once_are_answered_one_after_the_other()
    {
        var task = await RunningTaskAsync();
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_1", "ses_1", Fakes.Run("ls -la ../original")));
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_2", "ses_1", Fakes.Run("pwd && git status")));
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("ls -la ../original"), default);
        await _h.Signals().PermissionAskedAsync("ses_1", "per_2", Fakes.Run("pwd && git status"), default);

        var first = _h.Activity.ForTask(task.Id.Value);
        _h.Runtime.Pending.RemoveAt(0);
        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: true, default);
        var second = _h.Activity.ForTask(task.Id.Value);
        _h.Runtime.Pending.Clear();
        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: true, default);
        var done = _h.Activity.ForTask(task.Id.Value);

        Assert.Equal((ActivityKind.Waiting, "per_1"), (first?.Kind, first?.PermissionId));
        Assert.Equal((ActivityKind.Waiting, "per_2", "Can I look at the changes in git?"), (second?.Kind, second?.PermissionId, second?.Doing));
        Assert.Equal((ActivityKind.Working, null), (done?.Kind, done?.PermissionId));
        Assert.Equal([("per_1", true), ("per_2", true)], _h.Runtime.PermissionReplies);
    }

    [Fact]
    public async Task An_answer_given_in_OpenCode_for_another_request_does_not_end_the_current_wait()
    {
        var task = await RunningTaskAsync();
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_1", "ses_1", Fakes.Run("ls")));
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("ls"), default);

        await _h.Signals().PermissionRepliedAsync("ses_1", "per_old", allowed: true, default);

        Assert.Equal((ActivityKind.Waiting, "per_1"), (_h.Activity.ForTask(task.Id.Value)?.Kind, _h.Activity.ForTask(task.Id.Value)?.PermissionId));
    }

    [Fact]
    public async Task In_auto_mode_Openspace_answers_a_safe_request_itself()
    {
        _h.Autonomy = Autonomy.Auto;
        var task = await RunningTaskAsync();

        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("cd web && npm ci"), default);

        Assert.Equal(("per_1", true), Assert.Single(_h.Runtime.PermissionReplies));
        Assert.NotEqual(ActivityKind.Waiting, _h.Activity.ForTask(task.Id.Value)?.Kind);
        Assert.Equal("Allowed on its own: install the dependencies · cd web && npm ci", Assert.Single(_h.Audit.Items).Summary);
    }

    [Fact]
    public async Task In_auto_mode_a_risky_request_still_waits_and_says_why()
    {
        _h.Autonomy = Autonomy.Auto;
        var task = await RunningTaskAsync();

        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("git push origin main"), default);

        var activity = _h.Activity.ForTask(task.Id.Value);
        Assert.Equal((ActivityKind.Waiting, "Can I push to the remote?", "git push origin main"), (activity?.Kind, activity?.Doing, activity?.Detail));
        Assert.Empty(_h.Runtime.PermissionReplies);
        Assert.Contains("A person decides because it changes the repository", Assert.Single(_h.Audit.Items).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_an_answer_only_requests_that_need_a_person_are_shown()
    {
        _h.Autonomy = Autonomy.Auto;
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", Fakes.Run("npx wrangler deploy"), default);
        _h.Runtime.Pending.Add(new Abstractions.PermissionRequest("per_2", "ses_1", Fakes.Run("npm test")));

        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: false, default);

        Assert.Equal(ActivityKind.Working, _h.Activity.ForTask(task.Id.Value)?.Kind);
    }

    [Fact]
    public async Task Claude_Code_asking_through_Openspace_gets_a_safe_answer_at_once_in_auto_mode()
    {
        _h.Autonomy = Autonomy.Auto;
        var task = await RunningTaskAsync();

        Assert.True(await _h.Signals().RequestPermissionAsync(task.Id, new AgentAction(ActionKind.Edit, $"{task.Run!.WorkspacePath}/header.php"), default));
        Assert.Empty(_h.Permissions.Pending(task.Run.WorkspacePath));
    }

    [Fact]
    public async Task Tokens_count_on_the_round_even_once_the_work_is_in_review()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().UsedAsync("ses_1", new TokenCount(1200, 300, 5000, 800), default);
        await _h.Signals().FinishedAsync("ses_1", default);

        await _h.Signals().UsedAsync("ses_1", new TokenCount(100, 20, 0, 0), default);
        await _h.Signals().UsedAsync("ses_someone_else", new TokenCount(9, 9, 9, 9), default);

        Assert.Equal(new TokenCount(1300, 320, 5000, 800), Assert.Single(task.Usage).Tokens);
    }

    [Fact]
    public async Task An_agent_lists_checks_to_try_by_hand_once_each()
    {
        var task = await RunningTaskAsync();

        Assert.Equal(1, await _h.Signals().AddManualCheckAsync(task.Id, "The banner shows on a phone", "Open the home page at 360px wide.", default));
        Assert.Equal(1, await _h.Signals().AddManualCheckAsync(task.Id, "the banner shows on a phone", "Again.", default));

        Assert.Equal("Open the home page at 360px wide.", Assert.Single(task.ManualChecks).How);
    }
}
