using Openspace.Application.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Application.Google;
using Openspace.Application.Routines;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>What a line of the shared agenda is.</summary>
public enum AgendaLineKind
{
    /// <summary>A queued task, when its desk is expected to take it.</summary>
    Task,

    /// <summary>A routine's next run.</summary>
    Routine,

    /// <summary>A decision whose review date comes.</summary>
    Review,

    /// <summary>An event of the person's calendar.</summary>
    Event,

    /// <summary>A task of the person's Google Tasks, due that day.</summary>
    Due,
}

/// <param name="Time">On the office's clock; none for what has a day but no hour.</param>
/// <param name="Detail">Who or what: the desk, the routine, the place, "overdue".</param>
public sealed record AgendaLine(DateOnly Date, TimeOnly? Time, AgendaLineKind Kind, string Title, string Detail);

/// <param name="Calendar">The Google account read, or null when none is connected with the calendar.</param>
/// <param name="CalendarProblem">Why the calendar is missing although an account is connected.</param>
public sealed record AgendaView(Guid OfficeId, DateOnly From, DateOnly Until, IReadOnlyList<AgendaLine> Lines, string? Calendar, string? CalendarProblem);

/// <summary>
/// The office's shared agenda for the next days, read from what Openspace already knows: queued
/// tasks and when their desks are expected to take them (shifts count), the routines' runs,
/// decisions due for review, and, when a Google account with the calendar is connected, its
/// events and tasks. It only reads; the person sees it, agents do not.
/// </summary>
public sealed class OfficeAgenda(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IRoutineRepository routines,
    IVault vault,
    IGoogleAccounts google,
    IAgenda agenda,
    TimeProvider clock)
{
    public const int DefaultDays = 7;

    public const int MaxDays = 31;

    private const string CalendarScope = "calendar";

    public async Task<AgendaView> GetAsync(OfficeId officeId, int? days, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var zone = clock.LocalTimeZone;
        var now = clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var from = DateOnly.FromDateTime(local.DateTime);
        var until = from.AddDays(Math.Clamp(days ?? DefaultDays, 1, MaxDays));
        var lines = new List<AgendaLine>();

        foreach (var task in await tasks.ListAsync(officeId, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false))
        {
            if (task.DeskId is { } deskId && office.HasDesk(deskId))
            {
                var desk = office.Locate(deskId).Desk;
                var at = ExpectedStart(desk.Shift, local);
                lines.Add(new AgendaLine(DateOnly.FromDateTime(at.DateTime), Minute(at), AgendaLineKind.Task, task.Title, $"expected at {desk.Name}"));
            }
        }

        foreach (var routine in (await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false)).Where(r => r.Enabled && r.NextRunAt is not null))
        {
            for (var next = routine.NextRunAt!.Value; ; next = routine.Schedule.NextAfter(next, zone))
            {
                var at = TimeZoneInfo.ConvertTime(next < now ? now : next, zone);
                var day = DateOnly.FromDateTime(at.DateTime);
                if (day >= until)
                {
                    break;
                }

                lines.Add(new AgendaLine(day, Minute(at), AgendaLineKind.Routine, routine.Name, next < now ? "due now" : "routine"));
                if (next < now)
                {
                    break;
                }
            }
        }

        try
        {
            var notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var note in DecisionRules.Due(notes, office.Name, until.AddDays(-1)))
            {
                var review = note.Review!.Value;
                lines.Add(new AgendaLine(review < from ? from : review, null, AgendaLineKind.Review, note.Title, review < from ? $"review was due {DecisionRules.Day(review)}" : "decision to review"));
            }
        }
        catch (IOException)
        {
            // A vault that cannot be read leaves the rest of the agenda as it is.
        }

        var (account, problem) = await CalendarAsync(lines, from, until, zone, cancellationToken).ConfigureAwait(false);
        return new AgendaView(
            office.Id.Value, from, until,
            [.. lines.OrderBy(l => l.Date).ThenBy(l => l.Time ?? TimeOnly.MinValue).ThenBy(l => l.Kind).ThenBy(l => l.Title, StringComparer.Ordinal)],
            account, problem);
    }

    /// <summary>The clock time to the minute: an agenda says 09:41, not 09:41:23.47.</summary>
    private static TimeOnly Minute(DateTimeOffset at) => new(at.Hour, at.Minute);

    /// <summary>When a desk on <paramref name="shift"/> takes work: now if it is in, else when its shift starts.</summary>
    public static DateTimeOffset ExpectedStart(Shift shift, DateTimeOffset local)
    {
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (shift.IsIn(time))
        {
            return local;
        }

        var starts = shift == Shift.Night ? Shifts.NightStarts : Shifts.DayStarts;
        var day = time < starts ? local.Date : local.Date.AddDays(1);
        return new DateTimeOffset(day + starts.ToTimeSpan(), local.Offset);
    }

    private async Task<(string? Account, string? Problem)> CalendarAsync(
        List<AgendaLine> lines, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        string? account = null;
        try
        {
            var status = await google.StatusAsync(cancellationToken).ConfigureAwait(false);
            account = status.Accounts.FirstOrDefault(a => a.Scopes.Any(s => s.Contains(CalendarScope, StringComparison.OrdinalIgnoreCase)))?.Email;
            if (account is null)
            {
                return (null, null);
            }

            foreach (var entry in await agenda.ReadAsync(account, from, until, zone, cancellationToken).ConfigureAwait(false))
            {
                var late = !entry.IsEvent && entry.Date < from;
                lines.Add(new AgendaLine(
                    late ? from : entry.Date, entry.Time, entry.IsEvent ? AgendaLineKind.Event : AgendaLineKind.Due, entry.Title,
                    late ? $"was due {DecisionRules.Day(entry.Date)}" : entry.Place));
            }

            return (account, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (account, $"The calendar could not be read: {ex.Message}");
        }
    }
}
