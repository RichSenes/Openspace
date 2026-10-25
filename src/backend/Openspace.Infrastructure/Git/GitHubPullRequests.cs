using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Git;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>The GitHub CLI; found in ~/.local/bin too when it is not on the path.</summary>
    public string Command { get; set; } = "gh";
}

/// <summary>Pull requests through the GitHub CLI, signed in with the person's own account.</summary>
internal sealed class GitHubPullRequests(IOptions<GitHubOptions> options) : IPullRequests
{
    public async Task<bool> AvailableAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        string remote;
        try
        {
            remote = await GitCli.RunAsync(repositoryPath, cancellationToken, "remote", "get-url", "origin").ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!remote.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var (code, _) = await RunAsync(repositoryPath, cancellationToken, "auth", "status").ConfigureAwait(false);
        return code == 0;
    }

    public async Task<string> OpenAsync(
        string repositoryPath, string branch, string target, string title, string body, CancellationToken cancellationToken)
    {
        var (code, output) = await RunAsync(
            repositoryPath, cancellationToken, "pr", "create", "--base", target, "--head", branch, "--title", title, "--body", body).ConfigureAwait(false);
        var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith("https://", StringComparison.Ordinal));
        return code == 0 && url is not null
            ? url
            : throw new RejectedException("pull-request-failed", $"GitHub did not open the pull request: {output.Trim()}");
    }

    public async Task<PullRequestState> StateAsync(string repositoryPath, string url, CancellationToken cancellationToken)
    {
        var (code, output) = await RunAsync(repositoryPath, cancellationToken, "pr", "view", url, "--json", "state").ConfigureAwait(false);
        if (code != 0)
        {
            throw new RejectedException("pull-request-failed", $"GitHub did not say how the pull request stands: {output.Trim()}");
        }

        using var json = JsonDocument.Parse(output);
        return json.RootElement.GetProperty("state").GetString() switch
        {
            "MERGED" => PullRequestState.Merged,
            "CLOSED" => PullRequestState.Closed,
            _ => PullRequestState.Open,
        };
    }

    private async Task<(int Code, string Output)> RunAsync(string folder, CancellationToken cancellationToken, params string[] args)
    {
        var start = new ProcessStartInfo(Command())
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // Never wait for a person to type something.
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["NO_COLOR"] = "1";

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the GitHub CLI.");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (127, "The GitHub CLI (gh) is not installed.");
        }
    }

    private string Command()
    {
        var command = options.Value.Command;
        if (command != "gh")
        {
            return command;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "gh");
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator).Any(dir => File.Exists(Path.Combine(dir, "gh")));
        return onPath || !File.Exists(local) ? command : local;
    }
}
