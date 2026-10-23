using System.Collections.Concurrent;

namespace Openspace.Application.Agents;

/// <summary>
/// Permission requests of engines that wait on Openspace for the answer (Claude Code asks
/// through an Openspace tool and blocks until a person decides). OpenCode keeps its own.
/// </summary>
public sealed class PermissionRequests
{
    /// <summary>Ids of requests held here start with this, so they are told from OpenCode's.</summary>
    public const string Prefix = "claude-";

    private readonly ConcurrentDictionary<string, Request> _waiting = new();

    private sealed record Request(string SessionId, string WorkspacePath, string Description, TaskCompletionSource<bool> Answer);

    /// <summary>Holds a request; its id names it in the activity and in the answer.</summary>
    public string Open(string sessionId, string workspacePath, string description)
    {
        var id = Prefix + Guid.NewGuid().ToString("N");
        _waiting[id] = new Request(sessionId, workspacePath, description, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        return id;
    }

    /// <summary>Waits for the person's answer: true to allow once. Giving up refuses.</summary>
    public async Task<bool> WaitAsync(string id, CancellationToken cancellationToken)
    {
        if (!_waiting.TryGetValue(id, out var request))
        {
            return false;
        }

        try
        {
            return await request.Answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    public bool Answer(string id, bool allow) => _waiting.TryGetValue(id, out var request) && request.Answer.TrySetResult(allow);

    /// <summary>Refuses everything a session still waits for, when its agent is stopped.</summary>
    public void RefuseAll(string sessionId)
    {
        foreach (var (_, request) in _waiting.Where(w => w.Value.SessionId == sessionId))
        {
            request.Answer.TrySetResult(false);
        }
    }

    public IReadOnlyList<Abstractions.PermissionRequest> Pending(string workspacePath) =>
        [.. _waiting.Where(w => w.Value.WorkspacePath == workspacePath && !w.Value.Answer.Task.IsCompleted)
            .Select(w => new Abstractions.PermissionRequest(w.Key, w.Value.SessionId, w.Value.Description))];
}
