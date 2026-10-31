namespace Openspace.Application.Agents;

/// <summary>
/// How full the conversation of a desk's agent is: its energy. An agent with little room
/// left in its context gets tired; when the engine summarises the conversation, or a fresh
/// one starts on the same task, it gets its energy back.
/// </summary>
/// <param name="Used">Tokens the conversation holds, as of the latest answer.</param>
/// <param name="Window">The most the model holds; unknown for some models.</param>
/// <param name="Compactions">Times the engine summarised this conversation.</param>
/// <param name="RefreshedAt">The last time the agent got its energy back on this task.</param>
public sealed record DeskContext(
    Guid OfficeId,
    Guid DeskId,
    Guid TaskId,
    string SessionId,
    long Used,
    long? Window,
    int Compactions,
    DateTimeOffset At,
    DateTimeOffset? RefreshedAt = null)
{
    /// <summary>The share of the context still free, from 1 (fresh) to 0 (full); null when the window is unknown.</summary>
    public double? Left => Window is > 0 ? Math.Clamp(1 - ((double)Used / Window.Value), 0, 1) : null;
}
