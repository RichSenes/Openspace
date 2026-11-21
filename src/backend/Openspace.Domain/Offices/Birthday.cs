using System.Globalization;
using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>An inhabitant's birthday: a day and a month, never a year, so nobody gets older.</summary>
public sealed record Birthday
{
    private Birthday(int month, int day)
    {
        Month = month;
        Day = day;
    }

    public int Month { get; }

    public int Day { get; }

    /// <summary>A day of the calendar; 29 February is allowed and is kept on 28 February in other years.</summary>
    public static Birthday On(int month, int day)
    {
        if (month is < 1 or > 12)
        {
            throw new DomainException("A birthday's month goes from 1 to 12.");
        }

        // A leap year, so that 29 February is a day like the others.
        if (day < 1 || day > DateTime.DaysInMonth(2024, month))
        {
            throw new DomainException($"Month {month} has no day {day}.");
        }

        return new Birthday(month, day);
    }

    /// <summary>A day picked from an id: the same id always gives the same day.</summary>
    public static Birthday FromSeed(Guid seed)
    {
        var dayOfYear = (int)(Seed.Of(seed) % 366);
        var date = new DateOnly(2024, 1, 1).AddDays(dayOfYear);
        return new Birthday(date.Month, date.Day);
    }

    /// <summary>Whether the birthday falls on <paramref name="date"/>.</summary>
    public bool IsOn(DateOnly date) =>
        date.Month == Month && (date.Day == Day || (Month == 2 && Day == 29 && date.Day == 28 && !DateTime.IsLeapYear(date.Year)));

    /// <summary>"11-21": how persistence keeps it.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Month:00}-{Day:00}");

    public static Birthday? Parse(string? text)
    {
        var parts = text?.Split('-');
        return parts is [var m, var d]
            && int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2024, month)
                ? new Birthday(month, day)
                : null;
    }
}
