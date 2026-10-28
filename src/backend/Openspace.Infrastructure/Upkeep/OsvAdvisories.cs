using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>
/// OSV.dev, the open database that gathers GitHub's advisories, PyPI's, the CVEs and more:
/// one batch call says which packages have known vulnerabilities, then each advisory is read
/// for its summary, severity and the version that fixes it.
/// </summary>
internal sealed class OsvAdvisories(HttpClient http) : IAdvisories
{
    private static readonly Uri Batch = new("https://api.osv.dev/v1/querybatch");

    /// <summary>Queries per batch call, as OSV allows.</summary>
    private const int PerBatch = 1000;

    /// <summary>Advisories read at once, and at most per run.</summary>
    private const int AtOnce = 6;

    private const int Most = 400;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<Dependency> packages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var asked = packages.DistinctBy(IAdvisories.Key).ToList();
        var ids = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chunk in asked.Chunk(PerBatch))
        {
            var queries = chunk.Select(p => new { package = new { ecosystem = Name(p.Ecosystem), name = p.Name }, version = p.Version });
            using var response = await http.PostAsJsonAsync(Batch, new { queries }, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var results = answer.RootElement.GetProperty("results").EnumerateArray().ToList();
            for (var i = 0; i < chunk.Length && i < results.Count; i++)
            {
                if (results[i].TryGetProperty("vulns", out var vulns) && vulns.ValueKind == JsonValueKind.Array)
                {
                    ids[IAdvisories.Key(chunk[i])] = [.. vulns.EnumerateArray().Select(v => v.GetProperty("id").GetString()!)];
                }
            }
        }

        var details = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(AtOnce);
        await Task.WhenAll(ids.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Take(Most).Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var response = await http.GetAsync(new Uri($"https://api.osv.dev/v1/vulns/{Uri.EscapeDataString(id)}"), cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }

                response.EnsureSuccessStatusCode();
                using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                lock (details)
                {
                    details[id] = detail.RootElement.Clone();
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        var found = new Dictionary<string, IReadOnlyList<Advisory>>(StringComparer.Ordinal);
        foreach (var package in asked)
        {
            if (ids.TryGetValue(IAdvisories.Key(package), out var list))
            {
                found[IAdvisories.Key(package)] = [.. list.Select(id => details.TryGetValue(id, out var detail)
                    ? Read(detail, package)
                    : new Advisory(id, string.Empty, Severity.Unknown, [], null, Link(id)))];
            }
        }

        return found;
    }

    internal static string Name(Ecosystem ecosystem) => ecosystem switch
    {
        Ecosystem.Npm => "npm",
        Ecosystem.NuGet => "NuGet",
        Ecosystem.PyPI => "PyPI",
        _ => throw new ArgumentOutOfRangeException(nameof(ecosystem)),
    };

    /// <summary>One advisory as it concerns one package version: the fix is the first one after that version.</summary>
    internal static Advisory Read(JsonElement detail, Dependency package)
    {
        var id = detail.GetProperty("id").GetString()!;
        var summary = detail.TryGetProperty("summary", out var s) && s.GetString() is { Length: > 0 } text
            ? text
            : detail.TryGetProperty("details", out var d) ? (d.GetString() ?? string.Empty).Split('\n')[0] : string.Empty;
        var aliases = detail.TryGetProperty("aliases", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
        var severity = detail.TryGetProperty("database_specific", out var specific) && specific.ValueKind == JsonValueKind.Object
            && specific.TryGetProperty("severity", out var rated) && rated.ValueKind == JsonValueKind.String
            ? rated.GetString()!.ToUpperInvariant() switch
            {
                "CRITICAL" => Severity.Critical,
                "HIGH" => Severity.High,
                "MODERATE" or "MEDIUM" => Severity.Moderate,
                "LOW" => Severity.Low,
                _ => Severity.Unknown,
            }
            : Severity.Unknown;
        return new Advisory(id, summary.Trim(), severity, aliases, FixedAfter(detail, package), Link(id));
    }

    /// <summary>
    /// The ranges of the package's entries say where the problem was introduced and where it was
    /// fixed; of the fixes for the range holding this version, the first.
    /// </summary>
    private static string? FixedAfter(JsonElement detail, Dependency package)
    {
        if (!detail.TryGetProperty("affected", out var affected) || affected.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var comparison = package.Ecosystem == Ecosystem.Npm ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var fixes = new List<string>();
        foreach (var entry in affected.EnumerateArray())
        {
            if (!entry.TryGetProperty("package", out var named)
                || !string.Equals(named.GetProperty("ecosystem").GetString(), Name(package.Ecosystem), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(named.GetProperty("name").GetString(), package.Name, comparison)
                || !entry.TryGetProperty("ranges", out var ranges))
            {
                continue;
            }

            foreach (var range in ranges.EnumerateArray().Where(r => r.GetProperty("type").GetString() is "SEMVER" or "ECOSYSTEM"))
            {
                string? introduced = null;
                foreach (var change in range.GetProperty("events").EnumerateArray())
                {
                    if (change.TryGetProperty("introduced", out var from))
                    {
                        introduced = from.GetString();
                    }
                    else if (change.TryGetProperty("fixed", out var to) && to.GetString() is { } fix && introduced is not null)
                    {
                        if ((introduced == "0" || VersionOrder.Instance.Compare(package.Version, introduced) >= 0) && VersionOrder.Instance.Compare(package.Version, fix) < 0)
                        {
                            fixes.Add(fix);
                        }

                        introduced = null;
                    }
                }
            }
        }

        return fixes.Count == 0 ? null : fixes.Min(VersionOrder.Instance);
    }

    private static Uri Link(string id) => new($"https://osv.dev/vulnerability/{Uri.EscapeDataString(id)}");
}
