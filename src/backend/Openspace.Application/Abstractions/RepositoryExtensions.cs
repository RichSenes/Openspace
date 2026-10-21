using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

namespace Openspace.Application.Abstractions;

internal static class RepositoryExtensions
{
    public static async Task<Office> GetAsync(
        this IOfficeRepository offices, OfficeId id, CancellationToken cancellationToken) =>
        await offices.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Office", id.Value);

    public static async Task<Role> GetAsync(
        this IRoleRepository roles, RoleId id, CancellationToken cancellationToken) =>
        await roles.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Role", id.Value);

    public static async Task<Epic> GetAsync(
        this IEpicRepository epics, EpicId id, CancellationToken cancellationToken) =>
        await epics.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Epic", id.Value);

    public static async Task<Domain.Reports.Report> GetAsync(
        this IReportRepository reports, Domain.Reports.ReportId id, CancellationToken cancellationToken) =>
        await reports.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Report", id.Value);

    public static async Task<WorkTask> GetAsync(
        this IWorkTaskRepository tasks, WorkTaskId id, CancellationToken cancellationToken) =>
        await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new NotFoundException("Task", id.Value);
}
