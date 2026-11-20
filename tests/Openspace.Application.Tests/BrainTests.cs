using Openspace.Application.Brain;

namespace Openspace.Application.Tests;

public class VaultFormatTests
{
    [Fact]
    public void A_note_written_and_read_back_is_the_same_note()
    {
        var note = new Note(
            "Decisions/2026-11-20-merge-order.md", "decision", "accepted", "Merge in the order tasks were approved",
            ["Allsafe"], ["Web"], ["src/web/**", "docs/*.md"], new DateOnly(2027, 2, 20),
            "[[2026-09-01-merge-order]]", "task 01a1: \"merge\" order", "Screens",
            "# Merge order\n\nOldest approval first. See [[Plans/release|the plan]] and [[Studies/merge-queues#Costs]].\n",
            [new("tags", " [brain, merges]")]);

        var text = VaultFormat.Write(note);
        var back = VaultFormat.Parse(note.Path, text);

        Assert.StartsWith("---\nkind: decision\nstatus: accepted\n", text, StringComparison.Ordinal);
        Assert.Contains("paths: [\"src/web/**\", \"docs/*.md\"]", text, StringComparison.Ordinal);
        Assert.Contains("supersedes: \"[[2026-09-01-merge-order]]\"", text, StringComparison.Ordinal);
        Assert.Equal(note.Kind, back.Kind);
        Assert.Equal(note.Status, back.Status);
        Assert.Equal(note.Title, back.Title);
        Assert.Equal(note.Offices, back.Offices);
        Assert.Equal(note.Rooms, back.Rooms);
        Assert.Equal(note.Paths, back.Paths);
        Assert.Equal(note.Review, back.Review);
        Assert.Equal(note.Supersedes, back.Supersedes);
        Assert.Equal(note.From, back.From);
        Assert.Equal(note.Author, back.Author);
        Assert.Equal(note.Body, back.Body);
        Assert.Equal(note.Extra, back.Extra);
        Assert.Equal(text, VaultFormat.Write(back));
        Assert.Equal(["2026-09-01-merge-order", "Plans/release", "Studies/merge-queues"], back.Links);
    }

