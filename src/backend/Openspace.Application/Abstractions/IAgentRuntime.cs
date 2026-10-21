namespace Openspace.Application.Abstractions;

/// <summary>Runs agents. Implemented on top of the OpenCode server.</summary>
public interface IAgentRuntime
{
    Task<bool> HasAgentAsync(string agentName, string workspacePath, CancellationToken cancellationToken);

    /// <summary>Opens an empty session working in the given folder, with the setup's permissions, and returns its id.</summary>
    Task<string> CreateSessionAsync(string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken);

    /// <summary>Sends a message and returns at once; progress arrives as agent signals.</summary>
    Task PromptAsync(
        string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Asks a model for text only, with no tools and no files, and waits for the answer.
    /// Used to draft things like role instructions.
    /// </summary>
    Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken);

    /// <summary>Gives agents working in this folder the Openspace tools, tied to this task.</summary>
    Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken);

    Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    /// <summary>The text of the agent's last answer, usually its summary of the work.</summary>
    Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    Task ReplyToPermissionAsync(
        string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken);
}
