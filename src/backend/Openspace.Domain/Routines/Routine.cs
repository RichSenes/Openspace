using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Routines;

public readonly record struct RoutineId(Guid Value)
{
    public static RoutineId New() => new(Guid.CreateVersion7());
}

/// <summary>
/// A recurring job of an office that is not a task for a desk: the morning paper, a look at
/// the mail, the job offers of the day. What it does is its <see cref="Kind"/>, one of the
/// kinds Openspace knows; how, its settings. It proposes and delivers; it never acts on the
/// person's things without an approval.
/// </summary>
public sealed class Routine : AggregateRoot<RoutineId>
{
    public const int MaxSettingsLength = 20_000;

    private Routine(RoutineId id, OfficeId officeId, string name, string kind, RoutineSchedule schedule, string settings, DateTimeOffset createdAt)
        : base(id)
    {
        OfficeId = officeId;
        Name = name;
        Kind = kind;
        Schedule = schedule;
        Settings = settings;
        CreatedAt = createdAt;
    }

    public OfficeId OfficeId { get; }

    public string Name { get; private set; }

    /// <summary>What it does, as Openspace names it: "morning-paper", "job-offers"…</summary>
    public string Kind { get; }

    public RoutineSchedule Schedule { get; private set; }

    /// <summary>The kind's own settings, as JSON: sources, sections, the account to read…</summary>
    public string Settings { get; private set; }

    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Every proposal waits for a yes. Off (the default), what can be undone (a label, a
    /// reminder, a saved file) is done on its own and logged; only what cannot waits.
    /// </summary>
    public bool AskFirst { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>When it runs next; null while it is turned off.</summary>
    public DateTimeOffset? NextRunAt { get; private set; }

    public static Routine Create(
        Office office, string name, string kind, RoutineSchedule schedule, string? settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(schedule);
        var routine = new Routine(
            RoutineId.New(), office.Id, Guard.NotBlank(name, "Routine name", 80), Guard.NotBlank(kind, "Routine kind", 40),
            schedule, ValidSettings(settings), now);
        routine.NextRunAt = schedule.NextAfter(now, zone);
        routine.Raise(new RoutineChanged(routine.Id, office.Id, now));
        return routine;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = Guard.NotBlank(name, "Routine name", 80);
        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    public void Reschedule(RoutineSchedule schedule, DateTimeOffset now, TimeZoneInfo zone)
    {
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        NextRunAt = Enabled ? schedule.NextAfter(now, zone) : null;
        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    public void Configure(string? settings, DateTimeOffset now)
    {
        Settings = ValidSettings(settings);
        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    public void TurnOn(bool enabled, DateTimeOffset now, TimeZoneInfo zone)
    {
        Enabled = enabled;
        NextRunAt = enabled ? Schedule.NextAfter(now, zone) : null;
        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    public void AskBeforeActing(bool askFirst, DateTimeOffset now)
    {
        AskFirst = askFirst;
        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    public bool IsDue(DateTimeOffset now) => Enabled && NextRunAt <= now;

    /// <summary>
    /// A run starts now, on schedule or by hand: the next one is counted from now, so runs
    /// missed while the computer was off are not made up all at once.
    /// </summary>
    public void Started(DateTimeOffset now, TimeZoneInfo zone)
    {
        LastRunAt = now;
        if (Enabled)
        {
            NextRunAt = Schedule.NextAfter(now, zone);
        }

        Raise(new RoutineChanged(Id, OfficeId, now));
    }

    private static string ValidSettings(string? settings)
    {
        var text = string.IsNullOrWhiteSpace(settings) ? "{}" : settings.Trim();
        if (text.Length > MaxSettingsLength)
        {
            throw new DomainException($"Routine settings cannot be longer than {MaxSettingsLength} characters.");
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new DomainException("Routine settings must be a JSON object.");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            throw new DomainException("Routine settings are not valid JSON.");
        }

        return text;
    }
}

public sealed record RoutineChanged(RoutineId RoutineId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;
