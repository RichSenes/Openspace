using Openspace.Application.Routines;
using Openspace.Domain.Common;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

public class RoutineTests
{
    private readonly Harness _h = new();

    private static ScheduleView Daily(string at) => new([], at, 1, null);

    private async Task<Routine> OffersAsync()
    {
        var id = await _h.RoutineCommands().CreateAsync(_h.Office.Id, "Offers in Japan", "fake-offers", Daily("06:30"), null, default);
        return _h.Routines.Items.Single(r => r.Id == id);
    }

    private Task RunDueAsync() => _h.RoutineRunner().RunWaitingAsync(default);

    [Fact]
    public async Task A_routine_of_an_unknown_kind_is_refused()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _h.RoutineCommands().CreateAsync(_h.Office.Id, "Mystery", "astrology", Daily("06:30"), null, default));
    }

    [Fact]
    public async Task A_new_routine_starts_with_its_kind_s_settings_and_runs_at_its_time()
    {
        var routine = await OffersAsync();
        Assert.Equal("""{"country":"JP"}""", routine.Settings);

        await RunDueAsync();
        Assert.Empty(_h.Routines.Runs);

        _h.Clock.Advance(TimeSpan.FromHours(21) + TimeSpan.FromMinutes(30));
        await RunDueAsync();

        var run = Assert.Single(_h.Routines.Runs);
        Assert.Equal((RoutineRunStatus.Succeeded, "0 new offers", false), (run.Status, run.Summary, run.ByHand));
        Assert.Equal([("Offers in Japan", "0 new offers", (string?)null)], _h.Outbox.Sent);
        Assert.True(routine.NextRunAt > _h.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Run_now_runs_it_at_once_whatever_its_schedule()
    {
        var routine = await OffersAsync();

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        Assert.True(Assert.Single(_h.Routines.Runs).ByHand);
    }

    [Fact]
    public async Task A_failing_run_says_why_and_the_routine_keeps_its_schedule()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Fail = new HttpRequestException("The job board did not answer.");

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        var run = Assert.Single(_h.Routines.Runs);
        Assert.Equal((RoutineRunStatus.Failed, "The job board did not answer."), (run.Status, run.Summary));
        Assert.NotNull(routine.NextRunAt);
    }

    [Fact]
    public async Task A_turned_off_routine_does_not_run_on_schedule()
    {
        var routine = await OffersAsync();
        await _h.RoutineCommands().TurnOnAsync(routine.Id, false, default);

        _h.Clock.Advance(TimeSpan.FromDays(3));
        await RunDueAsync();

        Assert.Empty(_h.Routines.Runs);
    }

    [Fact]
    public async Task Runs_cut_short_by_a_restart_are_failed()
    {
        var routine = await OffersAsync();
        var run = RoutineRun.Start(routine, false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.AddRun(run);

        await _h.RoutineRunner().RecoverAsync(default);

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
    }

    [Fact]
    public async Task The_same_offer_is_never_proposed_twice()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo, hybrid"));
        _h.OffersKind.Offers.Add(("https://jobs.example/2", "Mobile developer", ".NET MAUI, Osaka"));

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        Assert.Equal(2, _h.Routines.Proposals.Count);
        Assert.Equal("0 new offers", _h.Routines.Runs.OrderBy(r => r.StartedAt).Last().Summary);
    }

    [Fact]
    public async Task An_offer_that_changed_in_substance_comes_back_but_never_once_dismissed()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo, hybrid"));
        _h.OffersKind.Offers.Add(("https://jobs.example/2", "Mobile developer", ".NET MAUI, Osaka"));
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();
        var mobile = _h.Routines.Proposals.Single(p => p.Title == "Mobile developer");
        await _h.ProposalCommands().RejectAsync(mobile.Id, default);

        _h.OffersKind.Offers[0] = ("https://jobs.example/1", "Backend engineer", "C#, Tokyo, fully remote, visa sponsored");
        _h.OffersKind.Offers[1] = ("https://jobs.example/2", "Mobile developer", ".NET MAUI, Osaka, higher salary");
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        Assert.Equal(["Backend engineer", "Mobile developer", "Backend engineer"], _h.Routines.Proposals.OrderBy(p => p.CreatedAt).Select(p => p.Title));
        Assert.Equal(SeenState.Dismissed, _h.Routines.Seen.Single(s => s.Key == "https://jobs.example/2").State);
    }

    [Fact]
    public async Task An_approved_proposal_is_carried_out_and_a_failure_says_why()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo"));
        _h.OffersKind.Offers.Add(("https://jobs.example/2", "Mobile developer", ".NET MAUI"));
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();
        var (first, second) = (_h.Routines.Proposals[0], _h.Routines.Proposals[1]);

        await _h.ProposalCommands().ApproveAsync(first.Id, default);
        _h.OffersKind.FailCarry = new IOException("The CV folder is missing.");
        await _h.ProposalCommands().ApproveAsync(second.Id, default);

        Assert.Equal((ProposalStatus.Done, "Tailored CV saved."), (first.Status, first.Outcome));
        Assert.Equal((ProposalStatus.Failed, "The CV folder is missing."), (second.Status, second.Outcome));
        Assert.Equal(["Backend engineer"], _h.OffersKind.CarriedOut);
    }

    [Fact]
    public async Task The_list_shows_each_routine_with_its_last_run_and_the_waiting_proposals()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo"));
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        var view = Assert.Single(await _h.RoutineQueries().ListAsync(_h.Office.Id, default));
        var waiting = await _h.RoutineQueries().ProposalsAsync(_h.Office.Id, ProposalStatus.Pending, default);

        Assert.Equal(("Offers of the day", "06:30", "1 new offers", ""), (view.KindName, view.Schedule.At, view.LastRun!.Summary, view.LastRun.Log));
        Assert.Equal(("Offers in Japan", "Backend engineer"), (Assert.Single(waiting).RoutineName, waiting[0].Title));
    }

    [Fact]
    public async Task What_can_be_undone_is_done_on_its_own_and_logged()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Undoable = true;
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo"));

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        var proposal = Assert.Single(_h.Routines.Proposals);
        Assert.Equal((ProposalStatus.Done, "Tailored CV saved."), (proposal.Status, proposal.Outcome));
        Assert.Contains("Done on its own: Backend engineer", _h.Routines.Runs[0].Log, StringComparison.Ordinal);
        Assert.Empty(await _h.RoutineQueries().ProposalsAsync(_h.Office.Id, ProposalStatus.Pending, default));
    }

    [Fact]
    public async Task A_routine_that_asks_first_waits_for_a_yes_even_for_what_can_be_undone()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Undoable = true;
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo"));
        await _h.RoutineCommands().AskFirstAsync(routine.Id, true, default);

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        Assert.Equal(ProposalStatus.Pending, Assert.Single(_h.Routines.Proposals).Status);
        Assert.Empty(_h.OffersKind.CarriedOut);
        Assert.True(Assert.Single(await _h.RoutineQueries().ListAsync(_h.Office.Id, default)).AskFirst);
    }

    [Fact]
    public async Task The_first_routine_opens_the_office_s_routines_room_without_desks()
    {
        await OffersAsync();
        await OffersAsync();

        var room = Assert.Single(_h.Office.Rooms, r => r.Kind == Domain.Offices.RoomKind.Routines);
        Assert.Equal("Routine", room.Name);
        Assert.Throws<DomainException>(() => _h.Office.AddDesk(room.Id, "Clerk", Domain.Offices.AgentProfile.Create("build"), _h.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task Offices_that_had_routines_get_their_room_at_startup()
    {
        var routine = Routine.Create(_h.Office, "Paper", "fake-offers", RoutineSchedule.Daily(new TimeOnly(6, 0)), null, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);

        await _h.RoutineRunner().RecoverAsync(default);

        Assert.Contains(_h.Office.Rooms, r => r.Kind == Domain.Offices.RoomKind.Routines);
    }

    [Fact]
    public async Task A_run_tells_the_building_what_it_does_and_when_it_stops()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Offers.Add(("https://jobs.example/1", "Backend engineer", "C#, Tokyo"));

        await _h.RoutineCommands().RunNowAsync(routine.Id, default);
        await RunDueAsync();

        Assert.Equal("Starting", _h.Progress.Lines[0].Line);
        Assert.Contains(_h.Progress.Lines, l => l.Line == "Proposed: Backend engineer");
        Assert.Equal(string.Empty, _h.Progress.Lines[^1].Line);
    }

    [Fact]
    public async Task A_person_can_stop_a_run_and_the_routine_keeps_its_schedule()
    {
        var routine = await OffersAsync();
        _h.OffersKind.Hang = true;
        await _h.RoutineCommands().RunNowAsync(routine.Id, default);

        var running = RunDueAsync();
        while (_h.Routines.Runs.Count == 0)
        {
            await Task.Delay(10);
        }

        Assert.True(await _h.RoutineCommands().StopAsync(routine.Id, default));
        await running;

        Assert.Equal((RoutineRunStatus.Failed, "Stopped by a person."), (_h.Routines.Runs[0].Status, _h.Routines.Runs[0].Summary));
        Assert.NotNull(routine.NextRunAt);
        Assert.False(await _h.RoutineCommands().StopAsync(routine.Id, default));
    }
}
