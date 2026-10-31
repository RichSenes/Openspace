using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class FeedQueriesTests
{
    private readonly Harness _h = new();

    private FeedQueries Feed() => new(_h.Offices, _h.Tasks, _h.Audit);

    private void Log(WorkTask task, AuditKind kind, string summary) =>
        _h.Audit.Add(AuditEntry.Record(task.Id, _h.Clock.GetUtcNow(), kind, summary));

    [Fact]
    public async Task Shows_the_latest_highlights_first_with_who_did_them_and_where()
    {
        var task = _h.Enqueue("Add a logout button");
        Log(task, AuditKind.Step, "Adding the button");
        Log(task, AuditKind.Read, "Read header.php");
        Log(task, AuditKind.Change, "Changed header.php");

        var lines = await Feed().ListAsync(_h.Office.Id, null, false, null, null, default);

        Assert.Equal(["Changed header.php", "Adding the button"], lines.Select(l => l.Summary));
        var line = lines[0];
        Assert.Equal(
            ("Add a logout button", "E Corp", "Web", "Frontend"),
            (line.TaskTitle, line.OfficeName, line.RoomName, line.DeskName));
    }

    [Fact]
    public async Task A_note_shows_the_start_of_what_it_says()
    {
        var task = _h.Enqueue();
        _h.Audit.Add(AuditEntry.Record(task.Id, _h.Clock.GetUtcNow(), AuditKind.Note, "Note from Frontend", "\n  The header is shared with the admin area.\nMore below."));
        _h.Audit.Add(AuditEntry.Record(task.Id, _h.Clock.GetUtcNow(), AuditKind.Note, "Note from elliot", new string('a', 400)));
        Log(task, AuditKind.Change, "Changed header.php");

        var lines = await Feed().ListAsync(_h.Office.Id, null, false, null, null, default);

        Assert.Null(lines[0].Excerpt);
        Assert.Equal(FeedQueries.ExcerptLength, lines[1].Excerpt!.Length);
        Assert.Equal("The header is shared with the admin area.", lines[2].Excerpt);
    }

    [Fact]
    public async Task Everything_includes_reads_and_searches()
    {
        var task = _h.Enqueue();
        Log(task, AuditKind.Read, "Read header.php");
        Log(task, AuditKind.Search, "Searched for logout");

        var lines = await Feed().ListAsync(_h.Office.Id, null, true, null, null, default);

        Assert.Equal(["Searched for logout", "Read header.php"], lines.Select(l => l.Summary));
    }

    [Fact]
    public async Task A_room_shows_only_the_work_of_its_desks_and_the_building_shows_every_office()
    {
        var api = _h.Office.OpenRoom("Api", Harness.Start);
        var backend = _h.Office.AddDesk(api.Id, "Backend", AgentProfile.Create("backend-dev"), Harness.Start);
        var web = _h.Enqueue("Add a logout button");
        var server = WorkTask.Assign(_h.Office, backend.Id, "Rate limit the login", null, "elliot", Harness.Start);
        _h.Tasks.Add(server);
        var other = Office.Open("Allsafe", Harness.Start);
        var room = other.OpenRoom("Security", Harness.Start);
        var desk = other.AddDesk(room.Id, "Scanner", AgentProfile.Create("scanner"), Harness.Start);
        _h.Offices.Add(other);
        var scan = WorkTask.Assign(other, desk.Id, "Scan the network", null, "angela", Harness.Start);
        _h.Tasks.Add(scan);
        Log(web, AuditKind.Change, "Changed header.php");
        Log(server, AuditKind.Command, "Ran the tests");
        Log(scan, AuditKind.Note, "Two open ports");

        var inApi = await Feed().ListAsync(_h.Office.Id, api.Id, false, null, null, default);
        var inOffice = await Feed().ListAsync(_h.Office.Id, null, false, null, null, default);
        var everywhere = await Feed().ListAsync(null, null, false, null, null, default);

        Assert.Equal(["Ran the tests"], inApi.Select(l => l.Summary));
        Assert.Equal(2, inOffice.Count);
        Assert.Equal(["Allsafe", "E Corp", "E Corp"], everywhere.Select(l => l.OfficeName));
    }

    [Fact]
    public async Task Asks_for_at_most_the_limit_and_refuses_an_unknown_room()
    {
        var task = _h.Enqueue();
        for (var i = 0; i < 5; i++)
        {
            Log(task, AuditKind.Change, $"Change {i}");
        }

        Assert.Equal(2, (await Feed().ListAsync(_h.Office.Id, null, false, null, 2, default)).Count);
        await Assert.ThrowsAsync<NotFoundException>(() => Feed().ListAsync(_h.Office.Id, RoomId.New(), false, null, null, default));
    }
}
