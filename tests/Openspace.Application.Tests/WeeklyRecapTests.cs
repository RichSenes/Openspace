using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The weekly recap on a made-up agenda and office: no real account, no real model.</summary>
public sealed class WeeklyRecapTests
{
    private readonly Harness _h = new();
    private readonly FakeAgenda _agenda = new();
    private readonly FakeChat _chat = new();

    private WeeklyRecap Kind() => new(_agenda, _h.Routines, _chat);

    private Routine Create(string account = "sam@example.com", string language = "English")
    {
        var settings = $$"""
            {"title":"Week","account":"{{account}}","language":"{{language}}","channel":"STEWARD","days":7}
            """;
        var routine = Routine.Create(_h.Office, "Week", "weekly", RoutineSchedule.Daily(new TimeOnly(19, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [Kind()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.Where(r => r.RoutineId == routine.Id).OrderBy(r => r.StartedAt).Last();
    }

    /// <summary>Another routine of the office with its runs, a day apart, the first ones longest ago; and its proposals still waiting.</summary>
    private void Other(string name, int waiting, params (RoutineRunStatus Status, string Summary, int DaysAgo)[] runs)
    {
        var routine = Routine.Create(_h.Office, name, "mail", RoutineSchedule.Daily(new TimeOnly(6, 0)), "{}", Harness.Start, TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        foreach (var (status, summary, daysAgo) in runs)
        {
            var at = _h.Clock.GetUtcNow().AddDays(-daysAgo);
            var run = RoutineRun.Start(routine, false, at, TimeZoneInfo.Utc);
            if (status == RoutineRunStatus.Failed)
            {
                run.Fail(summary, at.AddMinutes(5));
            }
            else
            {
                run.Succeed(summary, at.AddMinutes(5));
            }

            _h.Routines.AddRun(run);
            for (var i = 0; i < waiting; i++)
            {
                _h.Routines.AddProposal(Proposal.Make(run, "label", $"Label {i}", "Body.", null, at));
            }

            waiting = 0;
        }
    }

    /// <summary>Sunday 28 June 2015 at 19:00: the recap is about Monday 29 June to Sunday 5 July.</summary>
    private void SundayEvening() => _h.Clock.Advance(new DateTimeOffset(2015, 6, 28, 19, 0, 0, TimeSpan.Zero) - _h.Clock.GetUtcNow());

    [Fact]
    public async Task On_sunday_evening_it_sends_the_week_to_come_what_is_left_and_how_the_routines_did()
    {
        var routine = Create();
        SundayEvening();
        _agenda.Entries.AddRange([
            new AgendaEntry(false, "Renew the library card", new DateOnly(2015, 6, 26), null, ""),
            new AgendaEntry(true, "Dentist", new DateOnly(2015, 6, 30), new TimeOnly(10, 30), "Clinic"),
            new AgendaEntry(false, "Pay the electricity bill", new DateOnly(2015, 6, 30), null, ""),
            new AgendaEntry(true, "Trip to the lake", new DateOnly(2015, 7, 3), null, ""),
        ]);
        Other("Postman", 2, (RoutineRunStatus.Succeeded, "10 new: 6 sorted away.", 3), (RoutineRunStatus.Failed, "Gmail did not answer.", 2), (RoutineRunStatus.Succeeded, "4 new: 2 sorted away.", 1));
        Other("Old paper", 0, (RoutineRunStatus.Succeeded, "N. 1 printed.", 20));

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("2 events and 1 tasks ahead, 1 left behind; 2 proposals waiting.", run.Summary);
        Assert.Equal((new DateOnly(2015, 6, 29), new DateOnly(2015, 7, 6)), _agenda.Asked.Single());
        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(
            """
            Week · 29 Jun – 5 Jul

            Tuesday is the busy one.

            The days ahead
            Tuesday 30 June
            • 10:30 Dentist (Clinic)
            • To do: Pay the electricity bill
            Friday 3 July
            • All day: Trip to the lake

            Still to do (1)
            • Renew the library card — was due Fri 26 Jun

            Waiting for your yes in Openspace (2)
            • Postman: 2

            The past week
            • Postman: 3 runs, 1 failed
              4 new: 2 sorted away.
            """.ReplaceLineEndings("\n"),
            text);
        Assert.Contains("Not done yet, due Friday 26 June: Renew the library card", _chat.Asked.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_the_morning_it_starts_from_today()
    {
        var routine = Create();

        await RunAsync(routine);

        Assert.Equal((new DateOnly(2015, 6, 24), new DateOnly(2015, 7, 1)), _agenda.Asked.Single());
        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal("Week · 24 Jun – 30 Jun\n\nThe days ahead\nNothing in the agenda.", text);
        Assert.Empty(_chat.Asked);
    }

    [Fact]
    public async Task It_speaks_the_person_s_language()
    {
        var routine = Create(language: "Italian");
        SundayEvening();
        _agenda.Entries.Add(new AgendaEntry(true, "Dentista", new DateOnly(2015, 6, 30), new TimeOnly(10, 30), ""));
        Other("Postino", 0, (RoutineRunStatus.Succeeded, "Fatto.", 1), (RoutineRunStatus.Failed, "No.", 1), (RoutineRunStatus.Failed, "No.", 1));

        await RunAsync(routine);

        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.StartsWith("Week · 29 giu – 5 lug", text, StringComparison.Ordinal);
        Assert.Contains("I prossimi giorni\nMartedì 30 giugno\n• 10:30 Dentista", text, StringComparison.Ordinal);
        Assert.Contains("La settimana passata\n• Postino: 3 giri, 2 non riusciti", text, StringComparison.Ordinal);
        Assert.Contains("Write in Italian", _chat.Systems.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_silent_model_or_an_unreadable_agenda_still_leaves_a_recap()
    {
        var routine = Create();
        SundayEvening();
        _agenda.Entries.Add(new AgendaEntry(true, "Dentist", new DateOnly(2015, 6, 30), new TimeOnly(10, 30), ""));
        _chat.Fail = true;
        Other("Postman", 1, (RoutineRunStatus.Succeeded, "Done.", 1));

        var first = await RunAsync(routine);
        _agenda.Fail = true;
        var second = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, first.Status);
        Assert.Contains("No opening lines", first.Log, StringComparison.Ordinal);
        Assert.StartsWith("Week · 29 Jun – 5 Jul\n\nThe days ahead\nTuesday 30 June", _h.Outbox.Sent[0].Text, StringComparison.Ordinal);
        Assert.Equal(RoutineRunStatus.Succeeded, second.Status);
        Assert.Equal("No agenda; 1 proposals waiting.", second.Summary);
        Assert.Contains("The agenda could not be read (Calendar is down.)", second.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("The days ahead", _h.Outbox.Sent[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_account_it_leaves_the_agenda_out()
    {
        var routine = Create(account: "");
        Other("Postman", 3, (RoutineRunStatus.Succeeded, "Done.", 1));

        var run = await RunAsync(routine);

        Assert.Empty(_agenda.Asked);
        Assert.Contains("No Google account", run.Log, StringComparison.Ordinal);
        Assert.Equal("Week · 24 Jun – 30 Jun\n\nWaiting for your yes in Openspace (3)\n• Postman: 3\n\nThe past week\n• Postman: 1 run\n  Done.", _h.Outbox.Sent.Single().Text);
    }

    private sealed class FakeAgenda : IAgenda
    {
        public List<AgendaEntry> Entries { get; } = [];

        public List<(DateOnly From, DateOnly Until)> Asked { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AddAsync(string account, AgendaItem item, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The recap never writes.");

        public Task<IReadOnlyList<AgendaEntry>> ReadAsync(string account, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Calendar is down.");
            }

            Asked.Add((from, until));
            return Task.FromResult<IReadOnlyList<AgendaEntry>>([.. Entries.Where(e => e.Date < until && (e.Date >= from || !e.IsEvent)).OrderBy(e => e.Date).ThenBy(e => e.IsEvent)]);
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public List<string> Systems { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The model is busy.");
            }

            Asked.Add(ask.User);
            Systems.Add(ask.System);
            return Task.FromResult("""{"intro":"Tuesday is the busy one."}""");
        }
    }
}
