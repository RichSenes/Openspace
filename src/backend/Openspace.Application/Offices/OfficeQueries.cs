using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

public sealed class OfficeQueries(IOfficeRepository offices, IWorkspaceManager workspaces)
{
    public async Task<IReadOnlyList<OfficeView>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Select(OfficeView.From)];
    }

    /// <summary>Branches of the office repository that work can be merged into.</summary>
    public async Task<BranchList> BranchesAsync(OfficeId id, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return office.RepositoryPath is null
            ? new BranchList(null, [])
            : await workspaces.ListBranchesAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfficeView> GetAsync(OfficeId id, CancellationToken cancellationToken) =>
        OfficeView.From(await offices.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
