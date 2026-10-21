using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;

namespace Openspace.Application.Offices;

/// <summary>Creates offices and furnishes them with rooms and desks.</summary>
public sealed class OfficeCommands(
    IOfficeRepository offices,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IWorkspaceManager workspaces,
    TimeProvider clock)
{
    public async Task<OfficeId> OpenOfficeAsync(string name, CancellationToken cancellationToken)
    {
        var office = Office.Open(name, clock.GetUtcNow());
        offices.Add(office);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return office.Id;
    }

    /// <summary>
    /// Points the office to a git repository. The folder is checked first; with
    /// <paramref name="initialize"/> a folder that is not a repository yet becomes one.
    /// </summary>
    public async Task SetRepositoryAsync(
        OfficeId officeId, string path, bool initialize, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.SetRepository(path, clock.GetUtcNow());

        var problem = await workspaces.CheckRepositoryAsync(office.RepositoryPath!, cancellationToken).ConfigureAwait(false);
        if (problem is { CanInitialize: true } && initialize)
        {
            await workspaces.InitializeRepositoryAsync(office.RepositoryPath!, cancellationToken).ConfigureAwait(false);
            problem = await workspaces.CheckRepositoryAsync(office.RepositoryPath!, cancellationToken).ConfigureAwait(false);
        }

        if (problem is not null)
        {
            throw new RejectedException(problem.Code, problem.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RoomId> OpenRoomAsync(OfficeId officeId, string name, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var room = office.OpenRoom(name, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return room.Id;
    }

    public async Task RenameDeskAsync(OfficeId officeId, DeskId deskId, string name, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.RenameDesk(deskId, name, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives a desk a role, or takes it away with null. Applies from the desk's next task.</summary>
    public async Task SetDeskRoleAsync(OfficeId officeId, DeskId deskId, RoleId? roleId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        if (roleId is { } role)
        {
            await roles.GetAsync(role, cancellationToken).ConfigureAwait(false);
        }

        office.SetDeskRole(deskId, roleId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets the steps every task of the room must follow; applies to tasks started from now on.</summary>
    public async Task SetRoomProcedureAsync(
        OfficeId officeId, RoomId roomId, IReadOnlyList<string> steps, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.SetRoomProcedure(roomId, steps, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateRoomContextAsync(
        OfficeId officeId, RoomId roomId, string? context, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.UpdateRoomContext(roomId, context, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeskId> AddDeskAsync(
        OfficeId officeId,
        RoomId roomId,
        string name,
        string agentName,
        string? model,
        RoleId? roleId,
        CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        if (roleId is { } role)
        {
            await roles.GetAsync(role, cancellationToken).ConfigureAwait(false);
        }

        var desk = office.AddDesk(roomId, name, AgentProfile.Create(agentName, model), clock.GetUtcNow(), roleId);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return desk.Id;
    }
}
