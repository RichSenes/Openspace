using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Git;

/// <summary>
/// One git worktree per task, on a branch named <c>openspace/&lt;title&gt;-&lt;key&gt;</c>.
/// Your own checkout and branch are never touched.
/// </summary>
internal sealed class GitWorkspaceManager(IOptions<WorkspaceOptions> options) : IWorkspaceManager
{
    private const string FallbackAuthor = "Openspace";
    private const string FallbackEmail = "openspace@localhost";

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
        var branch = $"openspace/{Slug(title)}-{taskKey}";
        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "add", "-b", branch, path, "HEAD").ConfigureAwait(false);
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
            var identity = await HasIdentityAsync(workspacePath, cancellationToken).ConfigureAwait(false)
                ? Array.Empty<string>()
                : ["-c", $"user.name={FallbackAuthor}", "-c", $"user.email={FallbackEmail}"];
            await GitCli.RunAsync(workspacePath, cancellationToken, [.. identity, "commit", "--message", message]).ConfigureAwait(false);
        }

        await GitCli.RunAsync(repositoryPath, cancellationToken, "worktree", "remove", "--force", workspacePath).ConfigureAwait(false);
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

    private static async Task<bool> HasIdentityAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return (await GitCli.RunAsync(path, cancellationToken, "config", "user.email").ConfigureAwait(false)).Trim().Length > 0;
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
