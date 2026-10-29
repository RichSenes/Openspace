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
    public async Task A_branch_left_by_a_failed_start_is_picked_up_again()
    {
        await Git("branch", "openspace/logout-a1b2c3d4");

        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);

        Assert.Equal("openspace/logout-a1b2c3d4", (await GitCli.RunAsync(workspace.Path, default, "branch", "--show-current")).Trim());
    }

    [Fact]
    public async Task A_working_copy_in_use_is_never_shared()
    {
        await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);

        var clash = await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.PrepareAsync(Repo, "a1b2c3d4", "Login", default));

        Assert.Contains("already in use", clash.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("Add a logout button!", "add-a-logout-button")]
    [InlineData("  ¿Qué?  ", "qu")]
    [InlineData("!!!", "task")]
    public void Slugs_are_branch_safe(string title, string expected)
    {
        Assert.Equal(expected, GitWorkspaceManager.Slug(title));
    }

    [Fact]
    public async Task A_repository_with_commits_has_no_problem()
    {
        Assert.Null(await _manager.CheckRepositoryAsync(Repo, default));
    }

    [Fact]
    public async Task Missing_folders_plain_folders_and_empty_repositories_are_told_apart()
    {
        var plain = Path.Combine(_root, "plain");
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(plain);
        Directory.CreateDirectory(empty);
        await GitCli.RunAsync(empty, default, "init", "--quiet");

        Assert.Equal("folder-missing", (await _manager.CheckRepositoryAsync(Path.Combine(_root, "nowhere"), default))?.Code);
        Assert.Equal("not-a-repository", (await _manager.CheckRepositoryAsync(plain, default))?.Code);
        Assert.Equal("no-commits", (await _manager.CheckRepositoryAsync(empty, default))?.Code);
    }

    [Fact]
    public async Task Initializing_a_plain_folder_makes_it_ready_for_worktrees()
    {
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);

        await _manager.InitializeRepositoryAsync(plain, default);

        Assert.Null(await _manager.CheckRepositoryAsync(plain, default));
        var workspace = await _manager.PrepareAsync(plain, "e5f6a7b8", "First file", default);
        Assert.True(Directory.Exists(workspace.Path));
    }

    [Fact]
    public async Task Initializing_keeps_an_existing_history()
    {
        await _manager.InitializeRepositoryAsync(Repo, default);

        Assert.Equal("Initial", (await Git("log", "-1", "--format=%s")).Trim());
    }

    private async Task<string> AcceptedBranchAsync(string file = "logout.js", string content = "logout();\n")
    {
        var workspace = await _manager.PrepareAsync(Repo, "a1b2c3d4", "Logout", default);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, file), content);
        await _manager.CommitAsync(Repo, workspace.Path, "Add logout", default);
        return workspace.Branch;
    }

    [Fact]
    public async Task Branches_list_yours_and_hide_task_branches()
    {
        await AcceptedBranchAsync();
        await Git("branch", "develop");

        var list = await _manager.ListBranchesAsync(Repo, default);

        Assert.Equal("main", list.Current);
        Assert.Equal(["develop", "main"], list.Branches);
        Assert.Equal(0, list.UncommittedChanges);
    }

    [Fact]
    public async Task Branches_count_your_uncommitted_changes_but_not_new_files()
    {
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<p>work in progress</p>\n");
        await File.WriteAllTextAsync(Path.Combine(Repo, "notes.txt"), "not tracked\n");

        var list = await _manager.ListBranchesAsync(Repo, default);

        Assert.Equal(1, list.UncommittedChanges);
    }

    [Fact]
    public async Task Merging_into_the_checked_out_branch_updates_your_folder_and_deletes_the_task_branch()
    {
        var branch = await AcceptedBranchAsync();

        await _manager.MergeAsync(Repo, branch, "main", "Merge task: Logout", default);

        Assert.True(File.Exists(Path.Combine(Repo, "logout.js")));
        Assert.Equal("Merge task: Logout", (await Git("log", "-1", "--format=%s")).Trim());
        Assert.Equal(string.Empty, (await Git("branch", "--list", branch)).Trim());
    }

    [Fact]
    public async Task Merging_into_another_branch_never_moves_your_checkout()
    {
        await Git("branch", "develop");
        var branch = await AcceptedBranchAsync();

        await _manager.MergeAsync(Repo, branch, "develop", "Merge task: Logout", default);

        Assert.Equal("main", (await Git("branch", "--show-current")).Trim());
        Assert.False(File.Exists(Path.Combine(Repo, "logout.js")));
        Assert.Contains("logout.js", await Git("ls-tree", "--name-only", "develop"), StringComparison.Ordinal);
        Assert.DoesNotContain("merge-", await Git("worktree", "list"), StringComparison.Ordinal);
    }

    /// <summary>A beta branch with one commit of its own, made without leaving main.</summary>
    private async Task BetaAheadAsync(string file = "beta.txt", string content = "beta\n")
    {
        await Git("branch", "beta");
        await Git("checkout", "--quiet", "beta");
        await File.WriteAllTextAsync(Path.Combine(Repo, file), content);
        await Git("add", "--all");
        await Git("commit", "--quiet", "--message", "Beta work");
        await Git("checkout", "--quiet", "main");
    }

    [Fact]
    public async Task A_branch_behind_another_is_moved_forward_and_the_source_stays()
    {
        await BetaAheadAsync();

        var result = await _manager.MergeBranchAsync(Repo, "beta", "main", default);

        Assert.Equal(Application.Abstractions.BranchMerge.FastForward, result);
        Assert.Equal((await Git("rev-parse", "beta")).Trim(), (await Git("rev-parse", "main")).Trim());
        Assert.True(File.Exists(Path.Combine(Repo, "beta.txt")));
        Assert.Equal("beta", (await Git("branch", "--list", "beta")).Trim());
    }

    [Fact]
    public async Task Branches_that_both_moved_are_joined_by_a_merge_commit_without_moving_your_checkout()
    {
        await BetaAheadAsync();
        await File.WriteAllTextAsync(Path.Combine(Repo, "main.txt"), "main\n");
        await Git("add", "--all");
        await Git("commit", "--quiet", "--message", "Main work");
        await Git("checkout", "--quiet", "beta");

        var result = await _manager.MergeBranchAsync(Repo, "beta", "main", default);

        Assert.Equal(Application.Abstractions.BranchMerge.Merged, result);
        Assert.Equal("Merge beta into main", (await Git("log", "-1", "--format=%s", "main")).Trim());
        Assert.Equal("beta", (await Git("branch", "--show-current")).Trim());
        Assert.False(File.Exists(Path.Combine(Repo, "main.txt")));
        Assert.DoesNotContain("merge-", await Git("worktree", "list"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_already_inside_the_other_changes_nothing()
    {
        await Git("branch", "beta");
        var before = (await Git("rev-parse", "main")).Trim();

        Assert.Equal(Application.Abstractions.BranchMerge.UpToDate, await _manager.MergeBranchAsync(Repo, "beta", "main", default));
        Assert.Equal(before, (await Git("rev-parse", "main")).Trim());
    }

    [Fact]
    public async Task Branches_in_conflict_are_left_as_they_were_naming_the_files()
    {
        await BetaAheadAsync("index.html", "<header>beta</header>\n");
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<header>main</header>\n");
        await Git("commit", "--quiet", "--all", "--message", "Main header");
        var before = (await Git("rev-parse", "main")).Trim();

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() => _manager.MergeBranchAsync(Repo, "beta", "main", default));

        Assert.Equal("merge-conflict", error.Code);
        Assert.StartsWith("beta and main both changed the same lines in index.html.", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, (await Git("rev-parse", "main")).Trim());
        Assert.Equal(string.Empty, (await Git("status", "--porcelain")).Trim());
    }

    [Fact]
    public async Task A_branch_into_itself_or_into_a_missing_one_is_refused()
    {
        Assert.Equal("same-branch", (await Assert.ThrowsAsync<Application.RejectedException>(() => _manager.MergeBranchAsync(Repo, "main", "main", default))).Code);
        Assert.Equal("branch-missing", (await Assert.ThrowsAsync<Application.RejectedException>(() => _manager.MergeBranchAsync(Repo, "beta", "main", default))).Code);
    }

    [Fact]
    public async Task Uncommitted_changes_in_your_branch_stop_the_merge()
    {
        var branch = await AcceptedBranchAsync();
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<p>work in progress</p>\n");

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() =>
            _manager.MergeAsync(Repo, branch, "main", "Merge", default));

        Assert.Equal("uncommitted-changes", error.Code);
        Assert.NotEqual(string.Empty, (await Git("branch", "--list", branch)).Trim());
    }

    [Fact]
    public async Task A_conflict_is_aborted_and_leaves_everything_as_it_was()
    {
        var branch = await AcceptedBranchAsync("index.html", "<header>from the agent</header>\n");
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<header>from you</header>\n");
        await Git("commit", "--quiet", "--all", "--message", "Your change");

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() =>
            _manager.MergeAsync(Repo, branch, "main", "Merge", default));

        Assert.Equal("merge-conflict", error.Code);
        Assert.Equal(string.Empty, (await Git("status", "--porcelain")).Trim());
        Assert.Equal("<header>from you</header>\n", await File.ReadAllTextAsync(Path.Combine(Repo, "index.html")));
        Assert.NotEqual(string.Empty, (await Git("branch", "--list", branch)).Trim());
    }

    [Fact]
    public async Task A_conflict_names_its_files()
    {
        var branch = await AcceptedBranchAsync("index.html", "<header>from the agent</header>\n");
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<header>from you</header>\n");
        await Git("commit", "--quiet", "--all", "--message", "Your change");

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() =>
            _manager.MergeAsync(Repo, branch, "main", "Merge", default));

        Assert.Contains("in index.html", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catching_up_reopens_the_working_copy_with_the_conflict_marked_for_the_agent()
    {
        var branch = await AcceptedBranchAsync("index.html", "<header>from the agent</header>\n");
        await File.WriteAllTextAsync(Path.Combine(Repo, "index.html"), "<header>from you</header>\n");
        await Git("commit", "--quiet", "--all", "--message", "Your change");
        var workspace = new Application.Abstractions.Workspace(Path.Combine(_root, "worktrees", "a1b2c3d4"), branch);

        var files = await _manager.StartCatchUpAsync(Repo, workspace, "main", default);

        Assert.Equal(["index.html"], files);
        Assert.Contains("<<<<<<<", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "index.html")), StringComparison.Ordinal);
        Assert.Equal("main", (await Git("branch", "--show-current")).Trim());
        Assert.Equal("<header>from you</header>\n", await File.ReadAllTextAsync(Path.Combine(Repo, "index.html")));

        // Resolved and accepted again: the merge is committed on the task branch and goes in cleanly.
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "index.html"), "<header>from you and the agent</header>\n");
        await _manager.CommitAsync(Repo, workspace.Path, "Logout", default);
        await _manager.MergeAsync(Repo, branch, "main", "Merge task: Logout", default);
        Assert.Equal("<header>from you and the agent</header>\n", await File.ReadAllTextAsync(Path.Combine(Repo, "index.html")));
    }

    [Fact]
    public async Task A_scratch_copy_shows_a_branch_and_goes_without_moving_it()
    {
        await Git("branch", "develop");
        var before = (await Git("rev-parse", "develop")).Trim();

        var path = await _manager.OpenScratchAsync(Repo, "develop", default);
        await File.WriteAllTextAsync(Path.Combine(path, "build.log"), "done\n");
        await _manager.CloseScratchAsync(Repo, path, default);

        Assert.False(Directory.Exists(path));
        Assert.Equal(before, (await Git("rev-parse", "develop")).Trim());
        Assert.Equal("main", (await Git("branch", "--show-current")).Trim());
    }

    /// <summary>A bare repository in the test folder, standing for GitHub, with main pushed to it.</summary>
    private async Task<string> RemoteAsync()
    {
        var remote = Path.Combine(_root, "remote.git");
        await GitCli.RunAsync(_root, default, "init", "--quiet", "--bare", "--initial-branch", "main", remote);
        await Git("remote", "add", "origin", remote);
        await Git("push", "--quiet", "--set-upstream", "origin", "main");
        return remote;
    }

    [Fact]
    public async Task Branches_say_their_remote_and_how_far_ahead_they_are()
    {
        await RemoteAsync();
        await File.WriteAllTextAsync(Path.Combine(Repo, "about.html"), "<p>About</p>\n");
        await Git("add", "--all");
        await Git("commit", "--quiet", "--message", "About page");

        var list = await _manager.ListBranchesAsync(Repo, default);

        Assert.Equal(["origin"], list.Remotes);
        Assert.Equal(new Application.Abstractions.BranchTracking("main", "origin/main", 1, 0), Assert.Single(list.Tracking!));
    }

    [Fact]
    public async Task Pushing_sends_the_branch_and_never_over_newer_work_on_the_remote()
    {
        var remote = await RemoteAsync();
        await Git("checkout", "--quiet", "-b", "develop");
        await Git("checkout", "--quiet", "main");

        Assert.Equal("origin/develop", await _manager.PushAsync(Repo, "develop", default));

        // Someone else pushes to main meanwhile.
        var other = Path.Combine(_root, "other");
        await GitCli.RunAsync(_root, default, "clone", "--quiet", remote, other);
        await File.WriteAllTextAsync(Path.Combine(other, "theirs.txt"), "theirs\n");
        await GitCli.RunAsync(other, default, "add", "--all");
        await GitCli.RunAsync(other, default, "-c", "user.name=Angela Moss", "-c", "user.email=angela@example.invalid", "commit", "--quiet", "--message", "Theirs");
        await GitCli.RunAsync(other, default, "push", "--quiet");
        await File.WriteAllTextAsync(Path.Combine(Repo, "mine.txt"), "mine\n");
        await Git("add", "--all");
        await Git("commit", "--quiet", "--message", "Mine");

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() => _manager.PushAsync(Repo, "main", default));

        Assert.Equal("behind-remote", error.Code);
        Assert.Contains("never forces", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_remote_nothing_is_pushed()
    {
        var error = await Assert.ThrowsAsync<Application.RejectedException>(() => _manager.PushAsync(Repo, "main", default));

        Assert.Equal("no-remote", error.Code);
    }

    [Fact]
    public async Task A_branch_not_checked_out_is_brought_up_to_date_without_moving_your_checkout()
    {
        var remote = await RemoteAsync();
        await Git("branch", "release");
        await Git("push", "--quiet", "--set-upstream", "origin", "release");
        var other = Path.Combine(_root, "other");
        await GitCli.RunAsync(_root, default, "clone", "--quiet", "--branch", "release", remote, other);
        await File.WriteAllTextAsync(Path.Combine(other, "merged.txt"), "merged on GitHub\n");
        await GitCli.RunAsync(other, default, "add", "--all");
        await GitCli.RunAsync(other, default, "-c", "user.name=Angela Moss", "-c", "user.email=angela@example.invalid", "commit", "--quiet", "--message", "Merged");
        await GitCli.RunAsync(other, default, "push", "--quiet");

        await _manager.UpdateFromRemoteAsync(Repo, "release", default);

        Assert.Contains("merged.txt", await Git("ls-tree", "--name-only", "release"), StringComparison.Ordinal);
        Assert.Equal("main", (await Git("branch", "--show-current")).Trim());
    }
}
