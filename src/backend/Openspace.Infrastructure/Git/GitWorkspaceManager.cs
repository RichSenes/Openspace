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
        return new BranchList(current.Length > 0 ? current : null, branches);
    }

    public async Task MergeAsync(
        string repositoryPath, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken)
    {
        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{sourceBranch}").ConfigureAwait(false))
        {
            throw new RejectedException("branch-missing", $"The task branch {sourceBranch} no longer exists.");
        }

        if (!await SucceedsAsync(repositoryPath, cancellationToken, "rev-parse", "--verify", "--quiet", $"refs/heads/{targetBranch}").ConfigureAwait(false))
        {
            throw new RejectedException("branch-missing", $"There is no branch named {targetBranch}.");
        }

        var current = (await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "--show-current").ConfigureAwait(false)).Trim();
        if (current == targetBranch)
        {
            // Your own checkout: only merge into it when nothing of yours could be mixed in.
            var changes = await GitCli.RunAsync(repositoryPath, cancellationToken, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false);
            if (changes.Trim().Length > 0)
            {
                throw new RejectedException(
                    "uncommitted-changes",
                    $"'{repositoryPath}' has uncommitted changes on {targetBranch}. Commit or stash them, then merge again.");
            }

            await MergeInAsync(repositoryPath, sourceBranch, targetBranch, message, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Another branch: merge it in a throwaway worktree, so your checkout never moves.
            var scratch = Path.Combine(options.Value.Root, $"merge-{Guid.NewGuid():N}");
            await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "add", scratch, targetBranch).ConfigureAwait(false);
            try
            {
                await MergeInAsync(scratch, sourceBranch, targetBranch, message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await GitCli.RunAsync(repositoryPath, CancellationToken.None, "worktree", "remove", "--force", scratch).ConfigureAwait(false);
            }
        }

        // git branch -d only checks the checked-out branch; check the actual target instead.
        await GitCli.RunAsync(repositoryPath, cancellationToken, "merge-base", "--is-ancestor", sourceBranch, targetBranch).ConfigureAwait(false);
        await GitCli.RunAsync(repositoryPath, cancellationToken, "branch", "-D", sourceBranch).ConfigureAwait(false);
    }

    private static async Task MergeInAsync(
        string path, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken)
    {
        var identity = await IdentityArgsAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            await GitCli.RunAsync(path, cancellationToken, [.. identity, "merge", "--no-ff", "--no-edit", "--message", message, sourceBranch])
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            await SucceedsAsync(path, CancellationToken.None, "merge", "--abort").ConfigureAwait(false);
            throw new RejectedException(
                "merge-conflict",
                $"The work conflicts with {targetBranch}. Nothing was changed: send the task back so the agent can adapt it, or merge it by hand.");
        }
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
