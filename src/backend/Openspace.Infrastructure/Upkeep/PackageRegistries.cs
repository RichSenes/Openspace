using System.Net;
using System.Text.Json;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>The latest stable versions, from the registries' public read APIs: npm, NuGet, PyPI.</summary>
internal sealed class PackageRegistries(HttpClient http) : IPackageRegistry
{
    public async Task<string?> LatestAsync(Ecosystem ecosystem, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var address = ecosystem switch
        {
            // A scoped name keeps its @ and has its slash escaped: @scope%2Fname.
            Ecosystem.Npm => $"https://registry.npmjs.org/{name.Replace("/", "%2F", StringComparison.Ordinal)}/latest",
            Ecosystem.NuGet => $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(name.ToLowerInvariant())}/index.json",
            Ecosystem.PyPI => $"https://pypi.org/pypi/{Uri.EscapeDataString(name)}/json",
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem)),
        };
        using var response = await http.GetAsync(new Uri(address), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return Latest(ecosystem, answer.RootElement);
    }

    internal static string? Latest(Ecosystem ecosystem, JsonElement answer) => ecosystem switch
    {
        Ecosystem.Npm => answer.TryGetProperty("version", out var version) ? version.GetString() : null,
        Ecosystem.NuGet => answer.TryGetProperty("versions", out var versions)
            ? versions.EnumerateArray().Select(v => v.GetString()!).Where(v => !VersionOrder.IsPreRelease(v)).DefaultIfEmpty().Max(VersionOrder.Instance)
            : null,
        Ecosystem.PyPI => answer.TryGetProperty("info", out var info) && info.TryGetProperty("version", out var latest) ? latest.GetString() : null,
        _ => null,
    };
}
