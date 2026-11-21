using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Brain;
using Openspace.Application.Queue;

namespace Openspace.Api.Tests;

public sealed class BrainApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public BrainApiTests()
    {
        // Notes the person already had: the vault is made around them on start.
        Directory.CreateDirectory(Path.Combine(_app.BrainFolder, "Decisions"));
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-09-01-merge-order.md"), """
            ---
            kind: decision
            status: superseded
            offices: [Allsafe]
            ---
            # Merge order, first try

            Merge by size, smallest first.
            """);
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-11-20-merge-order.md"), """
            ---
            kind: decision
            status: accepted
            offices: [Allsafe]
            rooms: [Web]
            paths: ["src/web/**"]
            review: 2027-02-20
            supersedes: "[[2026-09-01-merge-order]]"
            author: Screens
            ---
            # Merge order

            Merge by approval, oldest first.
            """);
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-10-01-shop-merges.md"), """
            ---
            kind: decision
            status: accepted
            offices: [E Corp]
            ---
            # Merges at E Corp

            Merge on Fridays only.
            """);
        _http = _app.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private sealed record Hit(string Path, string Title, string? Kind, string? Status, List<string> Offices, string Excerpt);

    private sealed record NoteView(
        string Path, string? Kind, string? Status, string Title, List<string> Offices, List<string> Rooms, List<string> Paths,
        DateOnly? Review, string? Supersedes, string? Author, List<string> Links, string Body);

    private async Task<List<Hit>> SearchAsync(string query) => (await _http.GetFromJsonAsync<List<Hit>>($"/api/brain/search?{query}", Json))!;

    [Fact]
    public async Task Search_ranks_by_scope_and_puts_superseded_decisions_last()
    {
        var hits = await SearchAsync("q=merge&office=Allsafe&room=Web&path=src/web/cart.ts&kind=decision");

        Assert.Equal(
            ["Decisions/2026-11-20-merge-order.md", "Decisions/2026-10-01-shop-merges.md", "Decisions/2026-09-01-merge-order.md"],
            hits.Select(h => h.Path));
        Assert.Equal("Merge order", hits[0].Title);
        Assert.Contains("oldest first", hits[0].Excerpt, StringComparison.Ordinal);
        Assert.Empty(await SearchAsync("q=merge&kind=study"));
    }

    [Fact]
    public async Task A_note_comes_with_its_front_matter_as_fields()
    {
        var note = (await _http.GetFromJsonAsync<NoteView>("/api/brain/notes/Decisions/2026-11-20-merge-order", Json))!;

        Assert.Equal("Decisions/2026-11-20-merge-order.md", note.Path);
        Assert.Equal(("decision", "accepted", "Screens"), (note.Kind, note.Status, note.Author));
        Assert.Equal(["Allsafe"], note.Offices);
        Assert.Equal(["Web"], note.Rooms);
        Assert.Equal(["src/web/**"], note.Paths);
        Assert.Equal(new DateOnly(2027, 2, 20), note.Review);
        Assert.Equal(["2026-09-01-merge-order"], note.Links);
        Assert.StartsWith("# Merge order", note.Body, StringComparison.Ordinal);

        // The vault was made around the person's notes: the constitution is there too.
        using var constitution = await _http.GetAsync("/api/brain/notes/Constitution.md");
        Assert.Equal(HttpStatusCode.OK, constitution.StatusCode);
    }

    [Fact]
    public async Task Missing_notes_are_not_found_and_paths_outside_the_vault_are_refused()
    {
        using var missing = await _http.GetAsync("/api/brain/notes/Decisions/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // "../" never arrives: the address is cleaned before it is sent. Hidden folders do arrive.
        using var git = await _http.GetAsync("/api/brain/notes/.git/config");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, git.StatusCode);
    }

    [Fact]
    public async Task A_note_saved_on_disk_while_Openspace_runs_is_found()
    {
        await SearchAsync("q=merge");
        await File.WriteAllTextAsync(Path.Combine(_app.BrainFolder, "Decisions", "queues.md"), "# Queues\n\nA zeppelin of a merge queue.\n");

        var until = DateTime.UtcNow.AddSeconds(10);
        while ((await SearchAsync("q=zeppelin")).Count == 0)
        {
            Assert.True(DateTime.UtcNow < until, "The watcher did not see the new note in time.");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Now_lists_who_is_changing_what()
    {
        Assert.Empty((await _http.GetFromJsonAsync<List<WorkInFlight>>("/api/brain/now", Json))!);
        var work = new WorkInFlight(Guid.NewGuid(), Guid.NewGuid(), "Allsafe", "Web", "Screens", "Fix the total", ["src/web/cart.ts"]);
        _app.Services.GetRequiredService<WorkInProgress>().Replace([new RunningWork(work, ["src/web/cart.ts"], [])]);

        var now = (await _http.GetFromJsonAsync<List<WorkInFlight>>("/api/brain/now", Json))!;

        var only = Assert.Single(now);
        Assert.Equal((work.TaskId, "Screens", "Fix the total"), (only.TaskId, only.Desk, only.Title));
        Assert.Equal(["src/web/cart.ts"], only.Paths);
    }

    [Fact]
    public async Task Now_md_is_put_back_by_Openspace_in_one_commit()
    {
        // The vault is made on start; wait for it, then edit and commit the note by hand as a person might.
        await SearchAsync("q=merge");
        await File.WriteAllTextAsync(Path.Combine(_app.BrainFolder, "Now.md"), "# Now\n\nScreens is on the cart.\n");
        await GitAsync("-c", "user.name=Darlene", "-c", "user.email=darlene@example.invalid", "commit", "--quiet", "--all", "--message", "By hand");
        async Task RefreshAsync()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NowKeeper>().RefreshAsync(default);
        }

        await RefreshAsync();
        await RefreshAsync();
        await RefreshAsync();

        var note = (await _http.GetFromJsonAsync<NoteView>("/api/brain/notes/Now", Json))!;
        Assert.Equal("Openspace", note.Author);
        Assert.Contains(NowNote.Nobody, note.Body, StringComparison.Ordinal);
        Assert.Equal(1, await CommitsOfNowAsync());
    }

    /// <summary>Commits Openspace made to Now.md when what it says changed.</summary>
    private async Task<int> CommitsOfNowAsync() =>
        (await GitAsync("log", "--format=%s", "--", "Now.md"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("who is changing what", StringComparison.Ordinal));

    private async Task<string> GitAsync(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = _app.BrainFolder,
            RedirectStandardOutput = true,
        };
        using var git = System.Diagnostics.Process.Start(start)!;
        var output = await git.StandardOutput.ReadToEndAsync();
        await git.WaitForExitAsync();
        Assert.Equal(0, git.ExitCode);
        return output;
    }
}
