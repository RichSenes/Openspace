using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

internal sealed class InMemoryOffices : IOfficeRepository
{
    public List<Office> Items { get; } = [];

    public Task<Office?> FindAsync(OfficeId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(o => o.Id == id));

    public Task<IReadOnlyList<Office>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Office>>(Items);

    public void Add(Office office) => Items.Add(office);
}

internal sealed class InMemoryTasks : IWorkTaskRepository
{
    public List<WorkTask> Items { get; } = [];

    public Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(t => t.Id == id));

    public Task<IReadOnlyList<WorkTask>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkTask>>(Items
            .Where(t => officeId is null || t.OfficeId == officeId)
            .Where(t => status is null || t.Status == status)
            .OrderBy(t => t.CreatedAt)
            .ToList());

    public void Add(WorkTask task) => Items.Add(task);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
