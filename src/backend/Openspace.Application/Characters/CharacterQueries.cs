using Openspace.Application.Abstractions;
using Openspace.Application.Offices;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>
/// The character monitor: who each inhabitant is and how they are doing, for the person only.
/// Agents never see these numbers: no tool and no briefing reads them.
/// </summary>
public sealed class CharacterQueries(IOfficeRepository offices, IWorkTaskRepository tasks, IAuditLog audit, TimeProvider clock)
{
    /// <summary>Lines of the log read per page.</summary>
    private const int Page = 1000;

    /// <summary>The kinds of log line office life is read from.</summary>
    private static readonly AuditKind[] LifeKinds = [AuditKind.Lifecycle, AuditKind.Change];

    public async Task<CharacterBoardView> GetAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var all = await tasks.ListAsync(officeId, null, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var log = await LogSinceAsync(audit, all, now - Relationships.Window, cancellationToken).ConfigureAwait(false);
        var events = LifeEvents.From(all, log);
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
            [.. seniority.Where(s => s.Desk == desk.Id).Select(s => new SeniorityView(s.Room.Value, room.Name, s.Done, s.Merged, s.Expert))],
            Mood.Of(desk.Traits, events.Where(e => e.Desk == desk.Id), now, clock.LocalTimeZone))))
            .ToList();

        var experts = seniority.Where(s => s.Expert)
            .Select(s => new RoomExpertView(s.Room.Value, office.Rooms.First(r => r.Id == s.Room).Name, s.Desk.Value, office.Locate(s.Desk).Desk.Name, s.Done))
            .ToList();
        var names = office.Rooms.SelectMany(r => r.Desks).ToDictionary(d => d.Id, d => d.Name);
        var relationships = Relationships.Of(all, log, names, now)
            .Select(r => new RelationshipView(r.First.Value, names[r.First], r.Second.Value, names[r.Second], r.Affinity, r.History))
            .ToList();
        return new CharacterBoardView(office.Id.Value, office.Name, characters, experts, relationships);
    }

    /// <summary>The lifecycle and change lines of these tasks from <paramref name="since"/> on, oldest first.</summary>
    public static async Task<IReadOnlyList<AuditEntry>> LogSinceAsync(
        IAuditLog audit, IReadOnlyCollection<WorkTask> of, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(of);
        var ids = of.Select(t => t.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var lines = new List<AuditEntry>();
        long? before = null;
        while (true)
        {
            var page = await audit.LatestAsync(ids, LifeKinds, before, Page, cancellationToken).ConfigureAwait(false);
            lines.AddRange(page.Where(e => e.At >= since));
            if (page.Count < Page || page[^1].At < since || page[^1].Id <= 0)
            {
                break;
            }

            before = page[^1].Id;
        }

        return [.. lines.OrderBy(e => e.At)];
    }
}

/// <param name="Relationships">Between desks only, from the last month's real events, the strongest first.</param>
public sealed record CharacterBoardView(
    Guid OfficeId, string OfficeName, IReadOnlyList<CharacterView> Characters, IReadOnlyList<RoomExpertView> Experts, IReadOnlyList<RelationshipView> Relationships);

/// <param name="Affinity">From -100 to 100: the sum of the history's changes.</param>
public sealed record RelationshipView(Guid FirstId, string FirstName, Guid SecondId, string SecondName, int Affinity, IReadOnlyList<TieEvent> History);

/// <param name="Mood">From 0 to 100, with every change of the last week and its reason.</param>
public sealed record CharacterView(
    Guid DeskId,
    string Name,
    Guid RoomId,
    string RoomName,
    IReadOnlyList<Trait> Traits,
    BirthdayView Birthday,
    Shift Shift,
    IReadOnlyDictionary<TrustedAction, TrustLevel> Trust,
    IReadOnlyList<SeniorityView> Seniority,
    MoodLine Mood);

/// <param name="Done">Tasks of the room it finished.</param>
/// <param name="Expert">It is the room's expert.</param>
public sealed record SeniorityView(Guid RoomId, string RoomName, int Done, int Merged, bool Expert);

public sealed record RoomExpertView(Guid RoomId, string RoomName, Guid DeskId, string DeskName, int Done);
