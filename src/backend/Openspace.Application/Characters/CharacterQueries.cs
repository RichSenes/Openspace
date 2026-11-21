using Openspace.Application.Abstractions;
using Openspace.Application.Offices;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>
/// The character monitor: who each inhabitant is and how they are doing, for the person only.
/// Agents never see these numbers: no tool and no briefing reads them.
/// </summary>
public sealed class CharacterQueries(IOfficeRepository offices, IWorkTaskRepository tasks)
{
    public async Task<CharacterBoardView> GetAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var all = await tasks.ListAsync(officeId, null, cancellationToken).ConfigureAwait(false);
        var seniority = Seniority.Of(office, all);

        var characters = office.Rooms.SelectMany(room => room.Desks.Select(desk => new CharacterView(
            desk.Id.Value,
            desk.Name,
            room.Id.Value,
            room.Name,
            [.. desk.Traits],
            BirthdayView.From(desk),
            desk.Shift,
            desk.Trust.Levels,
            [.. seniority.Where(s => s.Desk == desk.Id).Select(s => new SeniorityView(s.Room.Value, room.Name, s.Done, s.Merged, s.Expert))])))
            .ToList();

        var experts = seniority.Where(s => s.Expert)
            .Select(s => new RoomExpertView(s.Room.Value, office.Rooms.First(r => r.Id == s.Room).Name, s.Desk.Value, office.Locate(s.Desk).Desk.Name, s.Done))
            .ToList();
        return new CharacterBoardView(office.Id.Value, office.Name, characters, experts);
    }
}

public sealed record CharacterBoardView(Guid OfficeId, string OfficeName, IReadOnlyList<CharacterView> Characters, IReadOnlyList<RoomExpertView> Experts);

public sealed record CharacterView(
    Guid DeskId,
    string Name,
    Guid RoomId,
    string RoomName,
    IReadOnlyList<Trait> Traits,
    BirthdayView Birthday,
    Shift Shift,
    IReadOnlyDictionary<TrustedAction, TrustLevel> Trust,
    IReadOnlyList<SeniorityView> Seniority);

/// <param name="Done">Tasks of the room it finished.</param>
/// <param name="Expert">It is the room's expert.</param>
public sealed record SeniorityView(Guid RoomId, string RoomName, int Done, int Merged, bool Expert);

public sealed record RoomExpertView(Guid RoomId, string RoomName, Guid DeskId, string DeskName, int Done);
