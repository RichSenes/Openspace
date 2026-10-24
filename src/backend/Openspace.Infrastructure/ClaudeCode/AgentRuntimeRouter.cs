using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>
/// Sends each call to the engine it belongs to: a desk's own engine for its sessions,
/// Claude Code for plans, drafts and reviews when it is there, OpenCode otherwise.
/// </summary>
internal sealed partial class AgentRuntimeRouter(
    OpenCodeRuntime openCode,
    ClaudeCodeRuntime claude,
    PermissionRequests permissions,
    IOptionsMonitor<ClaudeCodeOptions> options,
    ILogger<AgentRuntimeRouter> logger) : IAgentRuntime
{
    private static readonly string[] ReadingTools = ["Read", "Grep", "Glob"];

    private static readonly string[] ReviewTools = ["Read", "Grep", "Glob", "Bash"];

    private static readonly string[] ReviewAllowed =
        ["Read", "Grep", "Glob", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)"];

    public Task<bool> HasAgentAsync(AgentProfile agent, string workspacePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Engine == AgentEngine.ClaudeCode
            ? claude.IsAvailableAsync(cancellationToken)
            : openCode.HasAgentAsync(agent.AgentName, workspacePath, cancellationToken);
    }

    public Task<string> CreateSessionAsync(string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return setup.Engine == AgentEngine.ClaudeCode
            ? Task.FromResult(claude.CreateSession(workspacePath))
            : openCode.CreateSessionAsync(workspacePath, title, setup, cancellationToken);
    }

    public AgentEngine EngineOf(string sessionId) =>
        ClaudeCodeRuntime.Owns(sessionId) ? AgentEngine.ClaudeCode : AgentEngine.OpenCode;

    public Task PromptAsync(string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken) =>
        ClaudeCodeRuntime.Owns(sessionId)
            ? claude.PromptAsync(sessionId, workspacePath, setup, text, cancellationToken)
            : openCode.PromptAsync(sessionId, workspacePath, setup, text, cancellationToken);

    public async Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken)
    {
        if (await SupervisesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                return await claude.AskAsync(Path.GetTempPath(), instructions, text, null, [], [], ClaudeCodeOptions.ModelOrDefault(options.CurrentValue.SupervisorModel), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                LogFellBack(logger, "draft", ex.Message);
            }
        }

        return await openCode.DraftAsync(instructions, text, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PlanAsync(string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken)
    {
        if (await SupervisesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                return await claude.AskAsync(folder, instructions, text, jsonSchema, ReadingTools, ReadingTools, ClaudeCodeOptions.ModelOrDefault(options.CurrentValue.SupervisorModel), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Out of the account's limits, say: the plan is still worth having from OpenCode.
                LogFellBack(logger, "plan", ex.Message);
            }
        }

        return await openCode.PlanAsync(folder, instructions, text, jsonSchema, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReviewAsync(string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.Review || !await claude.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await claude.AskAsync(folder, instructions, text, jsonSchema, ReviewTools, ReviewAllowed, ClaudeCodeOptions.ModelOrDefault(options.CurrentValue.ReviewModel), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken)
    {
        // Claude Code is given the tools when it starts; OpenCode is told now.
        claude.ConnectTools(workspacePath, taskId);
        await openCode.ConnectToolsAsync(workspacePath, taskId, cancellationToken).ConfigureAwait(false);
    }

    public Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) =>
        ClaudeCodeRuntime.Owns(sessionId)
            ? claude.AbortAsync(sessionId)
            : openCode.AbortAsync(sessionId, workspacePath, cancellationToken);

    public Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) =>
        ClaudeCodeRuntime.Owns(sessionId)
            ? Task.FromResult(claude.LastReply(sessionId))
            : openCode.GetLastReplyAsync(sessionId, workspacePath, cancellationToken);

    public async Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var held = permissions.Pending(workspacePath);
        try
        {
            return [.. held, .. await openCode.PendingPermissionsAsync(workspacePath, cancellationToken).ConfigureAwait(false)];
        }
        catch (HttpRequestException)
        {
            // OpenCode may be off while Claude Code desks work.
            return held;
        }
    }

    public Task ReplyToPermissionAsync(string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken)
    {
        if (permissionId.StartsWith(PermissionRequests.Prefix, StringComparison.Ordinal))
        {
            permissions.Answer(permissionId, allow);
            return Task.CompletedTask;
        }

        return openCode.ReplyToPermissionAsync(permissionId, workspacePath, allow, cancellationToken);
    }

    private async Task<bool> SupervisesAsync(CancellationToken cancellationToken) =>
        options.CurrentValue.Supervise && await claude.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude Code could not {What} ({Reason}); OpenCode does it instead")]
    private static partial void LogFellBack(ILogger logger, string what, string reason);
}
