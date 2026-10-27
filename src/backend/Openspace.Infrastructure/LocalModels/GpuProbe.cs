using System.Diagnostics;
using System.Globalization;
using System.Text;
using Openspace.Application.LocalModels;

namespace Openspace.Infrastructure.LocalModels;

/// <summary>
/// Reads the card's memory through the NVIDIA driver tool and the running programs' command
/// lines on Linux. Nothing else: no file, window or output of those programs is looked at.
/// </summary>
internal sealed class GpuProbe : IGpuProbe
{
    public async Task<GpuMemory?> MemoryAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("nvidia-smi")
        {
            ArgumentList = { "--query-gpu=memory.used,memory.total", "--format=csv,noheader,nounits" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? ParseMemory(output) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>The first card of <c>nvidia-smi</c>'s CSV: "used, total" in MiB.</summary>
    internal static GpuMemory? ParseMemory(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var parts = line?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            ? new GpuMemory(used, total)
            : null;
    }

    public IReadOnlyList<string> RunningHolders(IReadOnlyList<GpuHolder> holders)
    {
        if (holders.Count == 0 || !OperatingSystem.IsLinux())
        {
            return [];
        }

        var self = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var commandLines = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            var pid = Path.GetFileName(folder);
            if (pid == self || !pid.All(char.IsAsciiDigit))
            {
                continue;
            }

            try
            {
                commandLines.Add(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "cmdline"))).Replace('\0', ' '));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone meanwhile, or not ours to read.
            }
        }

        return Match(holders, commandLines);
    }

    internal static IReadOnlyList<string> Match(IReadOnlyList<GpuHolder> holders, IEnumerable<string> commandLines)
    {
        var lines = commandLines.ToList();
        return
        [
            .. holders
                .Where(h => !string.IsNullOrWhiteSpace(h.Match)
                    && lines.Exists(l => l.Contains(h.Match, StringComparison.OrdinalIgnoreCase)))
                .Select(h => string.IsNullOrWhiteSpace(h.Name) ? h.Match : h.Name),
        ];
    }
}
