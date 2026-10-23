using Openspace.Domain.Offices;

namespace Openspace.Application.Abstractions;

/// <summary>Runs agents, on the OpenCode server or with Claude Code, whichever each desk uses.</summary>
public interface IAgentRuntime
{
    /// <summary>Whether the desk's engine is there and knows its agent.</summary>
    Task<bool> HasAgentAsync(AgentProfile agent, string workspacePath, CancellationToken cancellationToken);

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

    /// <summary>
    /// Lets a model read a folder (it may not change anything) and answer with JSON that
    /// matches <paramref name="jsonSchema"/>. Returns that JSON.
    /// </summary>
    Task<string> PlanAsync(string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken);

    /// <summary>
    /// Lets the reviewer read a working copy, and the changes in it through git, and answer
    /// with JSON that matches <paramref name="jsonSchema"/>. Null when no reviewer is set up.
    /// </summary>
    Task<string?> ReviewAsync(string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken);

    /// <summary>Gives agents working in this folder the Openspace tools, tied to this task.</summary>
    Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken);

    Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    /// <summary>The text of the agent's last answer, usually its summary of the work.</summary>
    Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    /// <summary>Permission requests still waiting for an answer in this folder.</summary>
    Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken);

    Task ReplyToPermissionAsync(
        string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken);
}

/// <param name="Action">What is asked, e.g. to run "ls -la".</param>
public sealed record PermissionRequest(string Id, string SessionId, Agents.AgentAction Action);
