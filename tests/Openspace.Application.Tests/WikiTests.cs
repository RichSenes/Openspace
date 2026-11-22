using Openspace.Application.Brain;

namespace Openspace.Application.Tests;

public class WikiLinksTests
{
    private static readonly string[] Paths =
    [
        "Constitution.md",
        "Decisions/2026-11-20-merge-order.md",
        "Plans/release.md",
        "Offices/Allsafe/overview.md",
        "Offices/E Corp/overview.md",
    ];

    [Theory]
    [InlineData("Plans/release", "Plans/release.md")]
    [InlineData("Plans/release.md", "Plans/release.md")]
    [InlineData("release", "Plans/release.md")]
    [InlineData("RELEASE", "Plans/release.md")]
    [InlineData("2026-11-20-merge-order", "Decisions/2026-11-20-merge-order.md")]
    [InlineData("Constitution", "Constitution.md")]
    [InlineData("Allsafe/overview", "Offices/Allsafe/overview.md")]
    [InlineData("E Corp/overview.md", "Offices/E Corp/overview.md")]
    public void A_link_finds_its_note_by_path_end_or_name(string link, string expected) =>
        Assert.Equal(expected, new WikiLinks(Paths).Resolve(link, "Studies/anything.md"));

    [Theory]
    [InlineData("nowhere")]
    [InlineData("Studies/release")]
    [InlineData("lease")]
    public void A_link_to_no_note_resolves_to_nothing(string link) =>
        Assert.Null(new WikiLinks(Paths).Resolve(link, "Studies/anything.md"));

    [Fact]
    public void When_two_notes_share_a_name_the_one_beside_the_link_wins_then_the_shortest()
    {
        var links = new WikiLinks(Paths);

        // Beside wins even over a shorter path; from elsewhere the shorter path wins.
        Assert.Equal("Offices/Allsafe/overview.md", links.Resolve("overview", "Offices/Allsafe/glossary.md"));
        Assert.Equal("Offices/E Corp/overview.md", links.Resolve("overview", "Studies/anything.md"));
    }

    [Fact]
    public void Backlinks_come_from_labelled_bare_and_heading_links_but_not_from_the_note_itself()
    {
        static Note At(string path, string body) => VaultFormat.Parse(path, body);
        var notes = new List<Note>
        {
            At("Plans/release.md", "# Release\n\nSee [[release]] for itself."),
            At("Decisions/2026-11-20-merge-order.md", "# Merge order\n\nPart of [[Plans/release|the release plan]]."),
            At("Studies/queues.md", "# Queues\n\nCompare [[release#Costs]]."),
            At("Studies/other.md", "# Other\n\nAbout [[Plans/release-notes]] and `release`."),
            At("Offices/Allsafe/overview.md", "---\nsupersedes: \"[[release.md]]\"\n---\n# Overview\n"),
        };

        var backlinks = WikiLinks.Backlinks("Plans/release.md", notes);

        Assert.Equal(["Decisions/2026-11-20-merge-order.md", "Offices/Allsafe/overview.md", "Studies/queues.md"], backlinks.Select(n => n.Path));
    }
}

