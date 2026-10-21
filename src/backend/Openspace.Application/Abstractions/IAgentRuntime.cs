using Openspace.Domain.Offices;

namespace Openspace.Application.Abstractions;

/// <summary>Runs agents. Implemented on top of the OpenCode server.</summary>
public interface IAgentRuntime
{
    Task<bool> HasAgentAsync(string agentName, string workspacePath, CancellationToken cancellationToken);

    /// <summary>Opens an empty session working in the given folder and returns its id.</summary>
    Task<string> CreateSessionAsync(string workspacePath, string title, CancellationToken cancellationToken);

    /// <summary>Sends a message and returns at once; progress arrives as agent signals.</summary>
    Task PromptAsync(
        string sessionId, string workspacePath, AgentProfile agent, string text, CancellationToken cancellationToken);

    Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    /// <summary>The text of the agent's last answer, usually its summary of the work.</summary>
    Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken);

    Task ReplyToPermissionAsync(
        string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken);
}
