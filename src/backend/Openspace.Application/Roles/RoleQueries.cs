using Openspace.Application.Abstractions;

namespace Openspace.Application.Roles;

public sealed class RoleQueries(IRoleRepository roles)
{
    public async Task<IReadOnlyList<RoleView>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await roles.ListAsync(cancellationToken).ConfigureAwait(false)).Select(RoleView.From)];
}
