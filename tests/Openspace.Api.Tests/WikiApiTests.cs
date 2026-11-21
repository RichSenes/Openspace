using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Openspace.Api.Tests;

public sealed class WikiApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public WikiApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_app.BrainFolder, "Decisions"));
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-11-20-cart-totals.md"), """
            ---
            kind: decision
            status: accepted
            offices: [Allsafe]
            paths: ["src/web/**"]
            from: "task 01a1"
            ---
            # Cart totals

            Totals are computed on the server. See [[Plans/checkout|the checkout plan]].
            """);
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-10-01-old-totals.md"), """
            ---
            kind: decision
            status: superseded
            paths: ["src/web/**"]
            ---
            # Old totals

            Totals in the browser. Part of [[checkout]].
            """);
        _http = _app.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private sealed record CommitView(string Commit, string Author, DateTimeOffset Date, string Summary);

    private sealed record VersionView(CommitView Commit, string Path, string? Text, string Diff, string? Previous, bool Truncated);

    private sealed record TreeNote(string Path, string Name, string Title, string? Kind, string? Status);

    private sealed record Folder(string Name, string Path, List<Folder> Folders, List<TreeNote> Notes);

    private sealed record Backlink(string Path, string Title, string? Kind, string? Status);

    private sealed record Reason(string Path, string Title, DateOnly Decided, List<string> Covering, string? From, bool General);

    private sealed record NoteView(string Path, string? Kind, string Title, string? Author, string Body);

    private sealed record Problem(string? Code);

    private Task<HttpResponseMessage> PutAsync(string path, string text, string? summary = null) =>
        _http.PutAsJsonAsync($"/api/brain/notes/{path}", new { text, summary }, Json);

    private async Task<List<CommitView>> HistoryAsync(string path) =>
        (await _http.GetFromJsonAsync<List<CommitView>>($"/api/brain/notes/{path}/history", Json))!;

    [Fact]
    public async Task The_tree_shows_the_vault_in_its_folders()
    {
        var root = (await _http.GetFromJsonAsync<Folder>("/api/brain/tree", Json))!;

        Assert.Contains(root.Notes, n => n.Path == "Constitution.md");
        Assert.Contains(root.Notes, n => n.Path == "Now.md");
        var decisions = Assert.Single(root.Folders, f => f.Name == "Decisions");
        var totals = Assert.Single(decisions.Notes, n => n.Name == "2026-11-20-cart-totals.md");
        Assert.Equal(("Cart totals", "decision", "accepted"), (totals.Title, totals.Kind, totals.Status));
    }

    [Fact]
    public async Task The_person_writes_a_note_and_its_history_shows_each_version_with_its_diff()
    {
        using (var created = await PutAsync("Plans/checkout", "# Checkout\n\nPay with a card.\n"))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var note = (await created.Content.ReadFromJsonAsync<NoteView>(Json))!;
            Assert.Equal(("Plans/checkout.md", "Checkout"), (note.Path, note.Title));
        }

        using (var edited = await PutAsync("Plans/checkout.md", "---\nkind: plan\n---\n# Checkout\n\nPay with a card or in cash.\n", "added cash"))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        }

        var history = await HistoryAsync("Plans/checkout.md");

        Assert.Equal(2, history.Count);
        Assert.All(history, c => Assert.Equal("elliot", c.Author));
        Assert.Equal(["added cash", "wrote Plans/checkout.md"], history.Select(c => c.Summary));
        Assert.True(history[0].Date >= history[1].Date);

        var newer = (await _http.GetFromJsonAsync<VersionView>($"/api/brain/notes/Plans/checkout.md/history/{history[0].Commit}", Json))!;
        Assert.Equal(history[0].Commit, newer.Commit.Commit);
        Assert.Equal(history[1].Commit, newer.Previous);
        Assert.Contains("Pay with a card or in cash.", newer.Text, StringComparison.Ordinal);
        Assert.Contains("\n-Pay with a card.\n", newer.Diff, StringComparison.Ordinal);
        Assert.Contains("\n+Pay with a card or in cash.\n", newer.Diff, StringComparison.Ordinal);
        Assert.Contains("\n+kind: plan\n", newer.Diff, StringComparison.Ordinal);
        Assert.False(newer.Truncated);

        // The first version, by a short hash: its diff is from nothing.
        var first = (await _http.GetFromJsonAsync<VersionView>($"/api/brain/notes/Plans/checkout/history/{history[1].Commit[..10]}", Json))!;
        Assert.Null(first.Previous);
        Assert.Equal("# Checkout\n\nPay with a card.\n", first.Text);
        Assert.Contains("new file mode", first.Diff, StringComparison.Ordinal);

        // Plain reads of a note still reach their own route.
        var read = (await _http.GetFromJsonAsync<NoteView>("/api/brain/notes/Plans/checkout", Json))!;
        Assert.Equal("plan", read.Kind);
    }

    [Fact]
    public async Task Unknown_or_malformed_commits_are_not_found()
    {
        var head = (await HistoryAsync("Constitution.md"))[0].Commit;

        using var unknown = await _http.GetAsync("/api/brain/notes/Constitution.md/history/0000000000000000000000000000000000000000");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var option = await _http.GetAsync("/api/brain/notes/Constitution.md/history/HEAD");
        Assert.Equal(HttpStatusCode.NotFound, option.StatusCode);
        using var missing = await _http.GetAsync("/api/brain/notes/Plans/nothing.md/history");
        Assert.Empty((await missing.Content.ReadFromJsonAsync<List<CommitView>>(Json))!);

        // A commit that did not touch the note still shows the note as it was then.
        var then = (await _http.GetFromJsonAsync<VersionView>($"/api/brain/notes/Constitution.md/history/{head}", Json))!;
        Assert.NotNull(then.Text);
    }

    [Fact]
    public async Task The_constitution_changes_through_the_person_and_Now_never_does()
    {
        var before = await HistoryAsync("Constitution.md");
        var text = (await _http.GetFromJsonAsync<NoteView>("/api/brain/notes/Constitution.md", Json))!.Body;

        using var edited = await PutAsync("Constitution.md", $"---\nkind: rule\n---\n{text}\n9. Tests before merging.\n");

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var after = await HistoryAsync("Constitution.md");
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(("elliot", "edited Constitution.md"), (after[0].Author, after[0].Summary));
        Assert.Equal("Openspace", after[^1].Author);
        Assert.Contains("9. Tests before merging.", await File.ReadAllTextAsync(Path.Combine(_app.BrainFolder, "Constitution.md")), StringComparison.Ordinal);

        using var now = await PutAsync("Now.md", "# Now\n\nNothing.\n");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, now.StatusCode);
        Assert.Equal("brain-now", (await now.Content.ReadFromJsonAsync<Problem>(Json))!.Code);

        using var hidden = await PutAsync(".git/config", "nope");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, hidden.StatusCode);
        Assert.Equal("brain-path", (await hidden.Content.ReadFromJsonAsync<Problem>(Json))!.Code);
        Assert.False(File.Exists(Path.Combine(_app.BrainFolder, ".git", "config.md")));

        using var empty = await _http.PutAsJsonAsync("/api/brain/notes/Plans/empty", new { summary = "no text" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Backlinks_list_the_notes_that_link_to_one()
    {
        using var written = await PutAsync("Plans/checkout", "# Checkout\n");

        var backlinks = (await _http.GetFromJsonAsync<List<Backlink>>("/api/brain/notes/Plans/checkout.md/backlinks", Json))!;

        Assert.Equal(["Decisions/2026-10-01-old-totals.md", "Decisions/2026-11-20-cart-totals.md"], backlinks.Select(b => b.Path));
        Assert.Equal("Cart totals", backlinks[1].Title);
        using var missing = await _http.GetAsync("/api/brain/notes/Plans/nothing/backlinks");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Why_answers_with_the_accepted_decisions_covering_a_file()
    {
        var why = (await _http.GetFromJsonAsync<List<Reason>>("/api/brain/why?path=src/web/cart.ts&office=Allsafe", Json))!;

        var only = Assert.Single(why);
        Assert.Equal(("Decisions/2026-11-20-cart-totals.md", "task 01a1"), (only.Path, only.From));
        Assert.Equal(["src/web/**"], only.Covering);
        Assert.Empty((await _http.GetFromJsonAsync<List<Reason>>("/api/brain/why?path=src/api/Program.cs", Json))!);
        Assert.Empty((await _http.GetFromJsonAsync<List<Reason>>("/api/brain/why?path=src/web/cart.ts&office=E%20Corp", Json))!);
        File.WriteAllText(Path.Combine(_app.BrainFolder, "Decisions", "2026-11-21-reviews.md"), "---\nkind: decision\nstatus: accepted\nrooms: [Web]\n---\n# Reviews\n\nTwo reviewers.\n");
        var general = (await _http.GetFromJsonAsync<List<Reason>>("/api/brain/why?path=src/web/cart.ts&office=Allsafe&room=Web", Json))!;
        Assert.Equal([("Decisions/2026-11-20-cart-totals.md", false), ("Decisions/2026-11-21-reviews.md", true)], general.Select(r => (r.Path, r.General)));
        Assert.Empty(general[1].Covering);
        Assert.Single((await _http.GetFromJsonAsync<List<Reason>>("/api/brain/why?path=src/web/cart.ts&office=Allsafe&room=Api", Json))!);
        using var none = await _http.GetAsync("/api/brain/why");
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }
}
