namespace Openspace.Infrastructure.OpenCode;

/// <summary>An OpenCode event reduced to what Openspace cares about.</summary>
internal abstract record AgentSignal(string SessionId)
{
    public sealed record Progress(string SessionId, string? Step, string? Doing) : AgentSignal(SessionId);

    public sealed record PermissionAsked(string SessionId, string PermissionId, string Description) : AgentSignal(SessionId);

    public sealed record Finished(string SessionId) : AgentSignal(SessionId);

    public sealed record Failed(string SessionId, string Reason) : AgentSignal(SessionId);
}
