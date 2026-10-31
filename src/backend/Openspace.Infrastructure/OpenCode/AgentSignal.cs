using Openspace.Application.Agents;
using Openspace.Domain.Offices;
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

    /// <summary>The engine summarised the conversation to make room in the model's context.</summary>
    public sealed record Compacted(string SessionId) : AgentSignal(SessionId);

    /// <summary>The agent's account is over its usage limits; they reset at <paramref name="Until"/> when known.</summary>
    /// <param name="Scope">The account, as <see cref="EngineLimits"/> names it.</param>
    public sealed record OutOfLimits(string SessionId, string Scope, DateTimeOffset? Until, string Reason) : AgentSignal(SessionId);

    /// <summary>Tokens a session spent; <paramref name="MessageId"/> names the answer, so it is counted once.</summary>
    /// <param name="Model">The model that answered, as <c>provider/model</c>, when the engine says.</param>
    public sealed record Used(string SessionId, TokenCount Tokens, string? MessageId = null, string? Model = null) : AgentSignal(SessionId);

    /// <summary>
    /// How full the conversation is after an answer. <paramref name="Window"/> is the most the
    /// model holds; when it is unknown, the window of <paramref name="Model"/> is looked up.
    /// </summary>
    public sealed record Context(string SessionId, long Tokens, long? Window, string? Model = null) : AgentSignal(SessionId);
}
