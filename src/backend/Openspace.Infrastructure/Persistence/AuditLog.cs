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

    public void Add(AuditEntry entry) => db.AuditEntries.Add(entry);
}
