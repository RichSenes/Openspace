namespace Openspace.Application.Abstractions;

/// <summary>A private working copy for one task, on its own branch.</summary>
public sealed record Workspace(string Path, string Branch);

/// <summary>Gives each task its own working copy so agents never step on each other or on you.</summary>
public interface IWorkspaceManager
{
    Task<Workspace> PrepareAsync(string repositoryPath, string taskKey, string title, CancellationToken cancellationToken);

    /// <summary>Everything the agent changed, as a unified diff.</summary>
    Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>Commits the changes on the task branch and removes the working copy. The branch stays.</summary>
    Task CommitAsync(string repositoryPath, string workspacePath, string message, CancellationToken cancellationToken);

    /// <summary>Removes the working copy and its branch.</summary>
    Task DiscardAsync(string repositoryPath, Workspace workspace, CancellationToken cancellationToken);
}
