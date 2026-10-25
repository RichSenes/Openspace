using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

public sealed class OfficeQueries(IOfficeRepository offices, IWorkspaceManager workspaces, IPullRequests pullRequests)
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
        if (office.RepositoryPath is null)
        {
            return new BranchList(null, []);
        }

        var list = await workspaces.ListBranchesAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var available = !office.LocalOnly && list.Remotes is { Count: > 0 }
            && await pullRequests.AvailableAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false);
        return list with { PullRequests = available };
    }

    public async Task<OfficeView> GetAsync(OfficeId id, CancellationToken cancellationToken) =>
        OfficeView.From(await offices.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
