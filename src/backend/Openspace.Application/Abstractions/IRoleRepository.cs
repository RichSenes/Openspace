using Openspace.Domain.Roles;

namespace Openspace.Application.Abstractions;

public interface IRoleRepository
{
    Task<Role?> FindAsync(RoleId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);

    void Add(Role role);

    void Remove(Role role);
}
