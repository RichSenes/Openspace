using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

public class WorkTaskTests
{
    private const string Requester = "elliot";

    private static WorkTask QueuedTask(out Office office, out Desk desk)
    {
        (office, desk) = ECorpWithDesk();
        return WorkTask.Assign(office, desk.Id, "Add a logout button to the header", null, Requester, Now);
    }

    private static WorkTask TaskIn(WorkTaskStatus status)
    {
        if (status == WorkTaskStatus.Proposed)
        {
            return WorkTask.Propose(ECorp(), "Rotate the certificates", null, Requester, Now);
        }

        var task = QueuedTask(out _, out _);
        switch (status)
        {
            case WorkTaskStatus.Queued:
                break;
            case WorkTaskStatus.Running:
                task.Start(Now);
                break;
            case WorkTaskStatus.Review:
                task.Start(Now);
                task.SubmitForReview(Now);
                break;
            case WorkTaskStatus.Failed:
                task.Start(Now);
                task.Fail("Tests did not compile.", Now);
                break;
            case WorkTaskStatus.Done:
                task.Start(Now);
                task.SubmitForReview(Now);
                task.Accept(Now);
                break;
            case WorkTaskStatus.Discarded:
                task.Discard(Now);
                break;
        }

        return task;
    }

    [Fact]
    public void Assigning_a_task_puts_it_straight_in_the_queue()
    {
        var task = QueuedTask(out var office, out var desk);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(desk.Id, task.DeskId);
        Assert.Equal(office.Id, task.OfficeId);
        Assert.IsType<WorkTaskCreated>(task.DomainEvents[0]);
        var queued = Assert.IsType<WorkTaskStatusChanged>(task.DomainEvents[^1]);
        Assert.Equal(WorkTaskStatus.Queued, queued.To);
    }

    [Fact]
    public void A_proposed_task_needs_a_desk_before_approval()
    {
        var (office, desk) = ECorpWithDesk();
        var task = WorkTask.Propose(office, "Harden the login page", "See the audit", Requester, Now);

        Assert.Throws<DomainException>(() => task.Approve(Now));

        task.AssignTo(office, desk.Id);
        task.Approve(Now);
        Assert.Equal(WorkTaskStatus.Queued, task.Status);
    }

    [Fact]
    public void Happy_path_ends_in_done()
    {
        var task = QueuedTask(out _, out _);

        task.Start(Now);
        task.SubmitForReview(Now);
        task.Accept(Now);

        Assert.Equal(WorkTaskStatus.Done, task.Status);
        Assert.True(task.IsClosed);
    }

    [Fact]
    public void Failing_records_the_reason_as_a_system_note()
    {
        var task = TaskIn(WorkTaskStatus.Failed);

        var note = Assert.Single(task.Notes);
        Assert.Equal(NoteAuthorKind.System, note.Author.Kind);
        Assert.Equal("Tests did not compile.", note.Text);
    }

