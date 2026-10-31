using System.Collections.Concurrent;
using Openspace.Application.Abstractions;

namespace Openspace.Application.Agents;

/// <summary>
/// The energy of every desk, kept in memory like the bubbles: what the building shows above
/// each agent. It starts empty after a restart and fills again with the next answers.
/// </summary>
public sealed class ContextBoard(IActivityPublisher publisher)
{
    private readonly ConcurrentDictionary<Guid, DeskContext> _byDesk = new();

    public IReadOnlyList<DeskContext> Snapshot(Guid? officeId = null) =>
        [.. _byDesk.Values.Where(c => officeId is null || c.OfficeId == officeId).OrderBy(c => c.At)];

    public DeskContext? ForDesk(Guid deskId) => _byDesk.GetValueOrDefault(deskId);

    /// <summary>
    /// An answer told how full the conversation is. A new conversation on the same task (a
    /// fresh start, another engine taking over) gives the agent its energy back; a new task
    /// simply starts fresh.
    /// </summary>
    public Task RecordAsync(
        Guid officeId, Guid deskId, Guid taskId, string sessionId, long used, long? window, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var before = ForDesk(deskId);
        DeskContext next;
        if (before is null || before.TaskId != taskId)
        {
            next = new DeskContext(officeId, deskId, taskId, sessionId, used, window, 0, now);
        }
        else if (before.SessionId != sessionId)
        {
            next = new DeskContext(officeId, deskId, taskId, sessionId, used, window ?? before.Window, 0, now, now);
        }
        else
        {
            next = before with { Used = used, Window = window ?? before.Window, At = now };
        }

        return PublishAsync(before, next, cancellationToken);
    }

    /// <summary>
    /// The engine summarised the conversation to make room: the agent has its energy back.
    /// The next answer says how full the summary left it.
    /// </summary>
    public Task CompactedAsync(Guid deskId, string sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ForDesk(deskId) is { } before && before.SessionId == sessionId
            ? PublishAsync(before, before with { Used = 0, Compactions = before.Compactions + 1, At = now, RefreshedAt = now }, cancellationToken)
            : Task.CompletedTask;

    private async Task PublishAsync(DeskContext? before, DeskContext next, CancellationToken cancellationToken)
    {
        _byDesk[next.DeskId] = next;
        if (before is null || before with { At = next.At } != next)
        {
            await publisher.ContextChangedAsync(next, cancellationToken).ConfigureAwait(false);
        }
    }
}
