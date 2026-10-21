using Openspace.Domain.Common;

namespace Openspace.Domain.Work;

/// <summary>Where and how an agent works on a task: its OpenCode session and its git workspace.</summary>
public sealed record AgentRun
{
    private AgentRun(string sessionId, string workspacePath, string branch)
    {
        SessionId = sessionId;
        WorkspacePath = workspacePath;
        Branch = branch;
    }

    public string SessionId { get; }

    /// <summary>Absolute path of the task's own working copy (a git worktree).</summary>
    public string WorkspacePath { get; }

    public string Branch { get; }

    public static AgentRun Create(string sessionId, string workspacePath, string branch) =>
        new(
            Guard.NotBlank(sessionId, "Session id", 200),
            Guard.NotBlank(workspacePath, "Workspace path", 1024),
            Guard.NotBlank(branch, "Branch", 200));
}
