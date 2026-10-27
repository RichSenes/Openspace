using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class LoopGuardTests
{
    private readonly Harness _h = new();
    private int _calls;

    private async Task<WorkTask> RunningAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    private Task FailEditAsync(WorkTask task) => _h.Signals().ToolFinishedAsync(
        task.Run!.SessionId, $"call_{++_calls}", AuditKind.Change, "Changed header.php",
        "header.php\nNo changes to apply: oldString and newString are identical.", false, default);

    private Task EditAsync(WorkTask task) => _h.Signals().ToolFinishedAsync(
        task.Run!.SessionId, $"call_{++_calls}", AuditKind.Change, "Changed header.php", "+ <button>Log out</button>", true, default);

    private async Task LoopAsync(WorkTask task)
    {
        for (var i = 0; i < LoopGuard.RepeatedFailures; i++)
        {
            await FailEditAsync(task);
        }
    }

    [Fact]
    public async Task The_same_failure_again_and_again_starts_the_agent_over_in_a_fresh_conversation()
    {
        var task = await RunningAsync();
        var first = task.Run!.SessionId;

        await LoopAsync(task);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal([first], _h.Runtime.Aborted);
        Assert.Contains("tried the same thing 4 times", task.Notes[^1].Text, StringComparison.Ordinal);

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.NotEqual(first, task.Run!.SessionId);
        Assert.Single(_h.Workspaces.Prepared);
        var (_, _, brief) = _h.Runtime.Prompts[^1];
        Assert.Contains("Openspace stopped because it tried the same thing 4 times", brief, StringComparison.Ordinal);
        Assert.Contains("oldString and newString are identical", brief, StringComparison.Ordinal);
        Assert.Contains("git diff", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_that_lands_in_between_is_progress_not_a_loop()
    {
        var task = await RunningAsync();

        await FailEditAsync(task);
        await FailEditAsync(task);
        await EditAsync(task);
        await FailEditAsync(task);
        await FailEditAsync(task);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Empty(_h.Runtime.Aborted);
    }

    [Fact]
    public async Task A_conversation_that_keeps_filling_up_starts_over_too()
    {
        var task = await RunningAsync();

        await _h.Signals().CompactedAsync(task.Run!.SessionId, default);
        await _h.Signals().CompactedAsync(task.Run!.SessionId, default);
        Assert.Equal(WorkTaskStatus.Running, task.Status);

        await _h.Signals().CompactedAsync(task.Run!.SessionId, default);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Contains("filled up 3 times", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_its_fresh_starts_a_looping_task_goes_to_review_with_its_work_kept()
    {
        var task = await RunningAsync();
        for (var i = 0; i < LoopGuard.FreshStarts; i++)
        {
            await LoopAsync(task);
            await _h.Runner().RunQueueAsync(default);
        }

        await LoopAsync(task);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.Contains("after 2 fresh starts already", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Empty(_h.Workspaces.Discarded);
        Assert.Equal(3, _h.Runtime.Aborted.Count);
    }

    [Fact]
    public async Task A_new_round_gets_its_fresh_starts_again()
    {
        var task = await RunningAsync();
        for (var i = 0; i < LoopGuard.FreshStarts; i++)
        {
            await LoopAsync(task);
            await _h.Runner().RunQueueAsync(default);
        }

        await LoopAsync(task);
        task.SendBack(NoteAuthor.Human("elliot"), "Keep the button, drop the animation.", _h.Clock.GetUtcNow());
        await _h.Runner().RunQueueAsync(default);
        await LoopAsync(task);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
    }

    [Fact]
    public async Task Stopping_by_hand_keeps_the_work_for_review()
    {
        var task = await RunningAsync();

        await _h.Commands().StopAsync(task.Id, default);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.Equal([task.Run!.SessionId], _h.Runtime.Aborted);
        Assert.Empty(_h.Workspaces.Discarded);
        Assert.Contains("Stopped by a person", task.Notes[^1].Text, StringComparison.Ordinal);
    }
}
