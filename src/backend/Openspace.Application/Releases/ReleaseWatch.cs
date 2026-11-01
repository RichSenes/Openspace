namespace Openspace.Application.Releases;

/// <summary>The version of Openspace running here, as built.</summary>
public sealed record AppVersion(string Value);

/// <summary>A published release of Openspace.</summary>
/// <param name="Version">Its number, without the "v" of its tag.</param>
/// <param name="Notes">What changed, as written on the release, in Markdown.</param>
public sealed record Release(string Version, string Name, string Notes, Uri Url, DateTimeOffset? PublishedAt);

/// <summary>Where releases are published.</summary>
public interface IReleaseSource
{
    /// <summary>The latest release, drafts and pre-releases left out; null when there is none.</summary>
    Task<Release?> LatestAsync(CancellationToken cancellationToken);
}

public sealed class ReleaseOptions
{
    public const string SectionName = "Releases";

    /// <summary>Look for new releases now and then. Openspace never updates itself: it only says so.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Every { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Where releases are published, as <c>owner/name</c> on GitHub; empty for the repository Openspace was cloned from.</summary>
    public string? Repository { get; set; }
}

/// <param name="Current">The version running here.</param>
/// <param name="Latest">The latest release seen, if any was found.</param>
/// <param name="Newer">Whether the latest release is newer than this one.</param>
/// <param name="CheckedAt">When releases were last looked at.</param>
public sealed record VersionView(string Current, Release? Latest, bool Newer, DateTimeOffset? CheckedAt);

/// <summary>
/// The latest release, looked for now and then. A failed look keeps the last one seen: a
/// missing network is no news. It never downloads or installs anything.
/// </summary>
public sealed class ReleaseWatch(IReleaseSource source, AppVersion version, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private Release? _latest;
    private DateTimeOffset? _checkedAt;

    public async Task<VersionView> CheckAsync(CancellationToken cancellationToken)
    {
        var latest = await source.LatestAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _latest = latest;
            _checkedAt = clock.GetUtcNow();
        }

        return View();
    }

    public VersionView View()
    {
        lock (_gate)
        {
            return new VersionView(version.Value, _latest, _latest is not null && IsNewer(_latest.Version, version.Value), _checkedAt);
        }
    }

    /// <summary>Whether one version number comes after the other, part by part: 0.10.0 after 0.9.2.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        var (a, b) = (Parts(candidate), Parts(current));
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var (x, y) = (i < a.Length ? a[i] : 0, i < b.Length ? b[i] : 0);
            if (x != y)
            {
                return x > y;
            }
        }

        return false;
    }

    /// <summary>The numbers of a version: "v1.2.3-beta+abc" gives 1, 2, 3.</summary>
    public static int[] Parts(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var plain = version.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return [.. plain.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0)];
    }
}
