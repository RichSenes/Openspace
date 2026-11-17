using Openspace.Application.Attention;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>What waits for the person, gathered from every office of a made-up building.</summary>
public sealed class NeedsYouTests
{
    private readonly Harness _h = new();

    private NeedsYouQueries Needs() => new(_h.Offices, _h.Tasks, _h.Routines);

    private Routine AddRoutine(string name, bool enabled = true)
    {
        var routine = Routine.Create(_h.Office, name, "job-offers", RoutineSchedule.Daily(new TimeOnly(8, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        if (!enabled)
        {
            routine.TurnOn(false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        }

        _h.Routines.Add(routine);
        return routine;
    }

    private RoutineRun Run(Routine routine, bool fails)
    {
        var run = RoutineRun.Start(routine, false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        if (fails)
        {
            run.Fail("The board could not be read.", _h.Clock.GetUtcNow());
        }
        else
        {
            run.Succeed("2 new offers.", _h.Clock.GetUtcNow());
        }

        _h.Routines.AddRun(run);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        return run;
    }

    [Fact]
    public async Task Tasks_to_approve_review_and_try_again_come_with_their_office_by_kind_and_the_oldest_first()
    {
        var proposed = WorkTask.Propose(_h.Office, "Write the privacy page", null, "supervisor", _h.Clock.GetUtcNow());
        _h.Tasks.Add(proposed);
        var review = _h.Enqueue("Add a logout button");
        review.Start(_h.Clock.GetUtcNow());
        review.SubmitForReview(_h.Clock.GetUtcNow());
        var failed = _h.Enqueue("Fix the login");
        failed.Start(_h.Clock.GetUtcNow());
        failed.Fail("The agent stopped.", _h.Clock.GetUtcNow());
        var older = WorkTask.Propose(_h.Office, "Older idea", null, "supervisor", Harness.Start.AddDays(-1));
        _h.Tasks.Add(older);
        _h.Enqueue("Still queued");

        var items = await Needs().ListAsync(default);

        Assert.Equal(
            [(NeedsYouKind.TaskToApprove, "Older idea"), (NeedsYouKind.TaskToApprove, "Write the privacy page"), (NeedsYouKind.TaskToReview, "Add a logout button"), (NeedsYouKind.TaskFailed, "Fix the login")],
            items.Select(i => (i.Kind, i.Title)));
        Assert.All(items, i => Assert.Equal(("E Corp", _h.Office.Id.Value), (i.OfficeName, i.OfficeId)));
        Assert.Equal(failed.Id.Value, items[^1].Id);
    }

    [Fact]
    public async Task Routine_proposals_wait_with_their_routine_and_a_routine_whose_last_run_failed_says_why()
    {
        var offers = AddRoutine("Job offers");
        var run = Run(offers, fails: false);
        _h.Routines.AddProposal(Proposal.Make(run, "interested", "Frontend Engineer — Kyo Labs", "Body", "{}", _h.Clock.GetUtcNow()));
        var paper = AddRoutine("Paper");
        Run(paper, fails: true);
        var mended = AddRoutine("Mail");
        Run(mended, fails: true);
        Run(mended, fails: false);
        var paused = AddRoutine("Old", enabled: false);
        Run(paused, fails: true);

        var items = await Needs().ListAsync(default);

        Assert.Equal(
            [(NeedsYouKind.Proposal, "Frontend Engineer — Kyo Labs", "Job offers"), (NeedsYouKind.RoutineFailed, "Paper", "The board could not be read.")],
            items.Select(i => (i.Kind, i.Title, i.Detail)));
        Assert.Equal(paper.Id.Value, items[1].Id);
    }

    [Fact]
    public async Task An_empty_building_needs_nothing() => Assert.Empty(await Needs().ListAsync(default));
}
