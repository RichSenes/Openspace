using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Roles;

namespace Openspace.Infrastructure.Persistence;

internal sealed class RoleRepository(OpenspaceDbContext db) : IRoleRepository
{
    public Task<Role?> FindAsync(RoleId id, CancellationToken cancellationToken) =>
        db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        await db.Roles.OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Role role) => db.Roles.Add(role);

    public void Remove(Role role) => db.Roles.Remove(role);
}
