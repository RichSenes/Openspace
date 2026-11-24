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

    public async Task<IReadOnlyList<GpuUser>> UsersAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("nvidia-smi")
        {
            ArgumentList = { "--query-compute-apps=pid,used_memory", "--format=csv,noheader,nounits" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return [];
            }

            var users = new List<GpuUser>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mib))
                {
                    continue;
                }

                string commandLine;
                try
                {
                    commandLine = Encoding.UTF8.GetString(File.ReadAllBytes($"/proc/{parts[0]}/cmdline")).Replace('\0', ' ');
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    commandLine = "";
                }

                var (name, own) = NameOf(commandLine);
                users.Add(new GpuUser(name, mib, own));
            }

            return [.. users.OrderByDescending(u => u.MiB)];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// A program on the card, named for people from its command line, never its paths or
    /// arguments: Openspace's own by what they are, the usual others by their name.
    /// </summary>
    internal static (string Name, bool Openspace) NameOf(string commandLine)
    {
        if (commandLine.Contains("speak-server.py", StringComparison.Ordinal))
        {
            return ("Shiori's voice", true);
        }

        if (commandLine.Contains("listen-server.py", StringComparison.Ordinal))
        {
            return ("Shiori's ears", true);
        }

        foreach (var (match, name) in Known)
        {
            if (commandLine.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                return (name, false);
            }
        }

        var program = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return (program is null ? "Another program" : Path.GetFileName(program), false);
    }

    private static readonly (string Match, string Name)[] Known =
    [
        ("llama-server", "A language model (llama-server)"),
        ("ComfyUI", "ComfyUI"),
        ("OneTrainer", "OneTrainer"),
        ("atelier_train", "OneTrainer"),
        ("ollama", "Ollama"),
    ];

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
