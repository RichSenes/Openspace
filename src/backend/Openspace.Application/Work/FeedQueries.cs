using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>One thing that happened in the building, with who did it and on which task.</summary>
/// <param name="Excerpt">For a note, the start of what it says.</param>
public sealed record FeedLine(
    long Id,
    DateTimeOffset At,
    AuditKind Kind,
    string Summary,
    bool? Succeeded,
    Guid TaskId,
    string TaskTitle,
    Guid OfficeId,
    string OfficeName,
    Guid? RoomId,
    string? RoomName,
    Guid? DeskId,
    string? DeskName,
    string? Excerpt = null);

/// <summary>
/// The live feed: the latest lines of every task's log, newest first, for one office, one
/// room or the whole building. Reads and searches are left out unless everything is asked.
/// </summary>
public sealed class FeedQueries(IOfficeRepository offices, IWorkTaskRepository tasks, IAuditLog audit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>What the feed shows unless everything is asked: what changed something, or says where the work is.</summary>
    public static readonly IReadOnlyCollection<AuditKind> Highlights =
        [AuditKind.Lifecycle, AuditKind.Step, AuditKind.Change, AuditKind.Command, AuditKind.Permission, AuditKind.Note, AuditKind.Procedure];

    /// <param name="before">Only lines older than this one: the next page.</param>
    public async Task<IReadOnlyList<FeedLine>> ListAsync(
        OfficeId? officeId, RoomId? roomId, bool everything, long? before, int? limit, CancellationToken cancellationToken)
    {
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        var office = officeId is { } id ? all.FirstOrDefault(o => o.Id == id) ?? throw new NotFoundException("Office", id.Value) : null;
        var room = roomId is { } r
            ? office?.Rooms.FirstOrDefault(x => x.Id == r) ?? throw new NotFoundException("Room", r.Value)
            : null;

        var found = await tasks.ListAsync(officeId, null, cancellationToken).ConfigureAwait(false);
        var shown = room is null ? found : [.. found.Where(t => t.DeskId is { } d && room.Desks.Any(x => x.Id == d))];
        var byId = shown.ToDictionary(t => t.Id);
        var lines = await audit.LatestAsync(
            officeId is null ? null : byId.Keys,
            everything ? null : Highlights,
            before,
            Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit),
            cancellationToken).ConfigureAwait(false);

        var officesById = all.ToDictionary(o => o.Id);
        return [.. lines.Where(e => byId.ContainsKey(e.TaskId)).Select(e => Line(e, byId[e.TaskId], officesById))];
    }

    private static FeedLine Line(AuditEntry entry, WorkTask task, Dictionary<OfficeId, Office> offices)
    {
        var office = offices.GetValueOrDefault(task.OfficeId);
        var place = task.DeskId is { } deskId && office?.HasDesk(deskId) == true ? office.Locate(deskId) : default;
        return new FeedLine(
            entry.Id, entry.At, entry.Kind, entry.Summary, entry.Succeeded,
            task.Id.Value, task.Title,
            task.OfficeId.Value, office?.Name ?? "An office",
            place.Room?.Id.Value, place.Room?.Name, place.Desk?.Id.Value, place.Desk?.Name,
            entry.Kind == AuditKind.Note ? Excerpt(entry.Detail) : null);
    }

    /// <summary>How much of a note the feed shows.</summary>
    public const int ExcerptLength = 160;

    private static string? Excerpt(string? text)
    {
        var line = text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is null || line.Length <= ExcerptLength ? line : line[..(ExcerptLength - 1)].TrimEnd() + "…";
    }
}
