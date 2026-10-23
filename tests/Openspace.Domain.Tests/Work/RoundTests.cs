using Openspace.Domain.Common;
using Openspace.Domain.Work;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Work;

/// <summary>Rounds of work, checks a person tries by hand, and the tokens spent on each round.</summary>
public class RoundTests
{
    private static WorkTask RunningTask()
    {
        var (office, desk) = ECorpWithDesk();
        var task = WorkTask.Assign(office, desk.Id, "Add a logout button to the header", null, "elliot", Now);
        task.Start(Now);
        return task;
    }

    [Fact]
    public void Every_send_back_starts_a_new_round_and_moves_the_status_time()
    {
        var task = RunningTask();
        task.SubmitForReview(Now.AddMinutes(10));

        task.SendBack(NoteAuthor.Human("elliot"), "Make it red.", Now.AddMinutes(20));

        Assert.Equal((2, Now.AddMinutes(20)), (task.Round, task.StatusChangedAt));
    }

    [Fact]
    public void Tokens_are_counted_on_the_round_they_were_spent_in()
    {
        var task = RunningTask();
        task.RecordUsage(new TokenCount(1000, 200, 3000, 400), Now);
        task.RecordUsage(new TokenCount(500, 100, 0, 0), Now);
        task.SubmitForReview(Now);
        task.SendBack(NoteAuthor.Human("elliot"), "Make it red.", Now);
        task.RecordUsage(new TokenCount(10, 5, 0, 0), Now);

        Assert.Equal([(1, 1500L, 300L), (2, 10L, 5L)], task.Usage.Select(u => (u.Round, u.Input, u.Output)));
        Assert.Equal(new TokenCount(1510, 305, 3000, 400), task.TotalTokens);
        Assert.Throws<DomainException>(() => task.RecordUsage(new TokenCount(-1, 0, 0, 0), Now));
    }

    [Fact]
    public void Checks_are_ticked_in_review_and_untouched_again_when_the_work_goes_back()
    {
        var task = RunningTask();
        task.AddManualCheck("The button signs you out", " Tap Logout. ", Now);
        Assert.Throws<DomainException>(() => task.TickManualCheck(1, true, Now));
        task.SubmitForReview(Now);

        task.TickManualCheck(1, true, Now.AddMinutes(1));
        Assert.Equal((true, "Tap Logout."), (task.ManualChecks[0].IsChecked, task.ManualChecks[0].How));

        task.SendBack(NoteAuthor.Human("elliot"), "The menu broke.", Now.AddMinutes(2));
        Assert.False(task.ManualChecks[0].IsChecked);
        Assert.Throws<DomainException>(() => task.AddManualCheck("Too late", null, Now));
    }

    [Fact]
    public void A_task_lists_a_bounded_number_of_checks()
    {
        var task = RunningTask();
        for (var i = 1; i <= WorkTask.MaxManualChecks; i++)
        {
            Assert.Equal(i, task.AddManualCheck($"Check {i}", null, Now));
        }

        Assert.Throws<DomainException>(() => task.AddManualCheck("One more", null, Now));
        Assert.Equal(3, task.AddManualCheck("check 3", "Repeated", Now));
    }
}
