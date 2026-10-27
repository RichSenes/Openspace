namespace Openspace.Application.Abstractions;

/// <summary>What can run agents on this machine, and with which models.</summary>
public interface IAgentEngines
{
    Task<EnginesView> DescribeAsync(CancellationToken cancellationToken);

    /// <summary>Asks Claude Code now how much of the account's limits is used; null when it cannot tell.</summary>
    Task<ClaudeUsageView?> CheckClaudeUsageAsync(CancellationToken cancellationToken);
}

/// <param name="OpenCodeModels">Models OpenCode can use, as <c>provider/model</c>, with their names.</param>
/// <param name="ClaudeCodeModels">Models Claude Code takes, the latest of each family first.</param>
/// <param name="ClaudeUsage">How much of the Claude account's limits is used, once Claude Code has reported it.</param>
/// <param name="Supervisor">The engine that plans and drafts: "ClaudeCode" or "OpenCode".</param>
/// <param name="Reviewer">The engine that reviews finished work, or null when nothing does.</param>
/// <param name="ClaudePausedUntil">While the account is over its limits, when Claude Code desks take work again.</param>
/// <param name="Pauses">Every account over its limits now, Claude's and OpenCode providers', with when each resumes.</param>
/// <param name="Gpu">Who has the graphics card, for desks on models of this machine.</param>
public sealed record EnginesView(
    bool OpenCode,
    IReadOnlyList<ModelOption> OpenCodeModels,
    bool ClaudeCode,
    IReadOnlyList<ModelOption> ClaudeCodeModels,
    ClaudeUsageView? ClaudeUsage,
    string Supervisor,
    string? Reviewer,
    DateTimeOffset? ClaudePausedUntil = null,
    IReadOnlyList<Agents.LimitPause>? Pauses = null,
    LocalModels.GpuView? Gpu = null);

public sealed record ModelOption(string Id, string Name);

/// <param name="Used">From 0 to 1.</param>
public sealed record UsageWindow(double Used, DateTimeOffset? ResetsAt);

/// <param name="Status">"allowed" while work can go on.</param>
public sealed record ClaudeUsageView(string Status, UsageWindow? FiveHours, UsageWindow? Week, DateTimeOffset SeenAt);
