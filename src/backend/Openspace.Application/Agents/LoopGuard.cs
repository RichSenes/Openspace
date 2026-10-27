using System.Collections.Concurrent;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>
/// Notices an agent going in circles: the same tool call failing the same way again and again
/// with no change landing in between, or a conversation that keeps filling up and being
/// summarised. Smaller models fall into both. Remembers, per task, the fresh starts it was
/// given, so a task that keeps looping is stopped for a person instead.
/// </summary>
public sealed class LoopGuard
{
    /// <summary>The same failure this many times, with no change made in between, is a loop.</summary>
    public const int RepeatedFailures = 4;

    /// <summary>A conversation summarised this many times has lost its thread.</summary>
    public const int Compactions = 3;

    /// <summary>Fresh starts a task gets in a round before it is stopped for a person.</summary>
    public const int FreshStarts = 2;

    private readonly ConcurrentDictionary<string, SessionTrail> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(WorkTaskId Task, int Round), int> _restarts = new();
    private readonly ConcurrentDictionary<WorkTaskId, string> _pending = new();

    /// <summary>A tool call failed. Returns how many times this very failure came since the last change.</summary>
    public int Failed(string sessionId, string signature)
    {
        var trail = _sessions.GetOrAdd(sessionId, _ => new SessionTrail());
        lock (trail)
        {
            trail.Failures[signature] = trail.Failures.GetValueOrDefault(signature) + 1;
            return trail.Failures[signature];
        }
    }

    /// <summary>A change landed: whatever failed before may have been on its way somewhere.</summary>
    public void Changed(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var trail))
        {
            lock (trail)
            {
                trail.Failures.Clear();
            }
        }
    }

    /// <summary>The conversation was summarised. Returns how many times it was in this session.</summary>
    public int Compacted(string sessionId)
    {
        var trail = _sessions.GetOrAdd(sessionId, _ => new SessionTrail());
        lock (trail)
        {
            return ++trail.Compactions;
        }
    }

    /// <summary>
    /// The task's agent is stopped to start over. False when it already had its fresh starts
    /// this round: then it waits for a person.
    /// </summary>
    public bool TryRestart(WorkTask task, string sessionId, string why)
    {
        ArgumentNullException.ThrowIfNull(task);
        _sessions.TryRemove(sessionId, out _);
        var given = _restarts.AddOrUpdate((task.Id, task.Round), 1, (_, n) => n + 1);
        if (given > FreshStarts)
        {
            return false;
        }

        _pending[task.Id] = why;
        return true;
    }

    /// <summary>Why the task's next run starts in a new conversation; null when it just resumes.</summary>
    public string? TakeFreshStart(WorkTaskId taskId) => _pending.TryRemove(taskId, out var why) ? why : null;

    private sealed class SessionTrail
    {
        public Dictionary<string, int> Failures { get; } = new(StringComparer.Ordinal);

        public int Compactions { get; set; }
    }
}
