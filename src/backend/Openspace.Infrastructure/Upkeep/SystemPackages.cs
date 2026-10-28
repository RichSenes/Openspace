using System.Globalization;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>
/// Asks Ubuntu's own tools what is waiting, without installing anything or asking for a
/// password: apt's update counter (from the lists the system refreshes by itself), Snap and Flatpak.
/// </summary>
internal sealed class SystemPackages : ISystemUpdates
{
    private const string AptCheck = "/usr/lib/update-notifier/apt-check";

    private const string RestartFlag = "/var/run/reboot-required";

    public async Task<SystemUpdates?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var apt = File.Exists(AptCheck) ? await Commands.RunAsync(AptCheck, [], cancellationToken).ConfigureAwait(false) : null;
        var (packages, security) = apt is { ExitCode: 0 } ? AptCounts(apt.Errors) : (0, 0);
        var snaps = await Commands.RunAsync("snap", ["refresh", "--list"], cancellationToken).ConfigureAwait(false);
        var flatpaks = await Commands.RunAsync("flatpak", ["remote-ls", "--updates", "--columns=application"], cancellationToken).ConfigureAwait(false);
        if (apt is null && snaps is null && flatpaks is null)
        {
            return null;
        }

        var apps = (snaps is { ExitCode: 0 } ? Snaps(snaps.Output).Select(s => $"snap {s}") : [])
            .Concat(flatpaks is { ExitCode: 0 } ? Flatpaks(flatpaks.Output).Select(f => $"flatpak {f}") : [])
            .ToList();
        return new SystemUpdates(packages, security, apps, File.Exists(RestartFlag));
    }

    /// <summary>apt-check writes "updates;security updates" on its error output.</summary>
    internal static (int Packages, int Security) AptCounts(string text)
    {
        var parts = text.Trim().Split(';');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var packages)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var security)
            ? (packages, security)
            : (0, 0);
    }

    /// <summary>"snap refresh --list" prints a table with a "Name" header, or one line saying all is up to date.</summary>
    internal static IEnumerable<string> Snaps(string output)
    {
        var lines = Commands.Lines(output);
        return lines.Length > 1 && lines[0].StartsWith("Name", StringComparison.Ordinal)
            ? lines.Skip(1).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            : [];
    }

    internal static IEnumerable<string> Flatpaks(string output) =>
        Commands.Lines(output).Where(l => !l.StartsWith("Application", StringComparison.Ordinal) && !l.Contains(' ', StringComparison.Ordinal));
}