public class WikiWhyTests
{
    private static Note Decision(string path, string status, string paths, string offices = "[]", string? from = null) =>
        VaultFormat.Parse(path, $"---\nkind: decision\nstatus: {status}\noffices: {offices}\npaths: {paths}\n{(from is null ? "" : $"from: \"{from}\"\n")}---\n# {path}\n");

    private static readonly Note[] Notes =
    [
        Decision("Decisions/2026-09-01-cart-totals.md", "accepted", "[\"src/web/**\"]", from: "task 01a1"),
        Decision("Decisions/2026-11-20-merge-order.md", "accepted", "[\"src/web/cart.ts\", \"docs/**\"]", "[Allsafe]", "routine weekly"),
        Decision("Decisions/2026-10-01-old-cart.md", "superseded", "[\"src/web/**\"]"),
        Decision("Decisions/2026-10-02-proposed.md", "proposed", "[\"src/web/**\"]"),
        Decision("Decisions/2026-10-03-shop.md", "accepted", "[\"src/web/**\"]", "[E Corp]"),
        Decision("Decisions/2026-10-04-everywhere.md", "accepted", "[]"),
        Decision("Decisions/2026-10-05-api.md", "accepted", "[\"src/api/*.cs\"]"),
        VaultFormat.Parse("Plans/web.md", "---\nkind: plan\nstatus: accepted\npaths: [\"src/web/**\"]\n---\n# Web\n"),
    ];

    [Fact]
    public void Accepted_decisions_whose_paths_cover_the_file_come_newest_first_with_where_they_came_from()
    {
        var why = WikiWhy.For("src/web/cart.ts", null, Notes);

        Assert.Equal(
            ["Decisions/2026-11-20-merge-order.md", "Decisions/2026-10-03-shop.md", "Decisions/2026-09-01-cart-totals.md"],
            why.Select(r => r.Path));
        Assert.Equal(["src/web/cart.ts"], why[0].Covering);
        Assert.Equal(("routine weekly", new DateOnly(2026, 11, 20)), (why[0].From, why[0].Decided));
        Assert.Equal("task 01a1", why[2].From);
    }

    [Fact]
    public void An_office_keeps_its_own_decisions_and_those_about_every_office()
    {
        Assert.Equal(
            ["Decisions/2026-11-20-merge-order.md", "Decisions/2026-09-01-cart-totals.md", "Decisions/2026-10-04-everywhere.md"],
            WikiWhy.For("src/web/cart.ts", "allsafe", Notes).Select(r => r.Path));
    }

    [Fact]
    public void Decisions_without_paths_follow_the_path_matches_as_general_when_the_office_or_room_fits()
    {
        Note[] notes =
        [
            .. Notes,
            VaultFormat.Parse("Decisions/2026-11-01-web-reviews.md", "---\nkind: decision\nstatus: accepted\noffices: [E Corp]\nrooms: [Web]\n---\n# Web reviews\n"),
            VaultFormat.Parse("Decisions/2026-11-02-api-reviews.md", "---\nkind: decision\nstatus: accepted\nrooms: [Api]\n---\n# Api reviews\n"),
            VaultFormat.Parse("Decisions/2026-11-03-allsafe-only.md", "---\nkind: decision\nstatus: accepted\noffices: [Allsafe]\n---\n# Allsafe only\n"),
            VaultFormat.Parse("Decisions/2026-11-04-old-general.md", "---\nkind: decision\nstatus: superseded\n---\n# Old\n"),
        ];

        var why = WikiWhy.For("src/web/cart.ts", "E Corp", notes, "Web");

        Assert.Equal(
            ["Decisions/2026-10-03-shop.md", "Decisions/2026-09-01-cart-totals.md", "Decisions/2026-11-01-web-reviews.md", "Decisions/2026-10-04-everywhere.md"],
            why.Select(r => r.Path));
        Assert.All(why.Take(2), r => Assert.False(r.General));
        Assert.All(why.Skip(2), r => Assert.True(r.General));
        Assert.All(why.Skip(2), r => Assert.Empty(r.Covering));
        Assert.DoesNotContain(WikiWhy.For("src/web/cart.ts", null, notes), r => r.General);
        Assert.Equal(
            ["Decisions/2026-11-03-allsafe-only.md", "Decisions/2026-11-02-api-reviews.md", "Decisions/2026-10-04-everywhere.md"],
            WikiWhy.For("README.md", null, notes, "api").Select(r => r.Path));
    }

    [Theory]
    [InlineData("src/api/Program.cs", "Decisions/2026-10-05-api.md")]
    [InlineData("src/api/sub/Program.cs", null)]
    [InlineData("./docs/brain.md", "Decisions/2026-11-20-merge-order.md")]
    [InlineData("README.md", null)]
    public void Only_globs_that_cover_the_file_count(string file, string? expected) =>
        Assert.Equal(expected, WikiWhy.For(file, null, Notes).Select(r => r.Path).FirstOrDefault());
}

public class WikiTests
{
    private sealed class NoHistory : IVaultHistory
    {
        public Task<IReadOnlyList<NoteCommit>> HistoryAsync(string path, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NoteCommit>>([]);

        public Task<NoteVersion?> VersionAsync(string path, string commit, CancellationToken cancellationToken) =>
            Task.FromResult<NoteVersion?>(null);
    }

    [Fact]
    public async Task The_tree_puts_folders_first_then_notes_by_name()
    {
        var vault = new InMemoryVault();
        vault.Put("Now.md", "# Now\n");
        vault.Put("Constitution.md", "---\nkind: rule\n---\n# Constitution\n");
        vault.Put("Offices/Allsafe/overview.md", "# Allsafe\n");
        vault.Put("Decisions/b.md", "---\nkind: decision\nstatus: accepted\n---\n# B\n");
        vault.Put("Decisions/a.md", "# A\n");

        var root = await new Wiki(vault, new NoHistory()).TreeAsync(CancellationToken.None);

        Assert.Equal(["Decisions", "Offices"], root.Folders.Select(f => f.Name));
        Assert.Equal(["Constitution.md", "Now.md"], root.Notes.Select(n => n.Name));
        Assert.Equal(["Decisions/a.md", "Decisions/b.md"], root.Folders[0].Notes.Select(n => n.Path));
        Assert.Equal(("B", "decision", "accepted"), (root.Folders[0].Notes[1].Title, root.Folders[0].Notes[1].Kind, root.Folders[0].Notes[1].Status));
        var allsafe = Assert.Single(root.Folders[1].Folders);
        Assert.Equal(("Allsafe", "Offices/Allsafe"), (allsafe.Name, allsafe.Path));
    }

