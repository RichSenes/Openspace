using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class ProcedureFlowTests
{
    private readonly Harness _h = new();

    public ProcedureFlowTests() =>
        _h.Office.SetRoomProcedure(_h.Room.Id, ["Copy every file you change to /backup first", "Log each change in CHANGES.md"], Harness.Start);

    private async Task<WorkTask> RunningTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public async Task A_started_task_takes_the_room_procedure_into_its_brief()
    {
        var task = await RunningTaskAsync();

        Assert.Equal(2, task.Procedure.Count);
        var brief = Assert.Single(_h.Runtime.Prompts).Text;
        Assert.Contains("1. Copy every file you change to /backup first", brief, StringComparison.Ordinal);
        Assert.Contains("complete_step", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_the_room_later_does_not_change_a_task_already_started()
    {
        var task = await RunningTaskAsync();

        _h.Office.SetRoomProcedure(_h.Room.Id, ["Something new"], Harness.Start);

        Assert.Equal("Copy every file you change to /backup first", task.Procedure[0].Text);
    }

    [Fact]
    public async Task Stopping_with_missing_steps_sends_the_agent_back_to_them()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().CompleteStepAsync(task.Id, 1, "Copied header.php to /backup/2015-06-24/", default);

        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        var reminder = _h.Runtime.Prompts[^1].Text;
        Assert.Contains("2. Log each change in CHANGES.md", reminder, StringComparison.Ordinal);
        Assert.DoesNotContain("1. Copy", reminder, StringComparison.Ordinal);
        Assert.Contains("reminded it (1 of 2)", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_two_reminders_the_task_fails_instead_of_reaching_review()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().FinishedAsync("ses_1", default);
        await _h.Signals().FinishedAsync("ses_1", default);
        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("step 1, 2 still to do", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Equal(3, _h.Runtime.Prompts.Count);
    }

    [Fact]
    public async Task With_every_step_reported_the_work_goes_to_review()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().CompleteStepAsync(task.Id, 1, "Copied header.php to /backup/2015-06-24/", default);
        await _h.Signals().CompleteStepAsync(task.Id, 2, "Added a line to CHANGES.md", default);

        await _h.Signals().FinishedAsync("ses_1", default);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.All(task.Procedure, s => Assert.True(s.IsDone));
    }

    [Fact]
    public async Task Steps_cannot_be_reported_for_a_task_that_is_not_running()
    {
        var queued = _h.Enqueue();

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.Signals().CompleteStepAsync(queued.Id, 1, "done", default));

        Assert.Equal("task-not-running", error.Code);
    }
}
