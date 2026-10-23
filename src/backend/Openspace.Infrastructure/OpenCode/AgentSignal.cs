using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>An OpenCode event reduced to what Openspace cares about.</summary>
internal abstract record AgentSignal(string SessionId)
{
    public sealed record Progress(string SessionId, string? Step, string? Doing) : AgentSignal(SessionId);

    public sealed record PermissionAsked(string SessionId, string PermissionId, AgentAction Action) : AgentSignal(SessionId);

    public sealed record Finished(string SessionId) : AgentSignal(SessionId);

    public sealed record PermissionReplied(string SessionId, string PermissionId, bool Allowed) : AgentSignal(SessionId);

    /// <summary>A tool call ended; what goes in the task's audit log.</summary>
    public sealed record ToolFinished(
        string SessionId, string CallId, AuditKind Kind, string Summary, string? Detail, bool Succeeded) : AgentSignal(SessionId);

    public sealed record Failed(string SessionId, string Reason) : AgentSignal(SessionId);

    /// <summary>Tokens a session spent; <paramref name="MessageId"/> names the answer, so it is counted once.</summary>
    public sealed record Used(string SessionId, TokenCount Tokens, string? MessageId = null) : AgentSignal(SessionId);
}
