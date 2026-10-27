using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class ProcedureTests
{
    private static readonly string[] Backup = ["Copy every file you change to /backup first", "Log each change in CHANGES.md"];

    private static WorkTask RunningWithProcedure()
    {
        var (office, desk) = ECorpWithDesk();
        var task = WorkTask.Assign(office, desk.Id, "Patch the header", null, "elliot", Now);
        task.Start(Now);
        task.AdoptProcedure(Backup);
        return task;
    }

    [Fact]
    public void Rooms_keep_a_clean_procedure_and_raise_an_event()
    {
        var office = ECorp();
        var room = office.OpenRoom("Servers", Now);

        office.SetRoomProcedure(room.Id, ["  Back up first ", "", "Log the change"], Now);

        Assert.Equal(["Back up first", "Log the change"], room.Procedure);
        Assert.IsType<RoomProcedureSet>(office.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => office.SetRoomProcedure(room.Id, Enumerable.Repeat("step", 21), Now));
    }

    [Fact]
    public void A_task_adopts_the_procedure_once_numbered_from_one()
    {
        var task = RunningWithProcedure();

        task.AdoptProcedure(["Something else"]);

        Assert.Equal([(1, Backup[0]), (2, Backup[1])], task.Procedure.Select(s => (s.Number, s.Text)));
        Assert.Equal(2, task.MissingSteps.Count);
    }

    [Fact]
    public void Steps_are_completed_with_evidence_and_raise_an_event()
    {
        var task = RunningWithProcedure();

        task.CompleteStep(2, "Added a line to CHANGES.md", Now);

        Assert.Equal("Added a line to CHANGES.md", task.Procedure[1].Evidence);
        Assert.Equal([1], task.MissingSteps.Select(s => s.Number));
        Assert.IsType<WorkTaskStepCompleted>(task.DomainEvents[^1]);
    }

    [Fact]
    public void Unknown_steps_and_empty_evidence_are_refused()
    {
        var task = RunningWithProcedure();

        Assert.Throws<DomainException>(() => task.CompleteStep(3, "done", Now));
        Assert.Throws<DomainException>(() => task.CompleteStep(1, " ", Now));
    }

    [Fact]
    public void Work_with_missing_steps_cannot_go_to_review()
    {
        var task = RunningWithProcedure();
        task.CompleteStep(1, "Copied header.php to /backup/2015-06-24/", Now);

        var error = Assert.Throws<DomainException>(() => task.SubmitForReview(Now));
        task.CompleteStep(2, "Logged in CHANGES.md", Now);
        task.SubmitForReview(Now);

        Assert.Contains("step 2", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkTaskStatus.Review, task.Status);
    }

    [Fact]
    public void Reminders_run_out_and_start_over_on_the_next_run()
    {
        var task = RunningWithProcedure();

        var first = task.RemindOfProcedure(2);
        var second = task.RemindOfProcedure(2);
        var third = task.RemindOfProcedure(2);
        task.Requeue(Now);
        task.Start(Now);

        Assert.Equal((true, true, false), (first, second, third));
        Assert.Equal(0, task.ProcedureReminders);
    }

    [Fact]
    public void An_interrupted_task_goes_to_review_as_it_is_with_steps_still_missing()
    {
        var task = RunningWithProcedure();

        task.Interrupt("Stopped by a person: the work so far is kept.", Now);

        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.Equal(2, task.MissingSteps.Count);
        Assert.Equal("Stopped by a person: the work so far is kept.", task.Notes[^1].Text);
    }

    [Fact]
    public void Only_a_running_task_can_be_interrupted()
    {
        var (office, desk) = ECorpWithDesk();
        var task = WorkTask.Assign(office, desk.Id, "Patch the header", null, "elliot", Now);

        Assert.Throws<DomainException>(() => task.Interrupt("Stopped.", Now));
    }
}
