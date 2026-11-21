using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <param name="Done">Its tasks of the room a person accepted.</param>
/// <param name="Merged">Of those, the ones whose work went into a branch.</param>
/// <param name="Expert">It finished the most of the room's tasks, and at least <see cref="Seniority.ExpertFrom"/>.</param>
public sealed record RoomSeniority(DeskId Desk, RoomId Room, int Done, int Merged, bool Expert);

/// <summary>
/// Long work in a room makes an inhabitant its expert: seniority counts the room's tasks each
/// desk finished, and the expert's notes come first in that room's briefings.
/// </summary>
public static class Seniority
{
    /// <summary>Finished tasks a desk needs before it can be its room's expert.</summary>
    public const int ExpertFrom = 3;

    /// <summary>The seniority of every desk of the office, room by room.</summary>
    public static IReadOnlyList<RoomSeniority> Of(Office office, IEnumerable<WorkTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(tasks);
        var finished = tasks.Where(t => t.OfficeId == office.Id && t.Status == WorkTaskStatus.Done && t.DeskId is not null)
            .GroupBy(t => t.DeskId!.Value)
            .ToDictionary(g => g.Key, g => (Done: g.Count(), Merged: g.Count(t => t.MergedInto is not null)));

        var all = new List<RoomSeniority>();
        foreach (var room in office.Rooms)
        {
            var desks = room.Desks
                .Select(d => finished.TryGetValue(d.Id, out var count) ? (Desk: d, count.Done, count.Merged) : (Desk: d, Done: 0, Merged: 0))
                .ToList();
            var expert = desks.Where(d => d.Done >= ExpertFrom)
                .OrderByDescending(d => d.Done).ThenByDescending(d => d.Merged).ThenBy(d => d.Desk.Name, StringComparer.Ordinal)
                .Select(d => (DeskId?)d.Desk.Id)
                .FirstOrDefault();
            all.AddRange(desks.Select(d => new RoomSeniority(d.Desk.Id, room.Id, d.Done, d.Merged, d.Desk.Id == expert)));
        }

        return all;
    }

    /// <summary>The expert of a room, if it has one.</summary>
    public static DeskId? ExpertOf(Office office, Room room, IEnumerable<WorkTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(room);
        return Of(office, tasks).FirstOrDefault(s => s.Room == room.Id && s.Expert)?.Desk;
    }
}
