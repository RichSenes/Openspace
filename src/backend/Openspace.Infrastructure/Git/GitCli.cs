using System.Diagnostics;

namespace Openspace.Infrastructure.Git;

/// <summary>Runs the git command line and fails loudly with git's own message.</summary>
internal static class GitCli
{
    public static async Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // Never wait for a person to type something.
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed: {(await error.ConfigureAwait(false)).Trim()}");
        }

        return await output.ConfigureAwait(false);
    }
}
