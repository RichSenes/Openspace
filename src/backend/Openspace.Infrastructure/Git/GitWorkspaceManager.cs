using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Git;

/// <summary>
/// One git worktree per task, on a branch named <c>openspace/&lt;title&gt;-&lt;key&gt;</c>.
/// Your own checkout and branch are never touched.
/// </summary>
internal sealed class GitWorkspaceManager(IOptions<WorkspaceOptions> options) : IWorkspaceManager
{
    private const string TaskBranchPrefix = "openspace/";
    private const string FallbackAuthor = "Openspace";
    private const string FallbackEmail = "openspace@localhost";

    public async Task<RepositoryProblem?> CheckRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return new RepositoryProblem("folder-missing", $"The folder '{repositoryPath}' does not exist.", false);
        }

        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--is-inside-work-tree").ConfigureAwait(false))
        {
            return new RepositoryProblem(
                "not-a-repository", $"'{repositoryPath}' is not a git repository yet. Openspace can initialize one there.", true);
        }

        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false))
        {
            return new RepositoryProblem(
                "no-commits", $"'{repositoryPath}' has no commits yet. Openspace can create an empty first commit.", true);
        }

        return null;
    }

    public async Task InitializeRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--is-inside-work-tree").ConfigureAwait(false))
        {
            await GitCli.RunAsync(repositoryPath, cancellationToken, "init", "--quiet").ConfigureAwait(false);
        }

        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false))
        {
            var identity = await IdentityArgsAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
            await GitCli.RunAsync(repositoryPath, cancellationToken, [.. identity, "commit", "--allow-empty", "--message", "Initial commit"])
                .ConfigureAwait(false);
        }
    }

    public async Task<Workspace> PrepareAsync(
        string repositoryPath, string taskKey, string title, CancellationToken cancellationToken)
    {
        var root = options.Value.Root;
        if (!Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("Workspaces:Root must be an absolute folder.");
        }

        if (!Directory.Exists(repositoryPath))
        {
            throw new InvalidOperationException($"The repository folder '{repositoryPath}' does not exist.");
        }

        Directory.CreateDirectory(root);
        var path = Path.Combine(root, taskKey);
        var branch = $"{TaskBranchPrefix}{Slug(title)}-{taskKey}";
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException($"The working copy '{path}' is already in use by another task.");
        }

        // A start that failed halfway may have left the task's branch behind: work goes on there.
        var leftOver = await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false);
        await GitCli.RunAsync(repositoryPath, cancellationToken, leftOver ? ["worktree", "add", path, branch] : ["worktree", "add", "-b", branch, path, "HEAD"])
            .ConfigureAwait(false);
        return new Workspace(path, branch);
    }

    public async Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspacePath))
        {
            return string.Empty;
        }

        // Intent-to-add makes brand new files show up in the diff without staging their content.
        await GitCli.RunAsync(workspacePath, cancellationToken, "add", "--intent-to-add", "--all").ConfigureAwait(false);
        return await GitCli.RunAsync(workspacePath, cancellationToken, "diff", "HEAD").ConfigureAwait(false);
    }

    public async Task CommitAsync(
        string repositoryPath, string workspacePath, string message, CancellationToken cancellationToken)
    {
        await GitCli.RunAsync(workspacePath, cancellationToken, "add", "--all").ConfigureAwait(false);
        var staged = await GitCli.RunAsync(workspacePath, cancellationToken, "diff", "--cached", "--name-only").ConfigureAwait(false);
        if (staged.Length > 0)
        {
            var identity = await IdentityArgsAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            await GitCli.RunAsync(workspacePath, cancellationToken, [.. identity, "commit", "--message", message]).ConfigureAwait(false);
        }

        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "remove", "--force", workspacePath).ConfigureAwait(false);
    }

    public async Task<BranchList> ListBranchesAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var current = (await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--show-current").ConfigureAwait(false)).Trim();
        var all = await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--format=%(refname:short)").ConfigureAwait(false);
        var branches = all.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => !b.StartsWith(TaskBranchPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        var changes = await GitCli.RunAsync(repositoryPath, cancellationToken, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
        var remotes = (await GitCli.RunAsync(repositoryPath, cancellationToken, "remote").ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tracking = (await TrackingAsync(repositoryPath, cancellationToken).ConfigureAwait(false))
            .Where(t => branches.Contains(t.Branch))
            .ToList();
        return new BranchList(
            current.Length > 0 ? current : null,
            branches,
            changes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
            remotes,
            tracking);
    }

    private static async Task<List<BranchTracking>> TrackingAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var lines = await GitCli.RunAsync(
            repositoryPath, cancellationToken, "for-each-ref", "--format=%(refname:short)%09%(upstream:short)%09%(upstream:track)", "refs/heads").ConfigureAwait(false);
        return [.. lines.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var parts = line.Split('\t');
            var upstream = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            var track = parts.Length > 2 ? parts[2] : string.Empty;
            return new BranchTracking(parts[0], upstream, Count(track, "ahead"), Count(track, "behind"));
        })];
    }

    /// <summary>A number from git's "[ahead 2, behind 1]".</summary>
    private static int Count(string track, string word)
    {
        var at = track.IndexOf(word + " ", StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        var digits = new string([.. track[(at + word.Length + 1)..].TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public async Task UpdateFromRemoteAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        var upstream = (await TrackingAsync(repositoryPath, cancellationToken).ConfigureAwait(false)).Find(t => t.Branch == branch)?.Upstream
            ?? throw new RejectedException("no-upstream", $"{branch} follows no remote branch.");
        var remote = upstream[..upstream.IndexOf('/', StringComparison.Ordinal)];
        var remoteBranch = upstream[(remote.Length + 1)..];
        var current = (await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--show-current").ConfigureAwait(false)).Trim();
        try
        {
            if (current == branch)
            {
                var changes = await GitCli.RunAsync(repositoryPath, cancellationToken, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
                if (changes.Trim().Length > 0)
                {
                    throw new RejectedException("uncommitted-changes", $"{branch} has uncommitted changes: pull it yourself once they are committed.");
                }

                await GitCli.RunAsync(repositoryPath, cancellationToken, "pull", "--ff-only", "--quiet", remote, remoteBranch).ConfigureAwait(false);
            }
            else
            {
                await GitCli.RunAsync(repositoryPath, cancellationToken, "fetch", "--quiet", remote, $"{remoteBranch}:{branch}").ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new RejectedException("update-failed", $"{branch} could not be brought up to date with {upstream} without merging: {ex.Message}");
        }
    }

    public async Task<string> PushAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false))
        {
            throw new RejectedException("branch-missing", $"There is no branch named {branch}.");
        }

        var upstream = (await TrackingAsync(repositoryPath, cancellationToken).ConfigureAwait(false)).Find(t => t.Branch == branch)?.Upstream;
        var remotes = (await GitCli.RunAsync(repositoryPath, cancellationToken, "remote").ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var remote = upstream is not null ? upstream[..upstream.IndexOf('/', StringComparison.Ordinal)] : remotes.Contains("origin") ? "origin" : null;
        if (remote is null)
        {
            throw new RejectedException("no-remote", "This repository has no remote to push to: it lives on this machine only.");
        }

        try
        {
            await GitCli.RunAsync(repositoryPath, cancellationToken, "fetch", "--quiet", remote).ConfigureAwait(false);
            if (upstream is not null)
            {
                var behind = int.Parse(
                    (await GitCli.RunAsync(repositoryPath, cancellationToken, "rev-list", "--count", $"{branch}..{upstream}").ConfigureAwait(false)).Trim(),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (behind > 0)
                {
                    throw new RejectedException(
                        "behind-remote",
                        $"{upstream} has {behind} commit{(behind == 1 ? string.Empty : "s")} that {branch} lacks: pull them first (git pull), then push. Openspace never forces a push.");
                }

                await GitCli.RunAsync(repositoryPath, cancellationToken, "push", remote, $"{branch}:{upstream[(remote.Length + 1)..]}").ConfigureAwait(false);
                return upstream;
            }

            await GitCli.RunAsync(repositoryPath, cancellationToken, "push", "--set-upstream", remote, branch).ConfigureAwait(false);
            return $"{remote}/{branch}";
        }
        catch (InvalidOperationException ex)
        {
            throw new RejectedException("push-failed", $"The push to {remote} did not go through: {ex.Message}");
        }
    }

    public async Task MergeAsync(
        string repositoryPath, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken)
    {
        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{sourceBranch}").ConfigureAwait(false))
        {
            throw new RejectedException("task-branch-missing", $"The task branch {sourceBranch} no longer exists.");
        }

        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{targetBranch}").ConfigureAwait(false))
        {
            throw new RejectedException("branch-missing", $"There is no branch named {targetBranch}.");
        }

        await InCheckoutAsync(
                repositoryPath,
                targetBranch,
                path => MergeInAsync(
                    path,
                    sourceBranch,
                    message,
                    fastForward: false,
                    where => $"The work conflicts with {targetBranch}{where}. Nothing was changed: let its agent bring it up to date, or merge it by hand.",
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        // git branch -d only checks the checked-out branch; check the actual target instead.
        await GitCli.RunAsync(repositoryPath, cancellationToken, "merge-base", "--is-ancestor", sourceBranch, targetBranch).ConfigureAwait(false);
        await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "-D", sourceBranch).ConfigureAwait(false);
    }

    public async Task<BranchMerge> MergeBranchAsync(
        string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken)
    {
        if (sourceBranch == targetBranch)
        {
            throw new RejectedException("same-branch", "Choose two different branches.");
        }

        foreach (var branch in new[] { sourceBranch, targetBranch })
        {
            if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false))
            {
                throw new RejectedException("branch-missing", $"There is no branch named {branch}.");
            }
        }

        if (await SucceedsAsync(repositoryPath, cancellationToken, "merge-base", "--is-ancestor", sourceBranch, targetBranch).ConfigureAwait(false))
        {
            return BranchMerge.UpToDate;
        }

        var fastForward = await SucceedsAsync(repositoryPath, cancellationToken, "merge-base", "--is-ancestor", targetBranch, sourceBranch).ConfigureAwait(false);
        await InCheckoutAsync(
                repositoryPath,
                targetBranch,
                path => MergeInAsync(
                    path,
                    sourceBranch,
                    $"Merge {sourceBranch} into {targetBranch}",
                    fastForward: true,
                    where => $"{sourceBranch} and {targetBranch} both changed the same lines{where}. Nothing was changed: merge them by hand, or give a desk the task.",
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        return fastForward ? BranchMerge.FastForward : BranchMerge.Merged;
    }

    /// <summary>
    /// Runs a merge where <paramref name="targetBranch"/> lives: your own checkout only when nothing
    /// of yours could be mixed in, any other branch in a throwaway worktree so your checkout never moves.
    /// </summary>
    private async Task InCheckoutAsync(string repositoryPath, string targetBranch, Func<string, Task> merge, CancellationToken cancellationToken)
    {
        var current = (await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--show-current").ConfigureAwait(false)).Trim();
        if (current == targetBranch)
        {
            var changes = await GitCli.RunAsync(repositoryPath, cancellationToken, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
            if (changes.Trim().Length > 0)
            {
                throw new RejectedException(
                    "uncommitted-changes",
                    $"'{repositoryPath}' has uncommitted changes on {targetBranch}. Commit or stash them, then merge again.");
            }

            await merge(repositoryPath).ConfigureAwait(false);
            return;
        }

        var scratch = Path.Combine(options.Value.Root, $"merge-{Guid.NewGuid():N}");
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "add", scratch, targetBranch).ConfigureAwait(false);
        try
        {
            await merge(scratch).ConfigureAwait(false);
        }
        finally
        {
            await GitCli.RunAsync(repositoryPath, CancellationToken.None, "worktree", "remove", "--force", scratch).ConfigureAwait(false);
        }
    }

    /// <param name="fastForward">Let git only move the branch when it can; a task's work always gets its merge commit.</param>
    /// <param name="conflict">The words for a conflict, given " in the files" (or nothing).</param>
    private static async Task MergeInAsync(
        string path, string sourceBranch, string message, bool fastForward, Func<string, string> conflict, CancellationToken cancellationToken)
    {
        var identity = await IdentityArgsAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            await GitCli.RunAsync(path, cancellationToken, [.. identity, "merge", fastForward ? "--ff" : "--no-ff", "--no-edit", "--message", message, sourceBranch])
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            var files = await ConflictedFilesAsync(path, CancellationToken.None).ConfigureAwait(false);
            await SucceedsAsync(path, CancellationToken.None, "merge", "--abort").ConfigureAwait(false);
            var where = files.Count > 0 ? $" in {string.Join(", ", files)}" : string.Empty;
            throw new RejectedException("merge-conflict", conflict(where));
        }
    }

    public async Task<IReadOnlyList<string>> StartCatchUpAsync(
        string repositoryPath, Workspace workspace, string targetBranch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        foreach (var branch in new[] { workspace.Branch, targetBranch })
        {
            if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false))
            {
                throw new RejectedException("branch-missing", $"There is no branch named {branch}.");
            }
        }

        if (Directory.Exists(workspace.Path))
        {
            throw new InvalidOperationException($"The working copy '{workspace.Path}' is already in use.");
        }

        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "prune").ConfigureAwait(false);
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "add", workspace.Path, workspace.Branch).ConfigureAwait(false);
        var identity = await IdentityArgsAsync(workspace.Path, cancellationToken).ConfigureAwait(false);
        try
        {
            await GitCli.RunAsync(workspace.Path, cancellationToken, [.. identity, "merge", "--no-ff", "--no-commit", targetBranch])
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The conflicts stay in the working copy, marked in the files, for the agent to resolve.
        }

        return await ConflictedFilesAsync(workspace.Path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ConflictedFilesAsync(string path, CancellationToken cancellationToken) =>
        (await GitCli.RunAsync(path, cancellationToken, "diff", "--name-only", "--diff-filter=U").ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<string> OpenScratchAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").ConfigureAwait(false))
        {
            throw new RejectedException("branch-missing", $"There is no branch named {branch}.");
        }

        var path = Path.Combine(options.Value.Root, $"check-{Guid.NewGuid():N}");
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "add", "--detach", path, branch).ConfigureAwait(false);
        return path;
    }

    public async Task CloseScratchAsync(string repositoryPath, string path, CancellationToken cancellationToken)
    {
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "remove", "--force", path).ConfigureAwait(false);
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "prune").ConfigureAwait(false);
    }

    public async Task DiscardAsync(string repositoryPath, Workspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (Directory.Exists(workspace.Path))
        {
            await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "remove", "--force", workspace.Path).ConfigureAwait(false);
        }

        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "prune").ConfigureAwait(false);
        var branches = await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--list", workspace.Branch).ConfigureAwait(false);
        if (branches.Trim().Length > 0)
        {
            await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "-D", workspace.Branch).ConfigureAwait(false);
        }
    }

    /// <summary>Uses the repository's own identity, or Openspace's when git has none configured.</summary>
    private static async Task<string[]> IdentityArgsAsync(string path, CancellationToken cancellationToken) =>
        await SucceedsAsync(path, cancellationToken, "config", "user.email").ConfigureAwait(false)
            ? []
            : ["-c", $"user.name={FallbackAuthor}", "-c", $"user.email={FallbackEmail}"];

    private static async Task<bool> SucceedsAsync(string path, CancellationToken cancellationToken, params string[] args)
    {
        try
        {
            await GitCli.RunAsync(path, cancellationToken, args).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A branch-safe version of a title: "Add a logout button!" becomes "add-a-logout-button".</summary>
    internal static string Slug(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }

            if (slug.Length >= 40)
            {
                break;
            }
        }

        var result = slug.ToString().Trim('-');
        return result.Length > 0 ? result : "task";
    }
}
