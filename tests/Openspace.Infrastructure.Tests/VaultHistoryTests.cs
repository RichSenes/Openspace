using Microsoft.Extensions.Options;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Brain;

namespace Openspace.Infrastructure.Tests;

/// <summary>A note's history read from a throwaway vault with real git.</summary>
public sealed class VaultHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-history-{Guid.NewGuid():N}");
    private readonly FileVault _vault;
    private readonly GitVaultHistory _history;

    public VaultHistoryTests()
    {
        _vault = new FileVault(Options.Create(new BrainOptions { Folder = _folder }));
        _history = new GitVaultHistory(_vault);
    }

    public void Dispose()
    {
        _vault.Dispose();
        if (Directory.Exists(_folder))
        {
            // Git marks objects read-only; clear that so the folder can go.
            foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_vault_without_git_has_no_history_yet()
    {
        Directory.CreateDirectory(_folder);

        Assert.Empty(await _history.HistoryAsync("Plans/a.md", 10, default));
        Assert.Null(await _history.VersionAsync("Plans/a.md", "abcd1234", default));
    }

    [Fact]
    public async Task Summaries_read_back_whole_and_paths_are_taken_literally()
    {
        await _vault.WriteAsync(Note.Create("Plans/ab.md", "AB", "# AB\n"), "Screens", default, "chose \"A|B\": 50% <now>, {not} later");
        await _vault.WriteAsync(Note.Create("Plans/a*.md", "Star", "# Star\n"), "Screens", default);

        var ab = Assert.Single(await _history.HistoryAsync("Plans/ab", 10, default));
        Assert.Equal(("Screens", "chose \"A|B\": 50% <now>, {not} later"), (ab.Author, ab.Summary));

        // As a pattern "a*" would also match ab.md; read literally it is only its own note.
        var star = Assert.Single(await _history.HistoryAsync("Plans/a*.md", 10, default));
        Assert.Equal("wrote Plans/a*.md", star.Summary);
    }

    [Fact]
    public void Records_that_do_not_fit_are_skipped()
    {
        var commits = GitVaultHistory.Parse(
            "0123456789abcdef0123456789abcdef01234567\u001fShiori\u001f2026-11-21T10:00:00+01:00\u001fShiori: accepted a: b\u001e\n"
            + "not-a-hash\u001fx\u001f2026-11-21T10:00:00+01:00\u001fy\u001e\n"
            + "0123456789abcdef0123456789abcdef01234567\u001fShiori\u001fyesterday\u001fz\u001e\n");

        var only = Assert.Single(commits);
        Assert.Equal(("Shiori", "accepted a: b"), (only.Author, only.Summary));
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(1)), only.Date);
    }
}
