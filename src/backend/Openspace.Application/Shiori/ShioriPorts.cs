using Openspace.Application.Functions;

namespace Openspace.Application.Shiori;

/// <summary>Where sessions are kept between answers and across restarts.</summary>
public interface IShioriSessionStore
{
    Task<ShioriSession?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<ShioriSession?> FindByTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>Every session, the most recently changed first.</summary>
    Task<IReadOnlyList<ShioriSession>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(ShioriSession session, CancellationToken cancellationToken);

    /// <summary>Writes the session as it is now, whole.</summary>
    Task SaveAsync(ShioriSession session, CancellationToken cancellationToken);
}

/// <summary>One answer to ask of Shiori's engine.</summary>
/// <param name="RuntimeSessionId">The engine's conversation, chosen by Openspace.</param>
/// <param name="Resume">Whether that conversation exists already, from an earlier answer.</param>
/// <param name="Engine">The engine and model of Settings › Models for Shiori.</param>
/// <param name="Instructions">Shiori's system prompt, built again for each answer so a new mode or rule counts at once.</param>
/// <param name="Text">The person's message, with the mode in front.</param>
/// <param name="ToolsToken">The secret that names the session to Openspace's tools.</param>
/// <param name="Tools">The Openspace tools the mode allows; the engine may use no others.</param>
public sealed record ShioriTurn(
    Guid SessionId,
    string RuntimeSessionId,
    bool Resume,
    ShioriMode Mode,
    ModelEngine Engine,
    string? Model,
    string Instructions,
    string Text,
    string ToolsToken,
    IReadOnlyList<string> Tools);

/// <summary>Runs Shiori's conversations on an agent engine.</summary>
public interface IShioriRuntime
{
    /// <summary>Asks for one answer and waits for it, passing on its text as it is written.</summary>
    /// <param name="onText">Called with each new piece of the answer, in order.</param>
    /// <returns>The whole answer.</returns>
    Task<string> AnswerAsync(ShioriTurn turn, Func<string, Task> onText, CancellationToken cancellationToken);

    /// <summary>Stops the answer being written in that conversation, if any.</summary>
    Task StopAsync(string runtimeSessionId);
}

/// <summary>Pushes what happens in a session to whoever is watching it.</summary>
public interface IShioriPublisher
{
    /// <summary>A new piece of the answer being written.</summary>
    Task TextAsync(Guid sessionId, string chunk, CancellationToken cancellationToken);

    /// <summary>A new line of the transcript: the person's message, a whole answer, a mode change.</summary>
    Task MessageAsync(Guid sessionId, TranscriptEntry entry, CancellationToken cancellationToken);

    /// <summary>The whiteboard changed: the whole board.</summary>
    Task BoardAsync(Guid sessionId, IReadOnlyList<WhiteboardItem> board, CancellationToken cancellationToken);

    Task StateAsync(ShioriState state, CancellationToken cancellationToken);
}

/// <summary>Nobody watching: used until the API brings its own.</summary>
internal sealed class NoShioriPublisher : IShioriPublisher
{
    public Task TextAsync(Guid sessionId, string chunk, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MessageAsync(Guid sessionId, TranscriptEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BoardAsync(Guid sessionId, IReadOnlyList<WhiteboardItem> board, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StateAsync(ShioriState state, CancellationToken cancellationToken) => Task.CompletedTask;
}
