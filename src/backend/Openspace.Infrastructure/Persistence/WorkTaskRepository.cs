using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

internal sealed class WorkTaskRepository(OpenspaceDbContext db) : IWorkTaskRepository
{
    public Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        db.WorkTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WorkTask>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken)
    {
        var query = db.WorkTasks.AsQueryable();
        if (officeId is { } office)
        {
            query = query.Where(t => t.OfficeId == office);
        }

        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
        }

        return await query.OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<WorkTask?> FindBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        db.WorkTasks.FirstOrDefaultAsync(t => t.Run!.SessionId == sessionId, cancellationToken);

    public void Add(WorkTask task) => db.WorkTasks.Add(task);
}
