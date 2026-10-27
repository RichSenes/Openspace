using Openspace.Domain.Common;

namespace Openspace.Domain.Routines;

public enum SeenState
{
    /// <summary>Seen and judged not worth showing.</summary>
    Skipped,

    /// <summary>Shown to the person.</summary>
    Shown,

    /// <summary>The person said it does not interest them: never shown again.</summary>
    Dismissed,

    /// <summary>The person took it up.</summary>
    Taken,
}

/// <summary>
/// What a routine has already met: a job offer, a news item, a mail. It is shown once; it
/// comes back only if it changed in substance (its fingerprint), and never once dismissed.
/// </summary>
public sealed class SeenItem
{
    private SeenItem(
        RoutineId routineId, string key, string fingerprint, string title, SeenState state,
        DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, DateTimeOffset changedAt)
    {
        RoutineId = routineId;
        Key = key;
        Fingerprint = fingerprint;
        Title = title;
        State = state;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        ChangedAt = changedAt;
    }

    public RoutineId RoutineId { get; }

    /// <summary>What makes it the same thing again: a canonical link, a company and a title…</summary>
    public string Key { get; }

    /// <summary>A digest of what matters in it: when it changes, the item counts as new.</summary>
    public string Fingerprint { get; private set; }

    public string Title { get; private set; }

    public SeenState State { get; private set; }

    public DateTimeOffset FirstSeenAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>When its state or fingerprint last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    public static SeenItem Meet(RoutineId routineId, string key, string fingerprint, string title, SeenState state, DateTimeOffset now) =>
        new(routineId, Guard.NotBlank(key, "Seen key", 500), fingerprint ?? string.Empty, Guard.NotBlank(title, "Seen title", 300), state, now, now, now);

    /// <summary>
    /// Met again. True when it deserves another look: it changed in substance since, and the
    /// person did not dismiss it.
    /// </summary>
    public bool MeetAgain(string fingerprint, string title, DateTimeOffset now)
    {
        LastSeenAt = now;
        if (string.Equals(Fingerprint, fingerprint ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        Fingerprint = fingerprint ?? string.Empty;
        Title = string.IsNullOrWhiteSpace(title) ? Title : title.Trim();
        ChangedAt = now;
        return State != SeenState.Dismissed;
    }

    public void Mark(SeenState state, DateTimeOffset now)
    {
        // Once dismissed, it stays dismissed: the routine never brings it back on its own.
        if (State == SeenState.Dismissed && state != SeenState.Taken)
        {
            return;
        }

        State = state;
        ChangedAt = now;
    }
}
