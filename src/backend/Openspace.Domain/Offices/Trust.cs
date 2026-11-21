using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>How much an inhabitant may do on its own, for one kind of action.</summary>
public enum TrustLevel
{
    /// <summary>Does it without a word.</summary>
    Alone,

    /// <summary>Does it, and leaves a line saying so.</summary>
    Tells,

    /// <summary>Asks a person first.</summary>
    AsksFirst,
}

/// <summary>The kinds of action a trust level applies to.</summary>
public enum TrustedAction
{
    /// <summary>
    /// Requests inside the task's own working copy that Openspace may answer without a person
    /// (in auto mode): editing files, running harmless commands. Risky requests always go to a
    /// person, whatever the trust.
    /// </summary>
    SafeRequests,

    /// <summary>Lines in its own journal in the brain.</summary>
    Journal,

    /// <summary>Decisions and notes it proposes to the brain, for Shiori to accept.</summary>
    Proposals,
}

/// <summary>
/// A desk's trust level per kind of action. What the person never set is today's behaviour:
/// everything here is done alone. Production, pushes and private folders are never in it: those
/// always need the person, whatever the trust.
/// </summary>
public sealed record TrustSettings
{
    private readonly SortedDictionary<TrustedAction, TrustLevel> _levels;

    private TrustSettings(SortedDictionary<TrustedAction, TrustLevel> levels) => _levels = levels;

    /// <summary>Today's behaviour: every action alone.</summary>
    public static readonly TrustSettings Default = new(new SortedDictionary<TrustedAction, TrustLevel>());

    /// <summary>The level of every kind of action, the defaults included.</summary>
    public IReadOnlyDictionary<TrustedAction, TrustLevel> Levels =>
        Enum.GetValues<TrustedAction>().ToDictionary(a => a, LevelOf);

    public static TrustLevel DefaultOf(TrustedAction action) => action switch
    {
        _ => TrustLevel.Alone,
    };

    public TrustLevel LevelOf(TrustedAction action) => _levels.TryGetValue(action, out var level) ? level : DefaultOf(action);

    /// <summary>The same settings with <paramref name="action"/> at <paramref name="level"/>.</summary>
    public TrustSettings With(TrustedAction action, TrustLevel level)
    {
        if (!Enum.IsDefined(action) || !Enum.IsDefined(level))
        {
            throw new DomainException("Unknown action or trust level.");
        }

        var levels = new SortedDictionary<TrustedAction, TrustLevel>(_levels);
        if (level == DefaultOf(action))
        {
            levels.Remove(action);
        }
        else
        {
            levels[action] = level;
        }

        return new TrustSettings(levels);
    }

    /// <summary>"SafeRequests=AsksFirst;Journal=Tells": only what differs from the defaults.</summary>
    public override string ToString() => string.Join(';', _levels.Select(l => $"{l.Key}={l.Value}"));

    public static TrustSettings Parse(string? text)
    {
        var settings = Default;
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2
                && Enum.TryParse<TrustedAction>(pair[0], out var action) && Enum.IsDefined(action)
                && Enum.TryParse<TrustLevel>(pair[1], out var level) && Enum.IsDefined(level))
            {
                settings = settings.With(action, level);
            }
        }

        return settings;
    }

    public bool Equals(TrustSettings? other) => other is not null && ToString() == other.ToString();

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);
}

/// <summary>When an inhabitant works: the building may show who is in by day and who by night.</summary>
public enum Shift
{
    /// <summary>Around the clock: today's behaviour.</summary>
    Always,

    /// <summary>From 08:00 to 20:00.</summary>
    Day,

    /// <summary>From 20:00 to 08:00.</summary>
    Night,
}

public static class Shifts
{
    public static readonly TimeOnly DayStarts = new(8, 0);
    public static readonly TimeOnly NightStarts = new(20, 0);

    /// <summary>Whether someone on <paramref name="shift"/> is in at <paramref name="time"/>, the local time of the office.</summary>
    public static bool IsIn(this Shift shift, TimeOnly time) => shift switch
    {
        Shift.Day => time >= DayStarts && time < NightStarts,
        Shift.Night => time >= NightStarts || time < DayStarts,
        _ => true,
    };
}
