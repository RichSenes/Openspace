namespace Openspace.Application.Agents;

public enum ActionKind
{
    /// <summary>A shell command.</summary>
    Run,
    Edit,
    Write,

    /// <summary>A page or a search on the web.</summary>
    Fetch,

    /// <summary>Anything that reaches outside the task's working copy.</summary>
    OutsideFolder,

    /// <summary>
    /// Something only a person can do or decide, such as closing an app: the agent waits
    /// for the answer. Openspace never answers it on its own.
    /// </summary>
    Question,
    Other,
}

/// <summary>
/// Something an agent does or asks to do, whatever its engine: what speech bubbles, the
/// log and the permission rules work from.
/// </summary>
/// <param name="Target">The command, the file, the address or the tool, as the engine gave it.</param>
public sealed record AgentAction(ActionKind Kind, string Target);
