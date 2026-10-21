namespace Openspace.Application.Abstractions;

/// <param name="Code"><c>folder-missing</c>, <c>not-a-repository</c> or <c>no-commits</c>.</param>
/// <param name="CanInitialize">Whether <see cref="IWorkspaceManager.InitializeRepositoryAsync"/> would fix it.</param>
public sealed record RepositoryProblem(string Code, string Message, bool CanInitialize);

/// <param name="Current">The branch checked out in the repository folder; null when detached.</param>
/// <param name="Branches">Local branches work can be merged into (task branches excluded).</param>
public sealed record BranchList(string? Current, IReadOnlyList<string> Branches);

/// <summary>A private working copy for one task, on its own branch.</summary>
public sealed record Workspace(string Path, string Branch);

/// <summary>Gives each task its own working copy so agents never step on each other or on you.</summary>
public interface IWorkspaceManager
{
    /// <summary>Why agents could not work in this folder, or null when it is a usable git repository.</summary>
    Task<RepositoryProblem?> CheckRepositoryAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Makes the folder a git repository with at least one commit, creating what is missing.</summary>
    Task InitializeRepositoryAsync(string repositoryPath, CancellationToken cancellationToken);

    Task<Workspace> PrepareAsync(string repositoryPath, string taskKey, string title, CancellationToken cancellationToken);

    /// <summary>Everything the agent changed, as a unified diff.</summary>
    Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>Commits the changes on the task branch and removes the working copy. The branch stays.</summary>
    Task CommitAsync(string repositoryPath, string workspacePath, string message, CancellationToken cancellationToken);

    Task<BranchList> ListBranchesAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Merges a task branch into <paramref name="targetBranch"/> without switching your checkout,
    /// then deletes the task branch. Throws <see cref="RejectedException"/> when it cannot be done
    /// safely (uncommitted changes, conflicts); in that case nothing is changed.
    /// </summary>
    Task MergeAsync(
        string repositoryPath, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken);

    /// <summary>Removes the working copy and its branch.</summary>
    Task DiscardAsync(string repositoryPath, Workspace workspace, CancellationToken cancellationToken);
}