    [Fact]
    public void Front_matter_as_Obsidian_writes_it_is_understood()
    {
        var text = """
            ---
            kind: decision
            status: proposed # until the person says yes
            offices:
              - Allsafe
              - 'E Corp'
            rooms: Web
            supersedes: [[2026-09-01-merge-order]]
            review: someday
            ---
            Body without a heading.
            """;

        var note = VaultFormat.Parse("Decisions/order.md", text);

        Assert.Equal("proposed", note.Status);
        Assert.Equal(["Allsafe", "E Corp"], note.Offices);
        Assert.Equal(["Web"], note.Rooms);
        Assert.Equal("[[2026-09-01-merge-order]]", note.Supersedes);
        Assert.Equal("order", note.Title);
        Assert.Null(note.Review);

        // A date Openspace cannot read is kept, not lost on the next write.
        Assert.Contains("review: someday", VaultFormat.Write(note), StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_without_front_matter_stays_plain_and_takes_its_title_from_the_heading()
    {
        var note = VaultFormat.Parse("Studies/queues.md", "# Merge queues compared\n\nText.\n");

        Assert.Null(note.Kind);
        Assert.Equal("Merge queues compared", note.Title);
        Assert.Equal("# Merge queues compared\n\nText.\n", VaultFormat.Write(note));
    }
}

public class VaultPathTests
{
    [Theory]
    [InlineData("Decisions/order", "Decisions/order.md")]
    [InlineData("Decisions\\order.md", "Decisions/order.md")]
    [InlineData("Plans//release.md", "Plans/release.md")]
    public void Paths_are_written_one_way(string asked, string expected) => Assert.Equal(expected, VaultPath.Normalize(asked));

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("Decisions/../../outside.md")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/notes.md")]
    [InlineData(".git/config")]
    [InlineData("Decisions/./order.md")]
    [InlineData("")]
    public void Paths_that_leave_the_vault_or_enter_its_hidden_folders_are_refused(string asked)
    {
        var refused = Assert.Throws<RejectedException>(() => VaultPath.Normalize(asked));
        Assert.Equal(VaultPath.RejectedCode, refused.Code);
    }
}

public class BrainSearchTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static IndexedNote Note(string path, string title, string? status = null, string[]? offices = null, string[]? rooms = null, string[]? paths = null, double rank = -1, int daysAgo = 0) =>
        new(path, title, "decision", status, offices ?? [], rooms ?? [], paths ?? [], null, Day.AddDays(-daysAgo), rank, "");

    private sealed class FakeIndex(params IndexedNote[] notes) : IBrainIndex
    {
        public IReadOnlyList<string>? Words { get; private set; }

        public string? Kind { get; private set; }

        public Task RebuildAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<IndexedNote>> FindAsync(IReadOnlyList<string> words, string? kind, CancellationToken cancellationToken)
        {
            (Words, Kind) = (words, kind);
            return Task.FromResult<IReadOnlyList<IndexedNote>>(notes);
        }
    }

    private static async Task<List<string>> Ranked(BrainQuery query, params IndexedNote[] notes) =>
        [.. (await new BrainSearch(new FakeIndex(notes)).SearchAsync(query, default)).Select(h => h.Path)];

    [Fact]
    public async Task Superseded_notes_come_last_whatever_else_they_match()
    {
        var ranked = await Ranked(
            new BrainQuery("merge", Office: "Allsafe"),
            Note("old.md", "Merge order", NoteStatus.Superseded, offices: ["Allsafe"], rank: -9),
            Note("new.md", "Notes about releases", NoteStatus.Accepted),
            Note("idea.md", "Releases again", "draft"));

        Assert.Equal("old.md", ranked[^1]);
    }

    [Fact]
    public async Task Status_orders_accepted_before_proposed_before_the_rest()
    {
        var ranked = await Ranked(
            new BrainQuery("merge"),
            Note("draft.md", "Merge", "draft"),
            Note("proposed.md", "Merge", NoteStatus.Proposed),
            Note("rejected.md", "Merge", NoteStatus.Rejected),
            Note("accepted.md", "Merge", NoteStatus.Accepted));

        Assert.Equal(["accepted.md", "proposed.md"], ranked[..2]);
    }

    [Fact]
    public async Task Notes_about_the_office_room_and_file_asked_come_before_others()
    {
        var ranked = await Ranked(
            new BrainQuery("merge", Office: "Allsafe", Room: "Web", Path: "src/web/cart.ts"),
            Note("elsewhere.md", "Merge", NoteStatus.Accepted, offices: ["E Corp"], rank: -9),
            Note("everywhere.md", "Merge", NoteStatus.Accepted, rank: -9),
            Note("here.md", "Merge", NoteStatus.Proposed, offices: ["allsafe"], rooms: ["Web"], paths: ["src/web/**"]));

        Assert.Equal(["here.md", "everywhere.md", "elsewhere.md"], ranked);
    }

    [Fact]
    public async Task Words_in_the_title_beat_words_in_the_body_then_the_text_score_then_recency()
    {
        var ranked = await Ranked(
            new BrainQuery("merge order"),
            Note("body.md", "Releases", NoteStatus.Accepted, rank: -9),
            Note("older.md", "Merge order", NoteStatus.Accepted, rank: -2, daysAgo: 30),
            Note("newer.md", "Merge order", NoteStatus.Accepted, rank: -2),
            Note("better.md", "Merge order", NoteStatus.Accepted, rank: -5, daysAgo: 60));

        Assert.Equal(["better.md", "newer.md", "older.md", "body.md"], ranked);
    }

    [Fact]
    public async Task Words_and_kind_go_to_the_index_and_punctuation_alone_finds_nothing()
    {
        var index = new FakeIndex(Note("a.md", "A"));
        var search = new BrainSearch(index);

        await search.SearchAsync(new BrainQuery("Merge, ORDER! merge", Kind: " decision "), default);
        Assert.Equal(["merge", "order"], index.Words);
        Assert.Equal("decision", index.Kind);

        Assert.Empty(await search.SearchAsync(new BrainQuery("?!"), default));
    }

    [Theory]
    [InlineData("src/web/cart.ts", "src/web/**", true)]
    [InlineData("src/web/deep/cart.ts", "src/web/*.ts", false)]
    [InlineData("src/web/cart.ts", "src/web/*.ts", true)]
    [InlineData("src/web/cart.ts", "src/web", true)]
    [InlineData("src/web", "src/web/cart.ts", true)]
    [InlineData("src/api/cart.ts", "src/web/**", false)]
    [InlineData("src/webshop/x.ts", "src/web", false)]
    [InlineData("README.md", "**/*.md", true)]
    public void Path_globs_cover_files_and_folders(string asked, string glob, bool covered) =>
        Assert.Equal(covered, BrainSearch.Covers(asked, glob));
}
