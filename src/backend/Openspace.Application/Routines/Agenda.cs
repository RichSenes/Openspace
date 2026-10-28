namespace Openspace.Application.Routines;

/// <summary>Something with a date for the person's agenda: a task to do by then, or an event to attend.</summary>
/// <param name="IsEvent">An event at a set time (an appointment, a flight); otherwise a task due that day (a bill to pay).</param>
/// <param name="Time">When an event starts, on the person's clock; none for a task, or an event lasting the whole day.</param>
/// <param name="Minutes">How long an event lasts.</param>
/// <param name="Notes">Where it comes from, with a link back to it.</param>
public sealed record AgendaItem(bool IsEvent, string Title, DateOnly Date, TimeOnly? Time, int Minutes, string Place, string Notes);

/// <summary>What is in the person's agenda on a day: an event, or a task due then and not done yet.</summary>
/// <param name="Time">When an event starts, on the person's clock; none for tasks and events lasting the whole day.</param>
public sealed record AgendaEntry(bool IsEvent, string Title, DateOnly Date, TimeOnly? Time, string Place);

/// <summary>A person's agenda: tasks with a due date and calendar events. Nothing in it invites or writes to anyone.</summary>
public interface IAgenda
{
    /// <param name="zone">The person's time zone, which an event's time is on.</param>
    /// <returns>A link to the task or event, or its id when there is none.</returns>
    Task<string> AddAsync(string account, AgendaItem item, TimeZoneInfo zone, CancellationToken cancellationToken);

    /// <summary>
    /// The events from <paramref name="from"/> up to <paramref name="until"/> (left out), on the person's clock, and
    /// the tasks not done yet that are due before <paramref name="until"/>, late ones included; sorted by date.
    /// </summary>
    Task<IReadOnlyList<AgendaEntry>> ReadAsync(string account, DateOnly from, DateOnly until, TimeZoneInfo zone, CancellationToken cancellationToken);
}
