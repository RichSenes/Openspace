namespace Openspace.Application.Abstractions;

/// <param name="Code"><c>folder-missing</c>, <c>not-a-repository</c> or <c>no-commits</c>.</param>
/// <param name="CanInitialize">Whether <see cref="IWorkspaceManager.InitializeRepositoryAsync"/> would fix it.</param>
public sealed record RepositoryProblem(string Code, string Message, bool CanInitialize);

/// <param name="Current">The branch checked out in the repository folder; null when detached.</param>
/// <param name="Branches">Local branches work can be merged into (task branches excluded).</param>
/// <param name="UncommittedChanges">Files changed and not committed in your checkout: nothing merges into <paramref name="Current"/> while there are any.</param>
/// <param name="Remotes">The repository's remotes, such as origin; none for a project kept on this machine.</param>
/// <param name="Tracking">How each branch stands against its remote one, as of the last fetch.</param>
/// <param name="PullRequests">Whether accepted work can go up as a pull request instead of a local merge.</param>
public sealed record BranchList(
    string? Current,
    IReadOnlyList<string> Branches,
    int UncommittedChanges = 0,
    IReadOnlyList<string>? Remotes = null,
    IReadOnlyList<BranchTracking>? Tracking = null,
    bool PullRequests = false);

/// <param name="Upstream">The remote branch it follows, such as origin/main; null when it follows none.</param>
/// <param name="Ahead">Commits it has that the remote branch lacks: what a push would send.</param>
/// <param name="Behind">Commits of the remote branch it lacks: to pull before pushing.</param>
public sealed record BranchTracking(string Branch, string? Upstream, int Ahead, int Behind);

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

    /// <summary>
    /// Brings one of your branches into another (say beta into main), moving it forward when it
    /// can and with a merge commit otherwise; the source stays. Same safety as a task's merge:
    /// your checkout never moves, uncommitted changes or conflicts change nothing.
    /// </summary>
    Task<BranchMerge> MergeBranchAsync(string repositoryPath, string sourceBranch, string targetBranch, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the task's working copy again on its branch and starts merging
    /// <paramref name="targetBranch"/> into it, leaving any conflicts marked in the files for
    /// the agent. Returns the files in conflict.
    /// </summary>
    Task<IReadOnlyList<string>> StartCatchUpAsync(
        string repositoryPath, Workspace workspace, string targetBranch, CancellationToken cancellationToken);

    /// <summary>
    /// A throwaway working copy of a branch, detached so that nothing done in it moves the
    /// branch; your checkout is never touched. Close it with <see cref="CloseScratchAsync"/>.
    /// </summary>
    Task<string> OpenScratchAsync(string repositoryPath, string branch, CancellationToken cancellationToken);

    Task CloseScratchAsync(string repositoryPath, string path, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a branch to its remote (origin when it follows none yet), after fetching: refused
    /// when the remote has commits the branch lacks. Never forces. Returns where it went.
    /// </summary>
    Task<string> PushAsync(string repositoryPath, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a branch up to date with its remote one, fast-forward only: in your checkout
    /// when it is that branch and has no uncommitted changes, else without touching the
    /// checkout. Refused rather than merging or losing anything.
    /// </summary>
    Task UpdateFromRemoteAsync(string repositoryPath, string branch, CancellationToken cancellationToken);

    /// <summary>Removes the working copy and its branch.</summary>
    Task DiscardAsync(string repositoryPath, Workspace workspace, CancellationToken cancellationToken);
}

/// <summary>What bringing a branch into another did.</summary>
public enum BranchMerge
{
    /// <summary>The target already had all of it.</summary>
    UpToDate,

    /// <summary>The target only moved forward: no merge commit.</summary>
    FastForward,

    /// <summary>Both had their own work: a merge commit joins them.</summary>
    Merged,
}
