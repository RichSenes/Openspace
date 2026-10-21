using Openspace.Domain.Work;

namespace Openspace.Application.Abstractions;

/// <summary>Append-only store of what happened on each task. Saved with the unit of work.</summary>
public interface IAuditLog
{
    Task<IReadOnlyList<AuditEntry>> ForTaskAsync(WorkTaskId taskId, CancellationToken cancellationToken);

    /// <summary>Whether an entry from this source event was already recorded.</summary>
    Task<bool> ContainsAsync(WorkTaskId taskId, string externalId, CancellationToken cancellationToken);

    /// <summary>Tasks with at least one line recorded in [since, until).</summary>
    Task<IReadOnlyList<WorkTaskId>> TasksActiveBetweenAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken);

    void Add(AuditEntry entry);
}
