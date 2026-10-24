using System.Collections.Concurrent;
using Openspace.Domain.Offices;

namespace Openspace.Application.Agents;

/// <param name="Scope">What is over its limits: <see cref="EngineLimits.ClaudeCode"/>, or an OpenCode provider such as <c>opencode:opencode</c>.</param>
public sealed record LimitPause(string Scope, string Label, DateTimeOffset Until);

/// <summary>
/// Accounts that take no new work for now because they are over their usage limits: the
/// Claude account, or an OpenCode provider (its free models, say). Desks running on them
/// keep their tasks in the queue until the pause is over; the others go on working.
/// </summary>
public sealed class EngineLimits(TimeProvider clock)
{
    public const string ClaudeCode = "claude-code";

    /// <summary>How long to wait when the engine did not say when its limits reset.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _until = new(StringComparer.Ordinal);

    /// <summary>The OpenCode provider a model belongs to, as the scope of its limits.</summary>
    public static string OpenCodeProvider(string provider) => $"opencode:{provider}";

    /// <summary>The account a desk's agent spends; null when it cannot be told (OpenCode's default model).</summary>
    public static string? ScopeOf(AgentProfile agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (agent.Engine == AgentEngine.ClaudeCode)
        {
            return ClaudeCode;
        }

        var slash = agent.Model?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        return slash > 0 ? OpenCodeProvider(agent.Model![..slash]) : null;
    }

    public void Pause(string scope, DateTimeOffset? until)
    {
        var now = clock.GetUtcNow();
        _until[scope] = until > now ? until.Value : now + DefaultPause;
    }

    /// <summary>When the account takes work again; null when it already does.</summary>
    public DateTimeOffset? PausedUntil(string? scope)
    {
        if (scope is null)
        {
            return null;
        }

        if (_until.TryGetValue(scope, out var until) && until > clock.GetUtcNow())
        {
            return until;
        }

        _until.TryRemove(scope, out _);
        return null;
    }

    public IReadOnlyList<LimitPause> Pauses() =>
    [
        .. _until.Keys
            .Select(scope => (scope, until: PausedUntil(scope)))
            .Where(p => p.until is not null)
            .Select(p => new LimitPause(p.scope, Label(p.scope), p.until!.Value))
            .OrderBy(p => p.Until),
    ];

    /// <summary>The account answered again: no need to wait for the reset.</summary>
    public void Resume(string scope) => _until.TryRemove(scope, out _);

    private static string Label(string scope)
    {
        var provider = scope[(scope.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return scope == ClaudeCode ? "Claude account" : provider == "opencode" ? "OpenCode's own models" : $"OpenCode models of {provider}";
    }
}
