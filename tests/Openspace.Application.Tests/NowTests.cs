using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Application.Offices;
using Openspace.Application.Queue;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class PathTokensTests
{
    [Theory]
    [InlineData("Fix the total in `src/web/cart.ts`.", "src/web/cart.ts")]
    [InlineData("Look at ./src/web/cart.ts, then stop", "src/web/cart.ts")]
    [InlineData("Everything under src/web/** please", "src/web/")]
    [InlineData("The folder src/web/ only", "src/web/")]
    [InlineData("Rename cart.ts", "cart.ts")]
    [InlineData(@"On Windows: src\web\cart.ts", "src/web/cart.ts")]
    [InlineData("The file /srv/repos/shop/src/web/cart.ts", "src/web/cart.ts")]
    public void Finds_the_paths_a_task_names(string text, string path) =>
        Assert.Equal([path], PathTokens.Extract(text, "/srv/repos/shop"));

    [Theory]
    [InlineData("Read https://example.com/docs/cart.html first")]
    [InlineData("Write to elliot@example.com")]
    [InlineData("Bump to v1.2.3, and/or tidy up, e.g. the cart.")]
    [InlineData("Check example.com")]
    [InlineData("Never read /etc/passwd.conf")]
    [InlineData("Not ../outside/secret.txt")]
    [InlineData("")]
    public void Leaves_out_what_is_not_a_file_of_the_repository(string text) =>
        Assert.Empty(PathTokens.Extract(text, "/srv/repos/shop"));

    [Fact]
    public void Names_each_path_once() =>
        Assert.Equal(["src/a.ts", "src/b.ts"], PathTokens.Extract("src/a.ts, src/b.ts and again src/a.ts", null));
}

public class PathOverlapTests
{
    [Theory]
    [InlineData("src/web/cart.ts", "src/web/cart.ts", true)]
    [InlineData("src/web/", "src/web/cart.ts", true)]
    [InlineData("cart.ts", "src/web/cart.ts", true)]
    [InlineData("src/web/cart.ts", "src/web/", true)]
    [InlineData("src/web/cart.ts", "src/web/cart.tsx", false)]
    [InlineData("src/web/", "src/website/index.ts", false)]
    [InlineData("art.ts", "src/web/cart.ts", false)]
    public void Tells_when_two_paths_touch_the_same_files(string mine, string theirs, bool overlap) =>
        Assert.Equal(overlap, PathOverlap.Shared([mine], [theirs]).Count > 0);
}

