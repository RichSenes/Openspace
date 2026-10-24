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

    /// <summary>Draws the office with another mood.</summary>
    public async Task ChangeThemeAsync(OfficeId officeId, string theme, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ChangeTheme(theme, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves how a room is arranged: its size, where its desks stand, its furniture.</summary>
    public async Task ArrangeRoomAsync(OfficeId officeId, RoomId roomId, RoomLayout layout, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ArrangeRoom(roomId, layout, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Goes back to the automatic layout of a room.</summary>
    public async Task ClearRoomLayoutAsync(OfficeId officeId, RoomId roomId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ClearRoomLayout(roomId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves the building drawn by hand: its size, its rooms and the common floor.</summary>
    public async Task PlanFloorAsync(OfficeId officeId, FloorPlan plan, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.PlanFloor(plan, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Goes back to the automatic building.</summary>
    public async Task ClearFloorPlanAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ClearFloorPlan(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameDeskAsync(OfficeId officeId, DeskId deskId, string name, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.RenameDesk(deskId, name, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives a desk a role, or takes it away with null. Applies from the desk's next task.</summary>
    public async Task DressDeskAsync(OfficeId officeId, DeskId deskId, DeskLook? look, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.DressDesk(deskId, look, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

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

    public async Task SetPrivatePathsAsync(OfficeId officeId, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.SetPrivatePaths(paths, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetRoomCheckAsync(OfficeId officeId, RoomId roomId, string? command, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.SetRoomCheck(roomId, command, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeRoomMoodAsync(OfficeId officeId, RoomId roomId, string? mood, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ChangeRoomMood(roomId, mood, clock.GetUtcNow());
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
        CancellationToken cancellationToken,
        AgentEngine engine = AgentEngine.OpenCode)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        if (roleId is { } role)
        {
            await roles.GetAsync(role, cancellationToken).ConfigureAwait(false);
        }

        var desk = office.AddDesk(roomId, name, AgentProfile.Create(agentName, model, engine), clock.GetUtcNow(), roleId);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return desk.Id;
    }

    /// <summary>Changes what runs the agent at a desk: engine, agent and model. Applies from its next task.</summary>
    public async Task ChangeDeskAgentAsync(
        OfficeId officeId, DeskId deskId, AgentEngine engine, string agentName, string? model, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        office.ReassignDesk(deskId, AgentProfile.Create(agentName, model, engine), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
