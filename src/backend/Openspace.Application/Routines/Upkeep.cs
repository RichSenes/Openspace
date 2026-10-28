using System.Globalization;

namespace Openspace.Application.Routines;

/// <summary>Where a package comes from, named as the vulnerability databases name it.</summary>
public enum Ecosystem
{
    Npm,
    NuGet,
    PyPI,
}

/// <summary>A package a project uses, at the version it is locked to.</summary>
/// <param name="Direct">Named by the project itself, not only pulled in by another package.</param>
/// <param name="Manifest">The file that says so, relative to the project's folder.</param>
public sealed record Dependency(Ecosystem Ecosystem, string Name, string Version, bool Direct, string Manifest);

/// <summary>Reads what a project depends on from its own files: lock files, project files, requirements.</summary>
public interface IDependencyReader
{
    /// <param name="skip">Folders never to look into, such as the person's private ones.</param>
    IReadOnlyList<Dependency> Read(string folder, IReadOnlyCollection<string> skip);
}

/// <summary>How bad a known vulnerability is, as its advisory rates it.</summary>
public enum Severity
{
    Unknown,
    Low,
    Moderate,
    High,
    Critical,
}

/// <summary>A known vulnerability of a package version.</summary>
/// <param name="Aliases">Its other names, such as the CVE.</param>
/// <param name="Fixed">The first version that fixes it after the one in use; null when none does yet.</param>
public sealed record Advisory(string Id, string Summary, Severity Severity, IReadOnlyList<string> Aliases, string? Fixed, Uri Link)
{
    /// <summary>The name people know it by: the CVE when there is one.</summary>
    public string Name => Aliases.FirstOrDefault(a => a.StartsWith("CVE-", StringComparison.Ordinal)) ?? Id;
}

/// <summary>Public vulnerability databases: what is known to be wrong with a package version.</summary>
public interface IAdvisories
{
    /// <returns>For each package asked about, by <see cref="Key"/>, its advisories; none for the clean ones.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<Dependency> packages, CancellationToken cancellationToken);

    public static string Key(Dependency package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return $"{package.Ecosystem}:{package.Name.ToUpperInvariant()}@{package.Version}";
    }
}

/// <summary>The public package registries: npm, NuGet, PyPI.</summary>
public interface IPackageRegistry
{
    /// <returns>The latest stable version published; null when the registry does not know it.</returns>
    Task<string?> LatestAsync(Ecosystem ecosystem, string name, CancellationToken cancellationToken);
}

/// <summary>A tool projects are built or run with, as installed here against its makers' latest.</summary>
/// <param name="Latest">The latest release of the same line (10.0, 22); null when it is up to date.</param>
/// <param name="Security">A release after the installed one fixes security problems.</param>
/// <param name="EndOfLife">When its makers stop fixing it, if they said.</param>
public sealed record Toolchain(string Name, string Installed, string? Latest, bool Security, DateOnly? EndOfLife);

/// <summary>The SDKs and runtimes on this computer: .NET, Node.</summary>
public interface IToolchains
{
    Task<IReadOnlyList<Toolchain>> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>What the computer's own package managers have waiting.</summary>
/// <param name="Packages">System packages (apt) with a newer version.</param>
/// <param name="Security">Of those, the ones that fix security problems.</param>
/// <param name="Apps">Snap and Flatpak apps with a newer version, as "snap firefox".</param>
/// <param name="Restart">The computer must restart for updates already installed to count.</param>
public sealed record SystemUpdates(int Packages, int Security, IReadOnlyList<string> Apps, bool Restart);

public interface ISystemUpdates
{
    /// <returns>Null on a system it cannot ask.</returns>
    Task<SystemUpdates?> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Work in a repository that exists only on this disk.</summary>
/// <param name="Changed">Files changed and not committed.</param>
/// <param name="Unpushed">Commits on the current branch its remote does not have.</param>
/// <param name="HasRemote">Whether the branch has a remote at all.</param>
public sealed record RepositoryState(string Branch, int Changed, int Unpushed, bool HasRemote);

public interface IRepositoryStates
{
    /// <returns>Null when the folder is not a git repository.</returns>
    Task<RepositoryState?> ReadAsync(string folder, CancellationToken cancellationToken);
}

/// <summary>
/// Orders version numbers the way package managers do, closely enough for updates: numbers
/// part by part ("1.10" after "1.9"), a pre-release before its release ("2.0.0-rc.1" before "2.0.0").
/// </summary>
public sealed class VersionOrder : IComparer<string?>
{
    public static readonly VersionOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        var (left, leftTag) = Split(x);
        var (right, rightTag) = Split(y);
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var order = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (order != 0)
            {
                return order;
            }
        }

        return (leftTag.Length, rightTag.Length) switch
        {
            (0, 0) => 0,
            (0, _) => 1,
            (_, 0) => -1,
            _ => string.CompareOrdinal(leftTag, rightTag),
        };
    }

    /// <summary>Whether going from one to the other changes the first number: the kind of update that may break things.</summary>
    public static bool IsMajor(string from, string to) => Split(from).Numbers.FirstOrDefault() != Split(to).Numbers.FirstOrDefault();

    /// <summary>Whether a version is a pre-release: "-rc.1", "-beta", "a1".</summary>
    public static bool IsPreRelease(string version) => Split(version).Tag.Length > 0;

    private static (long[] Numbers, string Tag) Split(string? version)
    {
        var text = (version ?? string.Empty).Trim().TrimStart('v', 'V');
        var build = text.IndexOf('+', StringComparison.Ordinal);
        text = build >= 0 ? text[..build] : text;
        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        var numbers = text[..end].Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .ToArray();
        return (numbers, text[end..].TrimStart('-', '.'));
    }
}