public class NowKeeperTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly InMemoryTasks _tasks = new();
    private readonly InMemoryOffices _offices = new();
    private readonly FakeWorkspaces _workspaces = new();
    private readonly FakeVault _vault = new();
    private readonly Office _office = Office.Open("E Corp", Start);
    private readonly Desk _desk;

    public NowKeeperTests()
    {
        var room = _office.OpenRoom("Web", Start);
        _desk = _office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), Start);
        _office.SetRepository("/srv/repos/e-corp", Start);
        _offices.Add(_office);
    }

    private WorkInProgress Work { get; } = new();

    private NowKeeper Keeper() => new(_tasks, _offices, _workspaces, Work, _vault, NullLogger<NowKeeper>.Instance);

    private WorkTask Running(string title, string? description, string workspace)
    {
        var task = WorkTask.Assign(_office, _desk.Id, title, description, "elliot", Start);
        task.Start(Start);
        task.AttachRun(AgentRun.Create("session", workspace, "openspace/" + title.Length), Start);
        _tasks.Add(task);
        return task;
    }

    [Fact]
    public async Task Keeps_the_changed_and_named_files_of_each_running_task()
    {
        var task = Running("Fix the total", "Also look at src/web/total.ts.", "/srv/worktrees/a");
        _workspaces.Changed["/srv/worktrees/a"] = ["src/web/cart.ts"];

        await Keeper().RefreshAsync(default);

        var work = Assert.Single(Work.Current());
        Assert.Equal((task.Id.Value, _office.Id.Value, "E Corp", "Web", "Frontend", "Fix the total"), (work.TaskId, work.OfficeId, work.Office, work.Room, work.Desk, work.Title));
        Assert.Equal(["src/web/cart.ts", "src/web/total.ts"], work.Paths);
        var note = Assert.Single(_vault.Writes);
        Assert.Equal("Openspace", note.Author);
        Assert.Contains("## E Corp", note.Note.Body, StringComparison.Ordinal);
        Assert.Contains("### Frontend (Web)", note.Note.Body, StringComparison.Ordinal);
        Assert.Contains("**Fix the total**", note.Note.Body, StringComparison.Ordinal);
        Assert.Contains("- `src/web/cart.ts`\n", note.Note.Body, StringComparison.Ordinal);
        Assert.Contains("- `src/web/total.ts` (named in the task, not changed yet)", note.Note.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writes_Now_only_when_it_changes()
    {
        Running("Fix the total", null, "/srv/worktrees/a");
        _workspaces.Changed["/srv/worktrees/a"] = ["src/web/cart.ts"];
        var keeper = Keeper();

        await keeper.RefreshAsync(default);
        await keeper.RefreshAsync(default);
        Assert.Single(_vault.Writes);

        _workspaces.Changed["/srv/worktrees/a"] = ["src/web/cart.ts", "src/web/cart.css"];
        await keeper.RefreshAsync(default);
        Assert.Equal(2, _vault.Writes.Count);
        Assert.Equal(3, _workspaces.ChangedReads);
    }

    [Fact]
    public async Task A_vault_that_already_says_the_same_is_not_written_again_after_a_restart()
    {
        _vault.Notes["Now.md"] = Note.Create("Now.md", "Now", NowNote.Body([]));

        await Keeper().RefreshAsync(default);

        Assert.Empty(_vault.Writes);
    }

    [Fact]
    public async Task Says_nobody_works_when_nothing_runs()
    {
        await Keeper().RefreshAsync(default);

        Assert.Contains(NowNote.Nobody, Assert.Single(_vault.Writes).Note.Body, StringComparison.Ordinal);
        Assert.Empty(Work.Current());
    }

    [Fact]
    public async Task A_vault_that_cannot_be_written_leaves_the_list_in_memory()
    {
        Running("Fix the total", null, "/srv/worktrees/a");
        _workspaces.Changed["/srv/worktrees/a"] = ["src/web/cart.ts"];
        _vault.Broken = true;

        await Keeper().RefreshAsync(default);

        Assert.Equal(["src/web/cart.ts"], Assert.Single(Work.Current()).Paths);
    }
}

public class MergeOverlapTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Flags_unmerged_branches_that_touched_the_same_files()
    {
        var office = Office.Open("E Corp", Start);
        var room = office.OpenRoom("Web", Start);
        var desk = office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), Start);
        office.SetRepository("/srv/repos/e-corp", Start);
        var offices = new InMemoryOffices();
        offices.Add(office);
        var tasks = new InMemoryTasks();
        var workspaces = new FakeWorkspaces();
        WorkTask InReview(string title, string branch, params string[] files)
        {
            var task = WorkTask.Assign(office, desk.Id, title, null, "elliot", Start);
            task.Start(Start);
            task.AttachRun(AgentRun.Create("s", "/srv/worktrees/" + branch, branch), Start);
            task.SubmitForReview(Start);
            tasks.Add(task);
            workspaces.BranchChanges[branch] = files;
            return task;
        }

        var cart = InReview("Fix the total", "openspace/a", "src/web/cart.ts", "src/web/total.ts");
        var copy = InReview("Translate the cart", "openspace/b", "src/web/cart.ts");
        InReview("Fix the login", "openspace/c", "src/web/login.ts");

        var list = await new OfficeQueries(offices, workspaces, new FakePullRequests(), tasks).BranchesAsync(office.Id, default);

        var overlap = Assert.Single(list.Overlaps!);
        Assert.Equal((cart.Id.Value, copy.Id.Value), (overlap.TaskId, overlap.OtherTaskId));
        Assert.Equal(["src/web/cart.ts"], overlap.Files);
    }
}

internal sealed class FakeVault : IVault
{
    public Dictionary<string, Note> Notes { get; } = [];

    public List<(Note Note, string Author)> Writes { get; } = [];

    public bool Broken { get; set; }

    public string Folder => "/srv/brain";

    public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<Note>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Note>>([.. Notes.Values]);

    public Task<Note?> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Notes.GetValueOrDefault(path));

    public Task WriteAsync(Note note, string author, CancellationToken cancellationToken, string? summary = null)
    {
        if (Broken)
        {
            throw new InvalidOperationException("git is not installed");
        }

        Notes[note.Path] = note;
        Writes.Add((note, author));
        return Task.CompletedTask;
    }
}
