using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Brain;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.Hosting;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure.Tests;

/// <summary>The vault in a throwaway folder with real git, and its index in a private in-memory database.</summary>
public sealed class BrainTests : IAsyncLifetime
{
    private static readonly string[] Folders = ["Decisions", "Plans", "Studies", "Sessions", "Digests", "Offices", "Inhabitants", "Chronicle"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-brain-{Guid.NewGuid():N}");
    private readonly string _connectionString = $"Data Source=brain-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private FileVault _vault = null!;
    private BrainIndex _index = null!;

    private string Folder => Path.Combine(_root, "brain");

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();
        await using (var db = new OpenspaceDbContext(new DbContextOptionsBuilder<OpenspaceDbContext>().UseSqlite(_connectionString).Options, NullDomainEventPublisher.Instance))
        {
            await db.Database.MigrateAsync();
        }

        _vault = new FileVault(Options.Create(new BrainOptions { Folder = Folder }));
        _index = new BrainIndex(_connectionString, _vault);
    }

    public async Task DisposeAsync()
    {
        _index.Dispose();
        _vault.Dispose();
        await _keepAlive.DisposeAsync();
        if (Directory.Exists(_root))
        {
            // Git marks objects read-only; clear that so the folder can go.
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private Task<string> Git(params string[] args) => GitCli.RunAsync(Folder, default, args);

    private async Task<int> CommitsAsync() => int.Parse((await Git("rev-list", "--count", "HEAD")).Trim(), System.Globalization.CultureInfo.InvariantCulture);

    private static Note Decision(string path, string title, string body, string status = NoteStatus.Accepted) =>
        Note.Create(path, title, body) with { Kind = "decision", Status = status, Offices = ["Allsafe"], Paths = ["src/web/**"] };

    private async Task<IReadOnlyList<IndexedNote>> FindAsync(params string[] words) => await _index.FindAsync(words, null, default);

    [Fact]
    public async Task A_new_vault_has_the_standard_structure_in_its_own_git_with_one_first_commit()
    {
        await _vault.EnsureAsync(default);

        Assert.True(File.Exists(Path.Combine(Folder, "Constitution.md")));
        Assert.True(File.Exists(Path.Combine(Folder, "Now.md")));
        Assert.All(Folders, f => Assert.True(File.Exists(Path.Combine(Folder, f, "README.md")), f));
        Assert.Equal(1, await CommitsAsync());
        Assert.Equal("Openspace", (await Git("log", "-1", "--format=%an")).Trim());
        Assert.Equal("", (await Git("remote")).Trim());
        Assert.Equal("", (await Git("status", "--porcelain")).Trim());

        // The founding rules, numbered, as the first rules of the constitution.
        var constitution = await _vault.ReadAsync("Constitution", default);
        Assert.Equal("rule", constitution!.Kind);
        Assert.Contains("\n13. One voice talks with the person: Shiori's.", constitution.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_files_are_never_overwritten_and_only_missing_parts_are_added()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "Decisions"));
        await File.WriteAllTextAsync(Path.Combine(Folder, "Constitution.md"), "# My rules\n\n1. Tea before code.\n");
        await File.WriteAllTextAsync(Path.Combine(Folder, "Decisions", "README.md"), "Mine.\n");

        await _vault.EnsureAsync(default);
        await Git("rm", "--quiet", "Chronicle/README.md");
        await Git("commit", "--quiet", "--message", "Removed a README");
        await _vault.EnsureAsync(default);
        await _vault.EnsureAsync(default);

        Assert.Equal("# My rules\n\n1. Tea before code.\n", await File.ReadAllTextAsync(Path.Combine(Folder, "Constitution.md")));
        Assert.Equal("Mine.\n", await File.ReadAllTextAsync(Path.Combine(Folder, "Decisions", "README.md")));
        Assert.True(File.Exists(Path.Combine(Folder, "Chronicle", "README.md")));

        // The first commit, the person's, one for the README put back; nothing for the run that found it all there.
        Assert.Equal(3, await CommitsAsync());
        Assert.Equal("Openspace: added the missing parts of the vault", (await Git("log", "-1", "--format=%s")).Trim());
    }

    [Fact]
    public async Task Every_write_is_one_commit_with_its_author_and_an_unchanged_note_adds_none()
    {
        await _vault.EnsureAsync(default);
        var note = Decision("Decisions/2026-11-20-merge-order", "Merge order", "# Merge order\n\nOldest approval first.\n");

        await _vault.WriteAsync(note, "Screens", default);
        await _vault.WriteAsync(note with { Body = "# Merge order\n\nOldest approval first, then size.\n" }, "Shiori", default, "made the order clearer");
        await _vault.WriteAsync(note with { Body = "# Merge order\n\nOldest approval first, then size.\n" }, "Shiori", default);

        Assert.Equal(3, await CommitsAsync());
        Assert.Equal(
            ["Shiori|Shiori: made the order clearer", "Screens|Screens: wrote Decisions/2026-11-20-merge-order.md"],
            (await Git("log", "-2", "--format=%an|%s")).Trim().Split('\n'));
        var back = await _vault.ReadAsync("Decisions/2026-11-20-merge-order.md", default);
        Assert.Equal("accepted", back!.Status);
        Assert.Equal(["src/web/**"], back.Paths);
        Assert.Contains("then size", back.Body, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(Folder, "Decisions"), "*.tmp"));
    }

