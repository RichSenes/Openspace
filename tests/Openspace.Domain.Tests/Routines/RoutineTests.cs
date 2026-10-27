using Openspace.Domain.Common;
using Openspace.Domain.Routines;
using static Openspace.Domain.Tests.TestData;

namespace Openspace.Domain.Tests.Routines;

public class RoutineTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void A_daily_routine_runs_next_at_its_time_on_the_person_s_clock()
    {
        var schedule = RoutineSchedule.Daily(new TimeOnly(7, 0));

        Assert.Equal(Utc(10, 27, 6), schedule.NextAfter(Utc(10, 27, 5), Rome));
        Assert.Equal(Utc(10, 28, 6), schedule.NextAfter(Utc(10, 27, 6), Rome));
    }

    [Fact]
    public void Daylight_saving_keeps_the_time_on_the_clock()
    {
        var schedule = RoutineSchedule.Daily(new TimeOnly(7, 0));

        // 7:00 is 5:00 UTC in summer time, 6:00 UTC once the clocks go back on 25 October.
        Assert.Equal(Utc(10, 24, 5), schedule.NextAfter(Utc(10, 24, 4), Rome));
        Assert.Equal(Utc(10, 25, 6), schedule.NextAfter(Utc(10, 24, 5), Rome));
    }

    [Fact]
    public void A_weekly_routine_waits_for_its_day()
    {
        var thursday = RoutineSchedule.Weekly([DayOfWeek.Thursday], new TimeOnly(9, 0));

        Assert.Equal(Utc(10, 29, 8), thursday.NextAfter(Utc(10, 27, 12), Rome));
    }

    [Fact]
    public void Every_two_weeks_counts_weeks_from_its_start()
    {
        var fortnightly = RoutineSchedule.Weekly([DayOfWeek.Monday], new TimeOnly(8, 30), everyWeeks: 2, since: new DateOnly(2026, 11, 2));

        Assert.Equal(Utc(11, 2, 7, 30), fortnightly.NextAfter(Utc(10, 27, 12), Rome));
        Assert.Equal(Utc(11, 16, 7, 30), fortnightly.NextAfter(Utc(11, 3, 12), Rome));
    }

    [Fact]
    public void Every_few_weeks_needs_a_day_and_at_most_eight_weeks()
    {
        Assert.Throws<DomainException>(() => RoutineSchedule.Weekly([], new TimeOnly(8, 0), everyWeeks: 2));
        Assert.Throws<DomainException>(() => RoutineSchedule.Weekly([DayOfWeek.Monday], new TimeOnly(8, 0), everyWeeks: 9));
    }

    [Fact]
    public void A_routine_is_due_at_its_time_and_counts_the_next_run_from_when_it_started()
    {
        var routine = Routine.Create(ECorp(), "Morning paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), null, Utc(10, 27, 5), Rome);
        Assert.False(routine.IsDue(Utc(10, 27, 5, 59)));
        Assert.True(routine.IsDue(Utc(10, 27, 6)));

        // The computer was off for two days: one run now, not three.
        RoutineRun.Start(routine, byHand: false, Utc(10, 29, 9), Rome);

        Assert.Equal((Utc(10, 29, 9), Utc(10, 30, 6)), (routine.LastRunAt, routine.NextRunAt));
        Assert.Equal("{}", routine.Settings);
    }

    [Fact]
    public void A_routine_turned_off_is_never_due()
    {
        var routine = Routine.Create(ECorp(), "Morning paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), null, Utc(10, 27, 5), Rome);

        routine.TurnOn(false, Utc(10, 27, 5), Rome);

        Assert.False(routine.IsDue(Utc(11, 30, 0)));
        Assert.Null(routine.NextRunAt);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Settings_must_be_a_json_object(string settings)
    {
        Assert.Throws<DomainException>(() => Routine.Create(ECorp(), "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), settings, Utc(10, 27, 5), Rome));
    }

    [Fact]
    public void A_run_logs_its_steps_and_ends_once()
    {
        var routine = Routine.Create(ECorp(), "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), "{\"sections\":3}", Utc(10, 27, 5), Rome);
        var run = RoutineRun.Start(routine, byHand: true, Utc(10, 27, 5), Rome);

        run.Write("Read 51 news items", Utc(10, 27, 5));
        run.Succeed("The paper is ready: 7 articles.", Utc(10, 27, 5, 10));

        Assert.Equal((RoutineRunStatus.Succeeded, true), (run.Status, run.ByHand));
        Assert.Contains("Read 51 news items", run.Log, StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => run.Fail("Again", Utc(10, 27, 6)));
    }

    [Fact]
    public void A_proposal_is_carried_out_only_after_a_yes()
    {
        var routine = Routine.Create(ECorp(), "Deadlines", "deadlines", RoutineSchedule.Daily(new TimeOnly(7, 0)), null, Utc(10, 27, 5), Rome);
        var run = RoutineRun.Start(routine, byHand: false, Utc(10, 27, 5), Rome);
        var proposal = Proposal.Make(run, "add-reminder", "Pay the second instalment", "Due on 16 November.", "{\"due\":\"2026-11-16\"}", Utc(10, 27, 5));

        Assert.Throws<DomainException>(() => proposal.Carried("Added.", Utc(10, 27, 6)));
        proposal.Approve(Utc(10, 27, 6));
        proposal.Carried("Added to the reminders.", Utc(10, 27, 6));

        Assert.Equal(ProposalStatus.Done, proposal.Status);
        Assert.Throws<DomainException>(() => proposal.Reject(Utc(10, 27, 7)));
    }

    [Fact]
    public void A_seen_item_comes_back_only_when_it_changed_and_never_once_dismissed()
    {
        var routineId = RoutineId.New();
        var offer = SeenItem.Meet(routineId, "jobs.example/123", "fp1", "Backend engineer", SeenState.Shown, Utc(10, 27, 5));

        Assert.False(offer.MeetAgain("fp1", "Backend engineer", Utc(10, 28, 5)));
        Assert.True(offer.MeetAgain("fp2", "Backend engineer (remote)", Utc(10, 29, 5)));

        offer.Mark(SeenState.Dismissed, Utc(10, 29, 6));
        offer.Mark(SeenState.Shown, Utc(10, 29, 7));

        Assert.False(offer.MeetAgain("fp3", "Backend engineer (Tokyo)", Utc(10, 30, 5)));
        Assert.Equal(SeenState.Dismissed, offer.State);
    }
}
