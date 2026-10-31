using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Guests;
using Openspace.Domain.Offices;

namespace Openspace.Application.Tests;

public class GuestTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemoryOffices _offices = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly FakeSessions _sessions = new();
    private readonly GuestBoard _board;
    private readonly GuestWatcher _watcher;
    private readonly Office _ecorp = Office.Open("E Corp", Start);
    private readonly Office _website = Office.Open("Website", Start);

    public GuestTests()
    {
        _ecorp.SetRepository("/srv/ecorp", Start);
        _website.SetRepository("/srv/ecorp/website", Start);
        _offices.Items.AddRange([_ecorp, _website, Office.Open("Allsafe", Start)]);
        _board = new GuestBoard(_publisher);
        _watcher = new GuestWatcher(_sessions, _offices, _unitOfWork, _board, Options.Create(new GuestOptions()), _clock);
    }

    [Fact]
    public async Task A_session_is_the_guest_of_the_deepest_office_holding_its_folder()
    {
        _sessions.Traces = [Trace("s1", "/srv/ecorp/website/src"), Trace("s2", "/srv/ecorp/api"), Trace("s3", "/srv/ecorpse"), Trace("s4", "/home/elliot")];

        await _watcher.ScanAsync(CancellationToken.None);

        Assert.Equal("src", Assert.Single(_board.Snapshot(_website.Id.Value)).Folder);
        Assert.Equal("api", Assert.Single(_board.Snapshot(_ecorp.Id.Value)).Folder);
        Assert.Equal(2, _board.Snapshot().Count);
    }

    [Fact]
    public async Task The_first_guest_opens_the_guest_room_once()
    {
        _sessions.Traces = [Trace("s1", "/srv/ecorp")];

        await _watcher.ScanAsync(CancellationToken.None);
        await _watcher.ScanAsync(CancellationToken.None);

        Assert.Single(_ecorp.Rooms, r => r.Kind == RoomKind.Guests);
        Assert.DoesNotContain(_website.Rooms, r => r.Kind == RoomKind.Guests);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Equal(string.Empty, Assert.Single(_board.Snapshot()).Folder);
    }

    [Fact]
    public async Task In_an_office_with_private_folders_a_guest_stays_anonymous()
    {
        _ecorp.SetPrivatePaths(["/srv/ecorp/secrets"], Start);
        _sessions.Traces = [Trace("s1", "/srv/ecorp/api", branch: "fix/login")];

        await _watcher.ScanAsync(CancellationToken.None);

        var guest = Assert.Single(_board.Snapshot());
        Assert.Null(guest.Folder);
        Assert.Null(guest.Branch);
        Assert.DoesNotContain("s1", guest.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offices_hear_only_of_changes_and_of_guests_leaving()
    {
        _sessions.Traces = [Trace("s1", "/srv/ecorp/api")];
        await _watcher.ScanAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(5));
        _sessions.Traces = [Trace("s1", "/srv/ecorp/api") with { LastAt = Start.AddSeconds(5) }];
        await _watcher.ScanAsync(CancellationToken.None);
        var since = Assert.Single(_board.Snapshot()).Since;

        _sessions.Traces = [];
        await _watcher.ScanAsync(CancellationToken.None);

        Assert.Equal(Start, since);
        Assert.Equal([1, 0], _publisher.Guests.Select(g => g.Guests.Count));
    }

    private static SessionTrace Trace(string id, string folder, string? branch = null) => new(id, folder, branch, Start, GuestState.Working);

    private sealed class FakeSessions : IClaudeSessions
    {
        public IReadOnlyList<SessionTrace> Traces { get; set; } = [];

        public Task<IReadOnlyList<SessionTrace>> RecentAsync(TimeSpan within, CancellationToken cancellationToken) => Task.FromResult(Traces);
    }
}
