using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

internal sealed class AuditLog(OpenspaceDbContext db) : IAuditLog
{
    public async Task<IReadOnlyList<AuditEntry>> ForTaskAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        await db.AuditEntries.Where(e => e.TaskId == taskId).OrderBy(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> ContainsAsync(WorkTaskId taskId, string externalId, CancellationToken cancellationToken) =>
        db.AuditEntries.AnyAsync(e => e.TaskId == taskId && e.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<WorkTaskId>> TasksActiveBetweenAsync(
        DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken) =>
        await db.AuditEntries.Where(e => e.At >= since && e.At < until).Select(e => e.TaskId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AuditEntry>> LatestAsync(
        IReadOnlyCollection<WorkTaskId>? tasks, IReadOnlyCollection<AuditKind>? kinds, long? before, int limit, CancellationToken cancellationToken)
    {
        if (tasks is { Count: 0 })
        {
            return [];
        }

        var query = db.AuditEntries.AsQueryable();
        if (tasks is not null)
        {
            var ids = tasks.ToList();
            query = query.Where(e => ids.Contains(e.TaskId));
        }

        if (kinds is not null)
        {
            var wanted = kinds.ToList();
            query = query.Where(e => wanted.Contains(e.Kind));
        }

        if (before is { } last)
        {
            query = query.Where(e => e.Id < last);
        }

        return await query.OrderByDescending(e => e.Id).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Add(AuditEntry entry) => db.AuditEntries.Add(entry);
}
