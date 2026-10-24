using System.Collections.Concurrent;
using Openspace.Domain.Offices;

namespace Openspace.Application.Agents;

/// <summary>
/// Engines that take no new work for now because their account is over its usage limits.
/// Their desks' tasks wait in the queue and start again once the pause is over.
/// </summary>
public sealed class EngineLimits(TimeProvider clock)
{
    /// <summary>How long to wait when the engine did not say when its limits reset.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<AgentEngine, DateTimeOffset> _until = new();

    public void Pause(AgentEngine engine, DateTimeOffset? until)
    {
        var now = clock.GetUtcNow();
        _until[engine] = until > now ? until.Value : now + DefaultPause;
    }

    /// <summary>When the engine takes work again; null when it already does.</summary>
    public DateTimeOffset? PausedUntil(AgentEngine engine)
    {
        if (_until.TryGetValue(engine, out var until) && until > clock.GetUtcNow())
        {
            return until;
        }

        _until.TryRemove(engine, out _);
        return null;
    }

    /// <summary>The account answered again: no need to wait for the reset.</summary>
    public void Resume(AgentEngine engine) => _until.TryRemove(engine, out _);
}
