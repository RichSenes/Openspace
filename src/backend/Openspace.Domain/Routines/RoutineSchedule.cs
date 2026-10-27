using Openspace.Domain.Common;

namespace Openspace.Domain.Routines;

/// <summary>
/// When a routine runs: at a time of day, on some days of the week (every day when none
/// are named), every week or every few weeks counted from <see cref="Since"/>. Times are
/// read on the person's clock, so a 7:00 paper stays at 7:00 across daylight saving.
/// </summary>
public sealed record RoutineSchedule
{
    public const int MaxEveryWeeks = 8;

    private RoutineSchedule(IReadOnlyList<DayOfWeek> days, TimeOnly at, int everyWeeks, DateOnly since)
    {
        Days = days;
        At = at;
        EveryWeeks = everyWeeks;
        Since = since;
    }

    /// <summary>The days it runs on, Monday first; empty means every day.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; }

    public TimeOnly At { get; }

    public int EveryWeeks { get; }

    /// <summary>The week counting starts from, for schedules every few weeks.</summary>
    public DateOnly Since { get; }

    public static RoutineSchedule Daily(TimeOnly at) => new([], at, 1, DateOnly.MinValue);

    public static RoutineSchedule Weekly(IEnumerable<DayOfWeek> days, TimeOnly at, int everyWeeks = 1, DateOnly? since = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (everyWeeks is < 1 or > MaxEveryWeeks)
        {
            throw new DomainException($"A routine runs every 1 to {MaxEveryWeeks} weeks.");
        }

        var list = days.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
        if (list.Count == 0 && everyWeeks > 1)
        {
            throw new DomainException("A routine that runs every few weeks needs at least one day.");
        }

        return new RoutineSchedule(list, at, everyWeeks, since ?? DateOnly.MinValue);
    }

    /// <summary>The first time it runs strictly after <paramref name="after"/>.</summary>
    public DateTimeOffset NextAfter(DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        for (var i = 0; i <= 7 * (EveryWeeks + 1); i++, day = day.AddDays(1))
        {
            if (!RunsOn(day))
            {
                continue;
            }

            var when = OnClock(day.ToDateTime(At), zone);
            if (when > after)
            {
                return when;
            }
        }

        throw new InvalidOperationException("A schedule always has a next run within a few weeks.");
    }

    public bool Equals(RoutineSchedule? other) =>
        other is not null && At == other.At && EveryWeeks == other.EveryWeeks && Since == other.Since && Days.SequenceEqual(other.Days);

    public override int GetHashCode() => HashCode.Combine(At, EveryWeeks, Since, Days.Count);

    private bool RunsOn(DateOnly day)
    {
        if (day < Since || (Days.Count > 0 && !Days.Contains(day.DayOfWeek)))
        {
            return false;
        }

        return EveryWeeks == 1 || (WeekStart(day).DayNumber - WeekStart(Since).DayNumber) / 7 % EveryWeeks == 0;
    }

    private static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>A wall-clock time in the zone; one skipped by daylight saving moves an hour on.</summary>
    private static DateTimeOffset OnClock(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
