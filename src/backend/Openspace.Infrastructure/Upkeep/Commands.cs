using System.Diagnostics;

namespace Openspace.Infrastructure.Upkeep;

/// <param name="Output">What it wrote on its standard output.</param>
/// <param name="Errors">What it wrote on its standard error.</param>
internal sealed record CommandResult(int ExitCode, string Output, string Errors);

/// <summary>Runs the read-only commands maintenance asks: versions, lists of updates, git counts.</summary>
internal static class Commands
{
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    /// <returns>Null when the program is not installed.</returns>
    public static async Task<CommandResult?> RunAsync(string program, IEnumerable<string> arguments, CancellationToken cancellationToken, string? folder = null)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = folder ?? string.Empty,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // English and plain, whatever the person's language: the output is parsed.
        start.Environment["LC_ALL"] = "C";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Longest);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return new CommandResult(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
