using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>What can run agents here: asks OpenCode for its models and checks that Claude Code starts.</summary>
internal sealed class AgentEngines(
    OpenCodeRuntime openCode, ClaudeCodeRuntime claude, ClaudeCodeUsage usage, IOptionsMonitor<ClaudeCodeOptions> options) : IAgentEngines
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
            claudeCode && options.CurrentValue.Review ? "ClaudeCode" : null);
    }

    public Task<ClaudeUsageView?> CheckClaudeUsageAsync(CancellationToken cancellationToken) => claude.CheckUsageAsync(cancellationToken);
}