    [Fact]
    public void Sending_back_requeues_with_the_reviewer_feedback()
    {
        var task = TaskIn(WorkTaskStatus.Review);

        task.SendBack(NoteAuthor.Human(Requester), "The button is on the wrong side.", Now);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(NoteAuthorKind.Human, task.Notes[^1].Author.Kind);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Running)]
    [InlineData(WorkTaskStatus.Failed)]
    public void Running_and_failed_tasks_can_be_requeued(WorkTaskStatus status)
    {
        var task = TaskIn(status);

        task.Requeue(Now);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Proposed)]
    [InlineData(WorkTaskStatus.Queued)]
    [InlineData(WorkTaskStatus.Running)]
    [InlineData(WorkTaskStatus.Review)]
    [InlineData(WorkTaskStatus.Failed)]
    public void Open_tasks_can_be_discarded(WorkTaskStatus status)
    {
        var task = TaskIn(status);

        task.Discard(Now);

        Assert.Equal(WorkTaskStatus.Discarded, task.Status);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Done)]
    [InlineData(WorkTaskStatus.Discarded)]
    public void Closed_tasks_cannot_move(WorkTaskStatus status)
    {
        var task = TaskIn(status);

        Assert.Throws<DomainException>(() => task.Discard(Now));
        Assert.Throws<DomainException>(() => task.Requeue(Now));
    }

    [Theory]
    [InlineData(WorkTaskStatus.Proposed)]
    [InlineData(WorkTaskStatus.Running)]
    [InlineData(WorkTaskStatus.Review)]
    public void Only_queued_tasks_can_start(WorkTaskStatus status)
    {
        Assert.Throws<DomainException>(() => TaskIn(status).Start(Now));
    }

    [Theory]
    [InlineData(WorkTaskStatus.Queued)]
    [InlineData(WorkTaskStatus.Running)]
    [InlineData(WorkTaskStatus.Failed)]
    public void Only_reviewed_tasks_can_be_accepted(WorkTaskStatus status)
    {
        Assert.Throws<DomainException>(() => TaskIn(status).Accept(Now));
    }

    [Fact]
    public void A_running_task_cannot_change_desk()
    {
        var task = QueuedTask(out var office, out var desk);
        task.Start(Now);

        Assert.Throws<DomainException>(() => task.AssignTo(office, desk.Id));
    }

    [Fact]
    public void A_task_cannot_go_to_a_desk_of_another_office()
    {
        var task = QueuedTask(out _, out _);
        var (otherOffice, otherDesk) = ECorpWithDesk();

        Assert.Throws<DomainException>(() => task.AssignTo(otherOffice, otherDesk.Id));
    }

    [Fact]
    public void A_task_cannot_go_to_an_unknown_desk()
    {
        var task = QueuedTask(out var office, out _);

        Assert.Throws<DomainException>(() => task.AssignTo(office, DeskId.New()));
    }

    [Fact]
    public void Agents_can_leave_notes()
    {
        var task = QueuedTask(out _, out var desk);
        task.Start(Now);

        task.AddNote(NoteAuthor.FromDesk(desk.Id), "Header lives in partials/header.php.", Now);

        var note = Assert.Single(task.Notes);
        Assert.Equal(desk.Id, note.Author.DeskId);
        Assert.IsType<WorkTaskNoteAdded>(task.DomainEvents[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Tasks_and_notes_need_text(string text)
    {
        var task = QueuedTask(out var office, out var desk);

        Assert.Throws<DomainException>(() => WorkTask.Assign(office, desk.Id, text, null, Requester, Now));
        Assert.Throws<DomainException>(() => task.AddNote(NoteAuthor.System, text, Now));
    }

    private static AgentRun SomeRun() => AgentRun.Create("ses_fsociety", "/srv/worktrees/t1", "openspace/t1");

    [Fact]
    public void A_running_task_records_its_agent_run()
    {
        var task = TaskIn(WorkTaskStatus.Running);

        task.AttachRun(SomeRun(), Now);

        Assert.Equal("ses_fsociety", task.Run?.SessionId);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Queued)]
    [InlineData(WorkTaskStatus.Review)]
    public void Only_running_tasks_get_an_agent_run(WorkTaskStatus status)
    {
        Assert.Throws<DomainException>(() => TaskIn(status).AttachRun(SomeRun(), Now));
    }

    [Fact]
    public void The_run_survives_a_send_back_so_the_agent_can_resume()
    {
        var task = TaskIn(WorkTaskStatus.Running);
        task.AttachRun(SomeRun(), Now);
        task.SubmitForReview(Now);

        task.SendBack(NoteAuthor.Human(Requester), "Wrong colour.", Now);

        Assert.NotNull(task.Run);
    }

    [Fact]
    public void A_task_with_a_run_cannot_move_to_another_desk()
    {
        var (office, desk) = ECorpWithDesk();
        var other = office.AddDesk(office.Rooms[0].Id, "Backend", AgentProfile.Create("backend-dev"), Now);
        var task = WorkTask.Assign(office, desk.Id, "Fix the footer", null, Requester, Now);
        task.Start(Now);
        task.AttachRun(SomeRun(), Now);
        task.Fail("Crashed.", Now);

        Assert.Throws<DomainException>(() => task.AssignTo(office, other.Id));
        task.AssignTo(office, desk.Id);
    }

    [Fact]
    public void Agent_run_needs_all_its_parts()
    {
        Assert.Throws<DomainException>(() => AgentRun.Create("", "/w", "b"));
        Assert.Throws<DomainException>(() => AgentRun.Create("s", " ", "b"));
        Assert.Throws<DomainException>(() => AgentRun.Create("s", "/w", ""));
    }

    [Fact]
    public void Accepted_agent_work_can_be_merged_once()
    {
        var task = TaskIn(WorkTaskStatus.Running);
        task.AttachRun(SomeRun(), Now);
        task.SubmitForReview(Now);
        task.Accept(Now);

        task.RecordMerge("main", Now);

        Assert.Equal("main", task.MergedInto);
        Assert.IsType<WorkTaskMerged>(task.DomainEvents[^1]);
        Assert.Throws<DomainException>(() => task.RecordMerge("develop", Now));
    }

    [Fact]
    public void Accepted_work_that_conflicts_goes_back_to_its_desk_as_a_new_round()
    {
        var task = TaskIn(WorkTaskStatus.Running);
        task.AttachRun(SomeRun(), Now);
        task.AddManualCheck("The cart shows the discount", null, Now);
        task.SubmitForReview(Now);
        task.TickManualCheck(1, true, Now);
        task.Accept(Now);

        task.ReopenToCatchUp(NoteAuthor.Human("elliot"), "Resolve the conflicts in src/cart.ts.", Now);

        Assert.Equal((WorkTaskStatus.Queued, 2, true), (task.Status, task.Round, task.AwaitsMerge is false));
        Assert.Null(task.ManualChecks[0].CheckedAt);
        Assert.Equal("Resolve the conflicts in src/cart.ts.", task.Notes[^1].Text);
    }

    [Fact]
    public void Merged_or_unaccepted_work_does_not_catch_up_and_done_work_is_not_approved_again()
    {
        var merged = TaskIn(WorkTaskStatus.Running);
        merged.AttachRun(SomeRun(), Now);
        merged.SubmitForReview(Now);
        merged.Accept(Now);
        merged.RecordMerge("main", Now);

        Assert.Throws<DomainException>(() => merged.ReopenToCatchUp(NoteAuthor.Human("elliot"), "Catch up.", Now));
        Assert.Throws<DomainException>(() => TaskIn(WorkTaskStatus.Review).ReopenToCatchUp(NoteAuthor.Human("elliot"), "Catch up.", Now));
        Assert.Throws<DomainException>(() => merged.Approve(Now));
    }

    [Fact]
    public void Work_that_is_not_accepted_or_has_no_agent_cannot_be_merged()
    {
        var review = TaskIn(WorkTaskStatus.Running);
        review.AttachRun(SomeRun(), Now);
        review.SubmitForReview(Now);

        Assert.Throws<DomainException>(() => review.RecordMerge("main", Now));
        Assert.Throws<DomainException>(() => TaskIn(WorkTaskStatus.Done).RecordMerge("main", Now));
    }

    [Fact]
    public void Tasks_proposed_together_still_get_keys_of_their_own()
    {
        var office = ECorp();

        // Proposed in the same instant, as the supervisor does: their ids start alike.
        var keys = Enumerable.Range(0, 12).Select(i => WorkTask.Propose(office, $"Task {i}", null, Requester, Now).Key).ToList();

        Assert.Equal(12, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Equal(10, k.Length));
    }

    [Fact]
    public void What_a_task_waits_for_is_set_before_it_starts_and_within_its_office()
    {
        var task = QueuedTask(out var office, out var desk);
        var other = WorkTask.Assign(office, desk.Id, "Analyse the certificates", null, Requester, Now);

        task.DependOn([other, other], Now);
        Assert.Equal([other.Id], task.DependsOn);
        Assert.IsType<WorkTaskDependenciesSet>(task.DomainEvents[^1]);

        Assert.Throws<DomainException>(() => task.DependOn([TaskIn(WorkTaskStatus.Queued)], Now));
        task.Start(Now);
        Assert.Throws<DomainException>(() => task.DependOn([other], Now));
        Assert.False(other.IsDoneForOthers);
    }

    [Fact]
    public void Only_a_queued_task_changes_to_a_reserve_model_and_back()
    {
        var task = QueuedTask(out _, out _);

        task.UseReserve("nvidia/qwen3-coder");
        Assert.Equal("nvidia/qwen3-coder", task.ReserveModel);
        task.UseReserve(null);
        Assert.Null(task.ReserveModel);

        Assert.Throws<DomainException>(() => task.UseReserve("qwen3-coder"));
        task.Start(Now);
        Assert.Throws<DomainException>(() => task.UseReserve("nvidia/qwen3-coder"));
    }
}
