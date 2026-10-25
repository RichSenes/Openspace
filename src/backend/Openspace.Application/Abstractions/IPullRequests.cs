namespace Openspace.Application.Abstractions;

public enum PullRequestState
{
    Open,
    Merged,
    Closed,
}

/// <summary>Pull requests on the forge the repository's remote lives on (GitHub, through its CLI).</summary>
public interface IPullRequests
{
    /// <summary>Whether pull requests can be opened from this repository: a GitHub remote, and the CLI signed in.</summary>
    Task<bool> AvailableAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Opens a pull request of a branch already pushed; returns its address.</summary>
    Task<string> OpenAsync(string repositoryPath, string branch, string target, string title, string body, CancellationToken cancellationToken);

    Task<PullRequestState> StateAsync(string repositoryPath, string url, CancellationToken cancellationToken);
}
