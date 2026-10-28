using System.Globalization;
using System.Text.Json;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>
/// The .NET SDKs and runtimes and Node installed here, against their makers' published release
/// lists: Microsoft's release index (latest of each line, whether it fixes security problems,
/// end of support) and Node's distribution index and release schedule.
/// </summary>
internal sealed class Toolchains(HttpClient http) : IToolchains
{
    private static readonly Uri DotNetIndex = new("https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json");

    private static readonly Uri NodeIndex = new("https://nodejs.org/dist/index.json");

    private static readonly Uri NodeSchedule = new("https://raw.githubusercontent.com/nodejs/Release/main/schedule.json");

    public async Task<IReadOnlyList<Toolchain>> CheckAsync(CancellationToken cancellationToken)
    {
        var found = new List<Toolchain>();
        var sdks = await Commands.RunAsync("dotnet", ["--list-sdks"], cancellationToken).ConfigureAwait(false);
        var runtimes = await Commands.RunAsync("dotnet", ["--list-runtimes"], cancellationToken).ConfigureAwait(false);
        if (sdks is { ExitCode: 0 } || runtimes is { ExitCode: 0 })
        {
            using var index = JsonDocument.Parse(await http.GetStringAsync(DotNetIndex, cancellationToken).ConfigureAwait(false));
            found.AddRange(DotNet(
                Installed(sdks?.Output ?? string.Empty, null),
                Installed(runtimes?.Output ?? string.Empty, "Microsoft.NETCore.App"),
                index.RootElement));
        }

        var node = await Commands.RunAsync("node", ["--version"], cancellationToken).ConfigureAwait(false);
        if (node is { ExitCode: 0 } && node.Output.Trim() is { Length: > 0 } version)
        {
            using var releases = JsonDocument.Parse(await http.GetStringAsync(NodeIndex, cancellationToken).ConfigureAwait(false));
            using var schedule = JsonDocument.Parse(await http.GetStringAsync(NodeSchedule, cancellationToken).ConfigureAwait(false));
            found.Add(Node(version, releases.RootElement, schedule.RootElement));
        }

        return found;
    }

    /// <summary>"10.0.402 [/path]" lines of <c>dotnet --list-sdks</c>, or "Name 10.0.13 [/path]" of <c>--list-runtimes</c> for one runtime.</summary>
    internal static List<string> Installed(string output, string? runtime) =>
        [.. Commands.Lines(output)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => runtime is null ? p.Length >= 1 : p.Length >= 2 && p[0] == runtime)
            .Select(p => runtime is null ? p[0] : p[1])];

    /// <summary>For each line (10.0, 9.0) installed, the newest SDK and runtime of it against the line's latest.</summary>
    internal static IEnumerable<Toolchain> DotNet(IReadOnlyList<string> sdks, IReadOnlyList<string> runtimes, JsonElement index)
    {
        var lines = index.GetProperty("releases-index").EnumerateArray().ToDictionary(r => r.GetProperty("channel-version").GetString()!, StringComparer.Ordinal);
        foreach (var (name, installed, latestField) in new[] { (".NET SDK", sdks, "latest-sdk"), (".NET runtime", runtimes, "latest-runtime") })
        {
            foreach (var group in installed.GroupBy(Channel).OrderByDescending(g => g.Key, VersionOrder.Instance))
            {
                var newest = group.Max(VersionOrder.Instance)!;
                if (!lines.TryGetValue(group.Key, out var line))
                {
                    continue;
                }

                var latest = line.GetProperty(latestField).GetString();
                var behind = latest is not null && VersionOrder.Instance.Compare(latest, newest) > 0;
                DateOnly? end = line.TryGetProperty("eol-date", out var eol) && DateOnly.TryParse(eol.GetString(), CultureInfo.InvariantCulture, out var date) ? date : null;
                yield return new Toolchain(name, newest, behind ? latest : null, behind && line.TryGetProperty("security", out var security) && security.GetBoolean(), end);
            }
        }
    }

    /// <summary>"10.0.402" belongs to the 10.0 line.</summary>
    private static string Channel(string version) => string.Join('.', version.Split('.').Take(2));

    /// <summary>The installed Node against the newest of its major line; a security release in between counts.</summary>
    internal static Toolchain Node(string installed, JsonElement releases, JsonElement schedule)
    {
        var version = installed.Trim().TrimStart('v');
        var major = version.Split('.')[0];
        var newer = releases.EnumerateArray()
            .Select(r => (Version: r.GetProperty("version").GetString()!.TrimStart('v'), Security: r.TryGetProperty("security", out var s) && s.ValueKind == JsonValueKind.True))
            .Where(r => r.Version.Split('.')[0] == major && VersionOrder.Instance.Compare(r.Version, version) > 0)
            .ToList();
        DateOnly? end = schedule.TryGetProperty($"v{major}", out var line) && line.TryGetProperty("end", out var e)
            && DateOnly.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var date) ? date : null;
        return new Toolchain("Node", version, newer.Count == 0 ? null : newer.Select(r => r.Version).Max(VersionOrder.Instance), newer.Any(r => r.Security), end);
    }
}
