using Microsoft.Extensions.Options;
using Openspace.Infrastructure.Git;

namespace Openspace.Infrastructure.Tests;

/// <summary>Runs real git in a throwaway folder.</summary>
public sealed class GitWorkspaceManagerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-git-{Guid.NewGuid():N}");
    private string Repo => Path.Combine(_root, "repo");
    private GitWorkspaceManager _manager = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Repo);
        await GitCli.RunAsync(Repo, default, "init", "--quiet", "--initial-branch", "main");
        await GitCli.RunAsync(Repo, default, "config", "user.name", "Tyrell Wellick");
        await GitCli.RunAsync(Repo, default, "config", "user.email", "tyrell@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<header><h1>E Corp</h1></header>\n");
        await GitCli.RunAsync(Repo, default, "add", "--all");
        await GitCli.RunAsync(Repo, default, "commit", "--quiet", "--message", "Initial");
        _manager = new GitWorkspaceManager(Options.Create(new WorkspaceOptions { Root = Path.Combine(_root, "worktrees") }));
    }

    public Task DisposeAsync()
    {
        // Git marks objects read-only; clear that so the folder can go.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    private Task<string> Git(params string[] args) => GitCli.RunAsync(Repo, default, args);

    [Fact]
    public async Task Preparing_creates_a_worktree_on_its_own_branch_without_touching_yours()
    {
        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Add a logout button!", default);

        Assert.Equal("openspace/add-a-logout-button-a1b2c3d4", workspace.Branch);
        Assert.True(File.Exists(Path.Combine(workspace.Path, "index.html")));
        Assert.Equal("main", (await Git("branch", "--show-current")).Trim());
    }

    [Fact]
    public async Task The_diff_shows_edits_and_new_files()
    {
        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "index.html"), "<header><h1>E Corp</h1><button>Logout</button></header>\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "logout.js"), "logout();\n");

        var diff = await _manager.DiffAsync(workspace.Path, default);

        Assert.Contains("+<header><h1>E Corp</h1><button>Logout</button></header>", diff, StringComparison.Ordinal);
        Assert.Contains("logout.js", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Committing_keeps_the_branch_and_removes_the_worktree()
    {
        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "logout.js"), "logout();\n");

        await _manager.CommitAsync(Repo, workspace.Path, "Add a logout button", default);

        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal("Add a logout button", (await Git("log", "-1", "--format=%s", workspace.Branch)).Trim());
        Assert.Equal("Initial", (await Git("log", "-1", "--format=%s", "main")).Trim());
    }

    [Fact]
    public async Task Discarding_removes_worktree_and_branch()
    {
        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);

        await _manager.DiscardAsync(Repo, workspace, default);

        Assert.False(Directory.Exists(workspace.Path));
        Assert.Equal(string.Empty, (await Git("branch", "--list", workspace.Branch)).Trim());
    }

    [Fact]
    public async Task A_folder_that_is_not_a_repository_is_reported()
    {
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.PrepareAsync(plain, "a1b2c3d4", "Logout", default));

        Assert.Contains("git worktree add", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Add a logout button!", "add-a-logout-button")]
    [InlineData("  ¿Qué?  ", "qu")]
    [InlineData("!!!", "task")]
    public void Slugs_are_branch_safe(string title, string expected)
    {
        Assert.Equal(expected, GitWorkspaceManager.Slug(title));
    }
}
