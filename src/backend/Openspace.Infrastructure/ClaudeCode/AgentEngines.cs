using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>What can run agents here: asks OpenCode for its models and checks that Claude Code starts.</summary>
internal sealed class AgentEngines(
    OpenCodeRuntime openCode, ClaudeCodeRuntime claude, ClaudeCodeUsage usage, IOptionsMonitor<ClaudeCodeOptions> options, Application.Agents.EngineLimits limits,
    Application.LocalModels.GpuArbiter gpu)
    : IAgentEngines
{
    /// <summary>Aliases Claude Code resolves to the latest model of each family.</summary>
    private static readonly ModelOption[] ClaudeModels =
    [
        new("opus", "Opus, the most capable"),
        new("sonnet", "Sonnet, balanced"),
        new("haiku", "Haiku, the fastest"),
    ];

    public async Task<EnginesView> DescribeAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ModelOption> models;
        bool reachable;
        try
        {
            models = await openCode.ModelsAsync(cancellationToken).ConfigureAwait(false);
            reachable = true;
        }
        catch (HttpRequestException)
        {
            models = [];
            reachable = false;
        }

        var claudeCode = await claude.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
        return new EnginesView(
            reachable,
            models,
            claudeCode,
            claudeCode ? ClaudeModels : [],
            usage.Latest,
            claudeCode && options.CurrentValue.Supervise ? "ClaudeCode" : "OpenCode",
            claudeCode && options.CurrentValue.Review ? "ClaudeCode" : null,
            limits.PausedUntil(Application.Agents.EngineLimits.ClaudeCode),
            limits.Pauses(),
            gpu.View());
    }

    public async Task<ClaudeUsageView?> CheckClaudeUsageAsync(CancellationToken cancellationToken)
    {
        var seen = await claude.CheckUsageAsync(cancellationToken).ConfigureAwait(false);

        // Asked by hand and answered: the desks need not wait for the reset any more.
        if (seen?.Status == "allowed" && seen.FiveHours?.Used < 1 && (seen.Week?.Used ?? 0) < 1)
        {
            limits.Resume(Application.Agents.EngineLimits.ClaudeCode);
        }

        return seen;
    }
}
