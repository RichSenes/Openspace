using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Skills;

/// <summary>What a Claude Code agent is given to load skills: a plugin, and the folders it may read them from.</summary>
/// <param name="PluginFolder">The plugin to pass with <c>--plugin-dir</c>.</param>
/// <param name="ReadFolders">The folders holding the skills' files: readable, never writable.</param>
internal sealed record SkillAccess(string PluginFolder, IReadOnlyList<string> ReadFolders);

/// <summary>
/// The installed skills. Claude Code ignores them when it runs restricted, so for its agents
/// Openspace builds a tiny plugin whose skills point at the installed ones: every skill, or
/// only those a role names.
/// </summary>
internal sealed class SkillCatalog(IOptionsMonitor<SkillsOptions> options) : ISkillCatalog
{
    /// <summary>The plugin's name, which Claude Code puts before each skill: <c>openspace:security-audit</c>.</summary>
    public const string PluginName = "openspace";

    private readonly Lock _building = new();

    public string? Folder => Existing(options.CurrentValue.Folder);

    /// <summary>
    /// The folders an agent may read skills from: the skills, and the plugins of links, which a
    /// Claude Code agent names when it hands work on to OpenCode or to its helpers.
    /// </summary>
    public IReadOnlyList<string> ReadFolders => Folder is { } folder
        ? [folder, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Expand(options.CurrentValue.PluginsFolder)))]
        : [];

    public IReadOnlyList<SkillInfo> List()
    {
        if (Folder is not { } folder)
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(folder)
            .Select(dir => (Dir: dir, File: Path.Combine(dir, "SKILL.md")))
            .Where(s => File.Exists(s.File))
            .Select(s => Read(s.File, Path.GetFileName(s.Dir)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)];
    }

    /// <summary>The plugin for these skills (null for all of them); null when there are none to give.</summary>
    public SkillAccess? For(IReadOnlyList<string>? names)
    {
        if (Folder is not { } folder || names is { Count: 0 })
        {
            return null;
        }

        var plugins = Expand(options.CurrentValue.PluginsFolder);
        var key = names is null ? "all" : Hash(string.Join('\n', names.Order(StringComparer.Ordinal)));
        var plugin = Path.Combine(plugins, key);
        lock (_building)
        {
            Directory.CreateDirectory(Path.Combine(plugin, ".claude-plugin"));
            File.WriteAllText(
                Path.Combine(plugin, ".claude-plugin", "plugin.json"),
                $$"""{"name":"{{PluginName}}","version":"1.0.0","description":"Skills Openspace hands to its desks"}""");
            var skills = Path.Combine(plugin, "skills");
            Remove(skills);
            if (names is null)
            {
                Directory.CreateSymbolicLink(skills, folder);
            }
            else
            {
                Directory.CreateDirectory(skills);
                foreach (var name in names.Where(n => File.Exists(Path.Combine(folder, n, "SKILL.md"))))
                {
                    Directory.CreateSymbolicLink(Path.Combine(skills, name), Path.Combine(folder, name));
                }
            }
        }

        return new SkillAccess(plugin, [plugin, folder]);
    }

    /// <summary>A skill's name and description from the front matter of its SKILL.md.</summary>
    internal static SkillInfo Read(string file, string fallbackName)
    {
        string? name = null;
        string? description = null;
        using var reader = new StreamReader(file);
        if (reader.ReadLine()?.Trim() == "---")
        {
            while (reader.ReadLine() is { } line && line.Trim() != "---")
            {
                if (Value(line, "name") is { } n)
                {
                    name = n;
                }
                else if (Value(line, "description") is { } d)
                {
                    description = d;
                }
            }
        }

        return new SkillInfo(string.IsNullOrWhiteSpace(name) ? fallbackName : name, description ?? string.Empty);
    }

    private static string? Value(string line, string key)
    {
        if (!line.StartsWith(key + ":", StringComparison.Ordinal))
        {
            return null;
        }

        var value = line[(key.Length + 1)..].Trim();
        return value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;
    }

    private static void Remove(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null)
        {
            info.Delete();
        }
        else if (info.Exists)
        {
            // Only links live inside: deleting them never touches the skills they point at.
            foreach (var link in info.EnumerateDirectories())
            {
                link.Delete();
            }

            info.Delete();
        }
    }

    private static string? Existing(string? folder) =>
        string.IsNullOrWhiteSpace(folder) || !Directory.Exists(Expand(folder)) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Expand(folder)));

    internal static string Expand(string path) =>
        path.StartsWith('~') ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..] : path;

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
