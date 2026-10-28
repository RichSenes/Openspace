using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>
/// Reads what a project depends on from the files package managers keep: npm's
/// package-lock.json, .NET's project files with their central versions and the restore's
/// project.assets.json (which has every package, the indirect ones too), Python's pinned
/// requirements. Only those files are opened; folders of downloads, builds and tools are not
/// entered, nor the ones it is told to skip.
/// </summary>
internal sealed partial class DependencyFiles : IDependencyReader
{
    private static readonly HashSet<string> NotCode = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "build", "out", "target", "venv", "__pycache__", "vendor", "coverage",
    };

    private static readonly System.Buffers.SearchValues<char> NotPlain = System.Buffers.SearchValues.Create("$*[]()<>, ");

    /// <summary>Folders deep enough for any project layout; deeper is someone else's code.</summary>
    private const int Deepest = 8;

    public IReadOnlyList<Dependency> Read(string folder, IReadOnlyCollection<string> skip)
    {
        ArgumentNullException.ThrowIfNull(skip);
        var root = Path.GetFullPath(folder);
        var skipped = skip.Select(s => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, Expand(s))))).ToList();
        var files = Walk(root, skipped).ToList();

        var central = files.Where(f => Path.GetFileName(f).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
            .SelectMany(CentralVersions)
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Version, StringComparer.OrdinalIgnoreCase);

        var found = new List<Dependency>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var relative = Path.GetRelativePath(root, file);
            if (name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase))
            {
                found.AddRange(NpmLock(file, relative));
            }
            else if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            {
                found.AddRange(DotNetProject(file, relative, central));
            }
            else if (name.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                found.AddRange(Requirements(file, relative));
            }
        }

        // One entry per package version: direct when any file names it.
        return [.. found
            .GroupBy(d => (d.Ecosystem, Name: d.Ecosystem == Ecosystem.Npm ? d.Name : d.Name.ToUpperInvariant(), d.Version))
            .Select(g => g.First() with { Direct = g.Any(d => d.Direct) })];
    }

    private static IEnumerable<string> Walk(string root, List<string> skipped)
    {
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> folders;
            try
            {
                files = Directory.EnumerateFiles(folder).ToList();
                folders = depth < Deepest ? Directory.EnumerateDirectories(folder).ToList() : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var inner in folders)
            {
                var name = Path.GetFileName(inner);
                if (!name.StartsWith('.') && !NotCode.Contains(name) && !skipped.Any(s => Under(inner, s)) && !new DirectoryInfo(inner).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push((inner, depth + 1));
                }
            }
        }
    }

    private static bool Under(string path, string folder) =>
        path.Equals(folder, StringComparison.Ordinal) || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>npm 7 and later list every installed package under "packages"; npm 6 nests them under "dependencies".</summary>
    internal static IEnumerable<Dependency> NpmLock(string file, string relative)
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(file));
        var lockRoot = lockFile.RootElement;
        var direct = new HashSet<string>(StringComparer.Ordinal);
        if (lockRoot.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Object)
        {
            if (packages.TryGetProperty(string.Empty, out var self))
            {
                direct.UnionWith(Names(self));
            }

            foreach (var entry in packages.EnumerateObject())
            {
                var at = entry.Name.LastIndexOf("node_modules/", StringComparison.Ordinal);
                if (at < 0 || entry.Value.ValueKind != JsonValueKind.Object
                    || entry.Value.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.True
                    || !entry.Value.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = entry.Name[(at + "node_modules/".Length)..];
                yield return new Dependency(Ecosystem.Npm, name, version.GetString()!, at == 0 && direct.Contains(name), relative);
            }

            yield break;
        }

        // npm 6: the names in package.json beside it are the direct ones.
        var manifest = Path.Combine(Path.GetDirectoryName(file)!, "package.json");
        if (File.Exists(manifest))
        {
            using var package = JsonDocument.Parse(File.ReadAllText(manifest));
            direct.UnionWith(Names(package.RootElement));
        }

        if (lockRoot.TryGetProperty("dependencies", out var tree))
        {
            foreach (var dependency in Nested(tree, true))
            {
                yield return new Dependency(Ecosystem.Npm, dependency.Name, dependency.Version, dependency.Top && direct.Contains(dependency.Name), relative);
            }
        }
    }

    private static IEnumerable<(string Name, string Version, bool Top)> Nested(JsonElement tree, bool top)
    {
        if (tree.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var entry in tree.EnumerateObject())
        {
            if (entry.Value.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String && !version.GetString()!.StartsWith("file:", StringComparison.Ordinal))
            {
                yield return (entry.Name, version.GetString()!, top);
            }

            if (entry.Value.TryGetProperty("dependencies", out var inner))
            {
                foreach (var nested in Nested(inner, false))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The packages a package.json (or the lock's own entry) names, for running and for developing.</summary>
    private static IEnumerable<string> Names(JsonElement package)
    {
        foreach (var group in new[] { "dependencies", "devDependencies", "optionalDependencies", "peerDependencies" })
        {
            if (package.TryGetProperty(group, out var names) && names.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in names.EnumerateObject())
                {
                    yield return name.Name;
                }
            }
        }
    }

    private static IEnumerable<(string Name, string Version)> CentralVersions(string file) =>
        XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => (Name: (string?)e.Attribute("Include") ?? string.Empty, Version: (string?)e.Attribute("Version") ?? string.Empty))
            .Where(v => v.Name.Length > 0 && Plain(v.Version));

    /// <summary>
    /// A .NET project's packages: the ones it names, and, when it was restored, every package
    /// the restore resolved for it, as obj/project.assets.json records them.
    /// </summary>
    internal static IEnumerable<Dependency> DotNetProject(string file, string relative, IReadOnlyDictionary<string, string> central)
    {
        var named = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var name = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var version = (string?)reference.Attribute("VersionOverride") ?? (string?)reference.Attribute("Version")
                ?? reference.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value ?? central.GetValueOrDefault(name);
            named[name.Trim()] = version is not null && Plain(version) ? version.Trim() : null;
        }

        var assets = Path.Combine(Path.GetDirectoryName(file)!, "obj", "project.assets.json");
        var resolved = new List<Dependency>();
        if (File.Exists(assets))
        {
            using var restore = JsonDocument.Parse(File.ReadAllText(assets));
            if (restore.RootElement.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Object)
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    var slash = library.Name.IndexOf('/', StringComparison.Ordinal);
                    if (slash > 0 && library.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                    {
                        var name = library.Name[..slash];
                        resolved.Add(new Dependency(Ecosystem.NuGet, name, library.Name[(slash + 1)..], named.ContainsKey(name), relative));
                    }
                }
            }
        }

        // A named package the restore did not record (never restored) still counts, at the version written.
        return resolved.Concat(named
            .Where(n => n.Value is not null && !resolved.Any(r => r.Name.Equals(n.Key, StringComparison.OrdinalIgnoreCase)))
            .Select(n => new Dependency(Ecosystem.NuGet, n.Key, n.Value!, true, relative)));
    }

    /// <summary>Pinned requirements only, "name==1.2.3": a range says nothing about the version in use.</summary>
    internal static IEnumerable<Dependency> Requirements(string file, string relative)
    {
        foreach (var line in File.ReadLines(file))
        {
            var match = Pinned().Match(line);
            if (match.Success)
            {
                yield return new Dependency(Ecosystem.PyPI, match.Groups["name"].Value, match.Groups["version"].Value, true, relative);
            }
        }
    }

    /// <summary>An exact version, not a range, a wildcard or an MSBuild property.</summary>
    private static bool Plain(string version) => version.Trim().Length > 0 && !version.AsSpan().ContainsAny(NotPlain);

    private static string Expand(string folder) =>
        folder.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder.TrimStart('~', '/')) : folder;

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(\[[^\]]*\])?\s*==\s*(?<version>[0-9][A-Za-z0-9.+!-]*)\s*(;.*)?(#.*)?$")]
    private static partial Regex Pinned();
}
