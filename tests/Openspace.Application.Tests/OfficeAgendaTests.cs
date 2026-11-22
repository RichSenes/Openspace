using Openspace.Application.Characters;
using Openspace.Application.Google;
using Openspace.Application.Routines;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>The office's shared agenda, on a made-up office, vault and calendar.</summary>
public sealed class OfficeAgendaTests
{
    private readonly Harness _h = new();
    private readonly Accounts _google = new();
    private readonly Calendar _calendar = new();

    private OfficeAgenda Agenda() => new(_h.Offices, _h.Tasks, _h.Routines, _h.Vault, _google, _calendar, _h.Clock);

    [Fact]
    public async Task It_lists_queued_work_routine_runs_and_reviews_by_day_without_a_calendar()
    {
        // 24 June 2015, 09:00 UTC: a night desk takes its task at 20:00.
        _h.Office.SetDeskShift(_h.Desk.Id, Shift.Night, _h.Clock.GetUtcNow());
        _h.Enqueue("Rotate the keys");
        var routine = Routine.Create(_h.Office, "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        _h.Vault.Put("Decisions/merge-order.md", "---\nkind: decision\nstatus: accepted\noffices: [E Corp]\nreview: 2015-06-26\n---\n# Merge order\n");
        _h.Vault.Put("Decisions/later.md", "---\nkind: decision\nstatus: accepted\nreview: 2015-09-01\n---\n# Later\n");

        var view = await Agenda().GetAsync(_h.Office.Id, 3, default);

        Assert.Null(view.Calendar);
        Assert.Equal((new DateOnly(2015, 6, 24), new DateOnly(2015, 6, 27)), (view.From, view.Until));
        Assert.Equal(
            [
                (new DateOnly(2015, 6, 24), (TimeOnly?)new TimeOnly(20, 0), AgendaLineKind.Task, "Rotate the keys"),
                (new DateOnly(2015, 6, 25), new TimeOnly(7, 0), AgendaLineKind.Routine, "Paper"),
                (new DateOnly(2015, 6, 26), null, AgendaLineKind.Review, "Merge order"),
                (new DateOnly(2015, 6, 26), new TimeOnly(7, 0), AgendaLineKind.Routine, "Paper"),
            ],
            view.Lines.Select(l => (l.Date, l.Time, l.Kind, l.Title)));
        Assert.Equal("expected at Frontend", view.Lines[0].Detail);
    }

    [Fact]
    public async Task With_a_connected_calendar_its_events_and_late_tasks_join_in()
    {
        _google.Calendar = true;
        _calendar.Entries.Add(new AgendaEntry(true, "Dentist", new DateOnly(2015, 6, 25), new TimeOnly(10, 30), "Clinic"));
        _calendar.Entries.Add(new AgendaEntry(false, "Pay the bill", new DateOnly(2015, 6, 20), null, ""));

        var view = await Agenda().GetAsync(_h.Office.Id, null, default);

        Assert.Equal("sam@example.com", view.Calendar);
        Assert.Equal(
            [
                (new DateOnly(2015, 6, 24), AgendaLineKind.Due, "Pay the bill", "was due 2015-06-20"),
                (new DateOnly(2015, 6, 25), AgendaLineKind.Event, "Dentist", "Clinic"),
            ],
            view.Lines.Select(l => (l.Date, l.Kind, l.Title, l.Detail)));
    }

    [Fact]
    public async Task A_calendar_that_fails_leaves_the_rest_and_says_why()
    {
        _google.Calendar = true;
        _calendar.Fail = true;
        _h.Enqueue();

        var view = await Agenda().GetAsync(_h.Office.Id, null, default);

        Assert.Equal("The calendar could not be read: Calendar is down.", view.CalendarProblem);
        Assert.Single(view.Lines);
    }

    [Theory]
    [InlineData(Shift.Day, 6, "2015-06-24T08:00")]
    [InlineData(Shift.Day, 21, "2015-06-25T08:00")]
    [InlineData(Shift.Night, 9, "2015-06-24T20:00")]
    [InlineData(Shift.Night, 23, "2015-06-24T23:00")]
    [InlineData(Shift.Always, 3, "2015-06-24T03:00")]
    public void A_desk_is_expected_now_or_when_its_shift_starts(Shift shift, int hour, string expected) =>
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), OfficeAgenda.ExpectedStart(shift, new DateTimeOffset(2015, 6, 24, hour, 0, 0, TimeSpan.Zero)).DateTime);

    private sealed class Accounts : IGoogleAccounts
    {
        public bool Calendar { get; set; }

        public Task<GoogleStatusView> StatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleStatusView(true, "/nowhere", Calendar
                ? [new GoogleAccountView("mail@example.com", Harness.Start, ["https://www.googleapis.com/auth/gmail.modify"]), new GoogleAccountView("sam@example.com", Harness.Start, ["https://www.googleapis.com/auth/calendar.events"])]
                : []));

        public Uri BeginConnect(Uri callback) => throw new NotSupportedException();

        public Task<string> FinishConnectAsync(string state, string code, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DisconnectAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveClientAsync(string json, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CheckAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> AccessTokenAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Calendar : IAgenda
    {
        public List<AgendaEntry> Entries { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AddAsync(string account, AgendaItem item, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The agenda never writes.");

        public Task<IReadOnlyList<AgendaEntry>> ReadAsync(string account, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            Fail ? throw new InvalidOperationException("Calendar is down.") : Task.FromResult<IReadOnlyList<AgendaEntry>>([.. Entries]);
    }
}
