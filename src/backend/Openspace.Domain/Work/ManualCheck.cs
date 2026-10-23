namespace Openspace.Domain.Work;

/// <summary>Something a person must try by hand before the work is merged, and how.</summary>
public sealed class ManualCheck
{
    internal ManualCheck(int number, string what, string how)
    {
        Number = number;
        What = what;
        How = how;
    }

    /// <summary>1-based, in the order checks were listed.</summary>
    public int Number { get; }

    /// <summary>What to verify, e.g. "The logout button signs you out on a phone".</summary>
    public string What { get; }

    /// <summary>How to try it: where to go, what to run, what to expect. May be empty.</summary>
    public string How { get; }

    /// <summary>When a person ticked it as tried; null while it is still to do.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    public bool IsChecked => CheckedAt is not null;

    internal void Tick(bool done, DateTimeOffset now) => CheckedAt = done ? CheckedAt ?? now : null;
}
