using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

/// <summary>Creates offices and furnishes them with rooms and desks.</summary>
public sealed class OfficeCommands(IOfficeRepository offices, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<OfficeId> OpenOfficeAsync(string name, CancellationToken cancellationToken)
    {
        var office = Office.Open(name, clock.GetUtcNow());
        offices.Add(office);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return office.Id;
    }

    public async Task<RoomId> OpenRoomAsync(OfficeId officeId, string name, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var room = office.OpenRoom(name, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return room.Id;
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
        CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var desk = office.AddDesk(roomId, name, AgentProfile.Create(agentName, model), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return desk.Id;
    }
}
