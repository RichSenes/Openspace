using Openspace.Domain.Offices;

namespace Openspace.Application.Characters;

/// <param name="Change">How much it moved; 0 never shows up.</param>
/// <param name="Reason">What moved it, in a few words.</param>
public sealed record MoodPoint(DateTimeOffset At, int Value, int Change, string Reason);

/// <param name="Baseline">Where it rests when nothing happens: its traits set it.</param>
/// <param name="History">Every change in the window, oldest first.</param>
public sealed record MoodLine(int Baseline, int Value, IReadOnlyList<MoodPoint> History);

/// <summary>
/// An inhabitant's mood, from 0 to 100, computed from what really happened to its work: merged
/// work lifts it, work sent back or failed lowers it, and each night it rests halfway back to
/// its baseline. Nothing is random, and the agent never sees it.
/// </summary>
public static class Mood
{
    public const int Min = 0;
    public const int Max = 100;

    /// <summary>The baseline of a character with no trait that moves it.</summary>
    public const int Neutral = 60;

    /// <summary>How far back the mood is followed: a week of nights brings anything older back to the baseline.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public const string Rested = "Rested overnight";

    public static int BaselineOf(IReadOnlyList<Trait> traits)
    {
        ArgumentNullException.ThrowIfNull(traits);
        var baseline = Neutral;
        baseline += traits.Contains(Trait.Sunny) ? 10 : 0;
        baseline += traits.Contains(Trait.Bold) ? 5 : 0;
        baseline -= traits.Contains(Trait.DryHumoured) ? 5 : 0;
        baseline -= traits.Contains(Trait.Shy) ? 5 : 0;
        return baseline;
    }

    /// <summary>How much an event moves the mood of a character with these traits.</summary>
    public static int ChangeOf(LifeEventKind kind, IReadOnlyList<Trait> traits)
    {
        ArgumentNullException.ThrowIfNull(traits);
        var change = kind switch
        {
            LifeEventKind.Merged => 8,
            LifeEventKind.SentBack => -6,
            LifeEventKind.Failed => -10,
            LifeEventKind.Conflict => -4,
            _ => 0,
        };

        // A meticulous character takes work sent back to heart; a sunny one shrugs bad news off;
        // a calm one is moved half as much by anything.
        if (kind == LifeEventKind.SentBack && traits.Contains(Trait.Meticulous))
        {
            change = change * 3 / 2;
        }

        if (change < 0 && traits.Contains(Trait.Sunny))
        {
            change /= 2;
        }

        if (traits.Contains(Trait.Calm))
        {
            change /= 2;
        }

        return change;
    }

    /// <param name="events">The desk's own events; others are ignored by the caller.</param>
    /// <param name="zone">Where the office's nights fall.</param>
    public static MoodLine Of(IReadOnlyList<Trait> traits, IEnumerable<LifeEvent> events, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(zone);
        var baseline = BaselineOf(traits);
        var since = now - Window;
        var value = baseline;
        var at = since;
        var history = new List<MoodPoint>();

        void RestUntil(DateTimeOffset until)
        {
            var nights = Math.Min(Nights(at, until, zone), 7);
            var before = value;
            for (var n = 0; n < nights; n++)
            {
                value = baseline + ((value - baseline) / 2);
            }

            if (value != before)
            {
                history.Add(new MoodPoint(Midnight(until, zone), value, value - before, Rested));
            }

            at = until;
        }

        foreach (var e in events.Where(e => e.At >= since && e.At <= now).OrderBy(e => e.At))
        {
            RestUntil(e.At);
            var before = value;
            value = Math.Clamp(value + ChangeOf(e.Kind, traits), Min, Max);
            if (value != before)
            {
                history.Add(new MoodPoint(e.At, value, value - before, ReasonOf(e)));
            }
        }

        RestUntil(now);
        return new MoodLine(baseline, value, history);
    }

    public static string ReasonOf(LifeEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Kind switch
        {
            LifeEventKind.Merged => $"“{e.Title}” was merged",
            LifeEventKind.SentBack => $"“{e.Title}” was sent back",
            LifeEventKind.Failed => $"“{e.Title}” failed",
            LifeEventKind.Conflict => $"“{e.Title}” no longer merged",
            _ => e.Title,
        };
    }

    /// <summary>Local midnights between two moments.</summary>
    private static int Nights(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone) =>
        to <= from ? 0 : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, zone).DateTime).DayNumber - DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime).DayNumber;

    /// <summary>The last local midnight at or before <paramref name="moment"/>.</summary>
    private static DateTimeOffset Midnight(DateTimeOffset moment, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        var midnight = local.Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
