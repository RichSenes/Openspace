namespace Openspace.Application.Abstractions;

/// <param name="ExitCode">Null when the command was stopped for taking too long.</param>
/// <param name="Output">The end of what it printed, errors included, in order.</param>
public sealed record CheckRun(int? ExitCode, string Output, TimeSpan Duration)
{
    public bool Passed => ExitCode == 0;
}

/// <summary>Runs a room's check command in a folder, through the shell, as a person would type it.</summary>
public interface ICheckRunner
{
    Task<CheckRun> RunAsync(string folder, string command, TimeSpan timeout, CancellationToken cancellationToken);
}
