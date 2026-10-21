namespace Openspace.Application.Agents;

public enum ActivityKind
{
    /// <summary>The agent is working.</summary>
    Working,

    /// <summary>The agent is blocked until a person answers (e.g. a permission).</summary>
    Waiting,

    /// <summary>The agent stopped: its work is waiting for review or it failed.</summary>
    Stopped,
}

/// <summary>
/// What a desk is doing right now: what speech bubbles show. Not stored: it lives
/// while Openspace runs and is rebuilt from the agents' next steps.
/// </summary>
/// <param name="Step">The step the agent is on, from its own todo list, in its words.</param>
/// <param name="Doing">The concrete action under way, e.g. "Editing header.php".</param>
/// <param name="PermissionId">Set while waiting for a permission.</param>
public sealed record DeskActivity(
    Guid OfficeId,
    Guid DeskId,
    Guid TaskId,
    ActivityKind Kind,
    string? Step,
    string? Doing,
    string? PermissionId,
    DateTimeOffset At)
{
    /// <summary>The one line a bubble shows.</summary>
    public string Headline => Step ?? Doing ?? string.Empty;
}
