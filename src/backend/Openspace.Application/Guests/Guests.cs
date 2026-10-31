using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Guests;

/// <summary>What a guest is doing: working, reading, or waiting for its own person.</summary>
public enum GuestState
{
    Working,
    Reading,
    Idle,
}

/// <summary>
/// A Claude Code session started outside Openspace, as its log tells it: the folder it
/// works in now, its branch, when it last did something and what.
/// </summary>
public sealed record SessionTrace(string SessionId, string Folder, string? Branch, DateTimeOffset LastAt, GuestState State);

/// <summary>The logs of the Claude Code sessions people start themselves, read but never touched.</summary>
public interface IClaudeSessions
{
    /// <summary>The sessions that did something within <paramref name="within"/>.</summary>
    Task<IReadOnlyList<SessionTrace>> RecentAsync(TimeSpan within, CancellationToken cancellationToken);
}

/// <summary>
/// A guest in an office: a session working on its folder. In an office with private folders
/// it stays anonymous: no folder, no branch.
/// </summary>
/// <param name="Id">A short name of its own, the same for the whole session.</param>
/// <param name="Folder">Where it works, from the office's repository; empty at its root.</param>
public sealed record Guest(string Id, Guid OfficeId, string? Folder, string? Branch, GuestState State, DateTimeOffset Since, DateTimeOffset LastAt);

/// <summary>The guests of one office, for whoever is watching.</summary>
public sealed record OfficeGuests(Guid OfficeId, IReadOnlyList<Guest> Guests);

public sealed class GuestOptions
{
    public const string SectionName = "Guests";

    /// <summary>Watch for sessions started outside Openspace. Off in tests.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A session quiet for this long has left.</summary>
    public TimeSpan StayFor { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Where Claude Code keeps its sessions' logs; empty for ~/.claude/projects.</summary>
    public string Folder { get; set; } = string.Empty;
}

/// <summary>The guests of every office, kept in memory like the bubbles.</summary>
public sealed class GuestBoard(IActivityPublisher publisher)
{
    private readonly Lock _gate = new();
    private Dictionary<Guid, IReadOnlyList<Guest>> _byOffice = [];

    public IReadOnlyList<Guest> Snapshot(Guid? officeId = null)
    {
        lock (_gate)
        {
            return [.. _byOffice.Where(o => officeId is null || o.Key == officeId).SelectMany(o => o.Value)];
        }
    }

    /// <summary>The guests now; offices whose guests changed hear about it, emptied ones too.</summary>
    public async Task ReplaceAsync(IReadOnlyList<Guest> guests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guests);
        var next = guests.GroupBy(g => g.OfficeId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guest>)[.. g.OrderBy(x => x.Since).ThenBy(x => x.Id, StringComparer.Ordinal)]);
        List<OfficeGuests> changed;
        lock (_gate)
        {
            changed = [.. next.Keys.Union(_byOffice.Keys)
                .Where(id => !Same(_byOffice.GetValueOrDefault(id), next.GetValueOrDefault(id)))
                .Select(id => new OfficeGuests(id, next.GetValueOrDefault(id) ?? []))];
            _byOffice = next;
        }

        foreach (var office in changed)
        {
            await publisher.GuestsChangedAsync(office, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The same guests doing the same: a newer timestamp alone is no news.</summary>
    private static bool Same(IReadOnlyList<Guest>? a, IReadOnlyList<Guest>? b) =>
        (a ?? []).Select(g => g with { LastAt = default }).SequenceEqual((b ?? []).Select(g => g with { LastAt = default }));
}

/// <summary>
/// Finds the guests of each office: a session belongs to the office whose repository holds
/// the folder it works in now, the deepest one when repositories sit inside each other. The
/// first guest of an office opens its guest room.
/// </summary>
public sealed class GuestWatcher(
    IClaudeSessions sessions, IOfficeRepository offices, IUnitOfWork unitOfWork, GuestBoard board, IOptions<GuestOptions> options, TimeProvider clock)
{
    public const string RoomName = "Guest room";

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var traces = await sessions.RecentAsync(options.Value.StayFor, cancellationToken).ConfigureAwait(false);
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        var before = board.Snapshot().ToDictionary(g => (g.OfficeId, g.Id));
        var now = clock.GetUtcNow();
        var guests = new List<Guest>();
        var opened = false;
        foreach (var trace in traces)
        {
            var office = OfficeOf(all, trace.Folder);
            if (office is null)
            {
                continue;
            }

            if (!office.Rooms.Any(r => r.Kind == RoomKind.Guests))
            {
                office.GuestRoom(RoomName, now);
                opened = true;
            }

            var id = NameOf(trace.SessionId);
            var anonymous = office.PrivatePaths.Count > 0;
            var since = before.TryGetValue((office.Id.Value, id), out var known) ? known.Since : trace.LastAt;
            guests.Add(new Guest(
                id,
                office.Id.Value,
                anonymous ? null : Relative(office.RepositoryPath!, trace.Folder),
                anonymous ? null : trace.Branch,
                trace.State,
                since,
                trace.LastAt));
        }

        if (opened)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await board.ReplaceAsync(guests, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The office whose repository holds the folder, the deepest first.</summary>
    public static Office? OfficeOf(IEnumerable<Office> offices, string folder) =>
        offices
            .Where(o => o.RepositoryPath is { } repo && Holds(repo, folder))
            .OrderByDescending(o => o.RepositoryPath!.Length)
            .FirstOrDefault();

    private static bool Holds(string repository, string folder)
    {
        var root = repository.TrimEnd('/', '\\');
        return folder.Equals(root, StringComparison.Ordinal)
            || (folder.StartsWith(root, StringComparison.Ordinal) && folder.Length > root.Length && folder[root.Length] is '/' or '\\');
    }

    private static string Relative(string repository, string folder) =>
        folder.Length <= repository.TrimEnd('/', '\\').Length ? string.Empty : folder[(repository.TrimEnd('/', '\\').Length + 1)..];

    /// <summary>A short name for a session that does not give its id away.</summary>
    public static string NameOf(string sessionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..10];
}
