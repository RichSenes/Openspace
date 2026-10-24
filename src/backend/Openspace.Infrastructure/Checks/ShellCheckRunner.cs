using System.Diagnostics;
using System.Text;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Checks;

/// <summary>Runs check commands through the system shell and keeps the end of what they print.</summary>
internal sealed class ShellCheckRunner : ICheckRunner
{
    /// <summary>Enough to see why something failed; test runners print the summary last.</summary>
    private const int MaxOutput = 12_000;

    public async Task<CheckRun> RunAsync(string folder, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", $"{command} 2>&1" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", $"exec 2>&1\n{command}" } };
        start.WorkingDirectory = folder;
        start.RedirectStandardOutput = true;
        start.RedirectStandardInput = true;
        start.UseShellExecute = false;

        // Tools see a non-interactive run, as on a build server.
        start.Environment["CI"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the shell.");
        process.StandardInput.Close();
        var output = new StringBuilder();
        var reading = ReadAsync(process.StandardOutput, output);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            await reading.ConfigureAwait(false);
            return new CheckRun(process.ExitCode, Tail(output), clock.Elapsed);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            output.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[Stopped after {timeout.TotalMinutes:0} minutes.]");
            return new CheckRun(null, Tail(output), clock.Elapsed);
        }
    }

    private static async Task ReadAsync(StreamReader reader, StringBuilder output)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            lock (output)
            {
                output.Append(buffer, 0, read);
                if (output.Length > MaxOutput * 2)
                {
                    output.Remove(0, output.Length - MaxOutput);
                }
            }
        }
    }

    private static string Tail(StringBuilder output)
    {
        lock (output)
        {
            var text = output.ToString();
            return text.Length <= MaxOutput ? text : string.Concat("…", text.AsSpan(text.Length - MaxOutput));
        }
    }
}