    [Fact]
    public async Task A_write_commits_only_its_note_and_leaves_the_persons_other_edits_alone()
    {
        await _vault.EnsureAsync(default);
        await File.AppendAllTextAsync(Path.Combine(Folder, "Plans", "README.md"), "Half written.\n");

        await _vault.WriteAsync(Note.Create("Inhabitants/Screens/journal.md", "Journal", "Fixed the cart.\n"), "Screens", default);

        Assert.Equal("M Plans/README.md", (await Git("status", "--porcelain")).Trim());
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("Decisions/../../outside.md")]
    [InlineData(".git/hooks/pre-commit.md")]
    public async Task Notes_cannot_be_written_or_read_outside_the_vault(string path)
    {
        await _vault.EnsureAsync(default);

        await Assert.ThrowsAsync<RejectedException>(() => _vault.WriteAsync(Note.Create(path, "Escape", "x"), "Screens", default));
        await Assert.ThrowsAsync<RejectedException>(() => _vault.ReadAsync(path, default));
        Assert.False(File.Exists(Path.Combine(_root, "outside.md")));
        Assert.Equal(1, await CommitsAsync());
    }

    [Fact]
    public async Task The_index_finds_notes_by_the_start_of_their_words_and_leaves_the_readmes_out()
    {
        await _vault.EnsureAsync(default);
        await _vault.WriteAsync(Decision("Decisions/merge-order.md", "Merge order", "Oldest approval first; the café rule does not apply."), "Screens", default);
        await _vault.WriteAsync(Note.Create("Studies/queues.md", "Queues", "Merging queues compared.") with { Kind = "study" }, "Shiori", default);
        await _index.RebuildAsync(default);

        Assert.Equal(["Decisions/merge-order.md", "Studies/queues.md"], (await FindAsync("merg")).Select(n => n.Path).Order());
        Assert.Equal("Decisions/merge-order.md", Assert.Single(await FindAsync("cafe")).Path);
        Assert.Equal("Studies/queues.md", Assert.Single(await _index.FindAsync(["merg"], "Study", default)).Path);
        Assert.Empty(await FindAsync("subject"));

        var found = Assert.Single(await FindAsync("oldest"));
        Assert.Equal(["Allsafe"], found.Offices);
        Assert.Equal(NoteStatus.Accepted, found.Status);
        Assert.Contains("Oldest approval", found.Excerpt, StringComparison.Ordinal);

        // No words: every note, the constitution and Now included.
        Assert.Equal(4, (await FindAsync()).Count);
    }

    [Fact]
    public async Task An_edit_made_outside_Openspace_is_found_once_the_watcher_fires()
    {
        using var service = new BrainService(
            _vault, _index, Options.Create(new BrainOptions { Folder = Folder, WatchDelay = TimeSpan.FromMilliseconds(50) }), NullLogger<BrainService>.Instance);
        await service.StartAsync(default);
        await WaitForAsync(async () => (await FindAsync("constitution")).Count == 1);

        Directory.CreateDirectory(Path.Combine(Folder, "Offices", "Allsafe"));
        var outside = Path.Combine(Folder, "Offices", "Allsafe", "glossary.md");
        await File.WriteAllTextAsync(outside, "---\nkind: note\n---\n# Glossary\n\nA zebra crossing is where desks meet.\n");
        await WaitForAsync(async () => (await FindAsync("zebra")).Count == 1);

        File.Delete(outside);
        await WaitForAsync(async () => (await FindAsync("zebra")).Count == 0);
        await service.StopAsync(default);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < until, "The index did not catch up in time.");
            await Task.Delay(25);
        }
    }
}