    [Fact]
    public async Task The_person_writes_any_note_as_themselves_but_never_Now()
    {
        var vault = new InMemoryVault();
        vault.Put("Constitution.md", "# Constitution\n\n1. Tests first.\n");
        var wiki = new Wiki(vault, new NoHistory());

        var edited = await wiki.WriteAsync("Constitution", "---\nkind: rule\n---\n# Constitution\n\n1. Tests first.\n2. Small commits.\n", "elliot", null, CancellationToken.None);
        var created = await wiki.WriteAsync("Plans/new.md", "# New\n", "elliot", "started a plan", CancellationToken.None);

        Assert.False(edited.Created);
        Assert.Contains("2. Small commits.", edited.Note.Body, StringComparison.Ordinal);
        Assert.True(created.Created);
        Assert.Equal(
            [("Constitution.md", "elliot", "edited Constitution.md"), ("Plans/new.md", "elliot", "started a plan")],
            vault.Writes.Select(w => (w.Path, w.Author, w.Summary)));

        var now = await Assert.ThrowsAsync<RejectedException>(() => wiki.WriteAsync("now.md", "# Now\n", "elliot", null, CancellationToken.None));
        Assert.Equal(Wiki.NowRefusedCode, now.Code);
        var outside = await Assert.ThrowsAsync<RejectedException>(() => wiki.WriteAsync("../outside.md", "# Out\n", "elliot", null, CancellationToken.None));
        Assert.Equal(VaultPath.RejectedCode, outside.Code);
        Assert.Equal(2, vault.Writes.Count);
    }

    [Fact]
    public async Task Backlinks_of_a_missing_note_are_not_found()
    {
        var vault = new InMemoryVault();
        vault.Put("Plans/a.md", "# A\n\n[[b]]");

        Assert.Null(await new Wiki(vault, new NoHistory()).BacklinksAsync("Plans/b", CancellationToken.None));
    }

    [Fact]
    public async Task The_graph_lists_every_note_and_each_resolved_link_once()
    {
        var vault = new InMemoryVault();
        vault.Put("Plans/release.md", "---\nkind: plan\nauthor: darlene\noffices: [Allsafe]\n---\n# Release\n\n[[release]] [[Decisions/a]] and [[a|again]], [[nowhere]].");
        vault.Put("Decisions/a.md", "---\nkind: decision\nstatus: accepted\n---\n# A\n\nPart of [[release#Costs]].");
        vault.Put("Studies/alone.md", "# Alone\n");

        var graph = await new Wiki(vault, new NoHistory()).GraphAsync(CancellationToken.None);

        Assert.Equal(["Decisions/a.md", "Plans/release.md", "Studies/alone.md"], graph.Notes.Select(n => n.Path));
        Assert.Equal(
            [new WikiGraphLink("Decisions/a.md", "Plans/release.md"), new WikiGraphLink("Plans/release.md", "Decisions/a.md")],
            graph.Links);
        var release = graph.Notes[1];
        Assert.Equal(("Release", "plan", "darlene", "Allsafe"), (release.Title, release.Kind, release.Author, Assert.Single(release.Offices)));
    }

    [Fact]
    public async Task The_text_of_a_note_keeps_its_front_matter_and_fields_Openspace_does_not_know()
    {
        var vault = new InMemoryVault();
        vault.Put("Decisions/a.md", "---\nkind: decision\nstatus: accepted\nmood: calm\n---\n# A\n\nBody.\n");
        var wiki = new Wiki(vault, new NoHistory());

        var text = await wiki.TextAsync("Decisions/a", CancellationToken.None);

        Assert.NotNull(text);
        Assert.StartsWith("---\nkind: decision\nstatus: accepted\n", text, StringComparison.Ordinal);
        Assert.Contains("mood: calm", text, StringComparison.Ordinal);
        Assert.EndsWith("# A\n\nBody.\n", text, StringComparison.Ordinal);
        Assert.Null(await wiki.TextAsync("Decisions/missing", CancellationToken.None));
    }
}
