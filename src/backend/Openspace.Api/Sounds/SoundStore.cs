using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Openspace.Domain.Common;

namespace Openspace.Api.Sounds;

public sealed class SoundOptions
{
    public const string SectionName = "Sounds";

    /// <summary>Where the sound choices and the person's own sound files are kept; never in the repository.</summary>
    public string Folder { get; set; } = "sounds";
}

/// <param name="Event">needs-you, question, review or failed.</param>
/// <param name="RoomId">Only for this room's desks; null for every room.</param>
/// <param name="DeskId">Only for this desk; it wins over its room's rule.</param>
/// <param name="Sound">builtin:name, file:name or silent.</param>
public sealed record SoundRule(string Event, Guid? RoomId, Guid? DeskId, string Sound);

/// <param name="Volume">From 0 to 1.</param>
public sealed record SoundConfig(bool Muted, double Volume, IReadOnlyList<SoundRule> Rules);

public sealed record SoundsView(bool Muted, double Volume, IReadOnlyList<SoundRule> Rules, IReadOnlyList<string> Files);

/// <summary>
/// The sounds Openspace plays in the page, and the person's own sound files: kept in a
/// folder of this machine, outside the repository.
/// </summary>
internal sealed partial class SoundStore(IOptions<SoundOptions> options)
{
    public const long MaxFileBytes = 3 * 1024 * 1024;
    public const int MaxRules = 100;

    public static readonly string[] Events = ["needs-you", "question", "review", "failed"];
    public static readonly string[] Builtins = ["chime", "knock", "levelUp", "fail", "coin", "blip"];
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
        [".webm"] = "audio/webm",
        [".flac"] = "audio/flac",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();

    private string Folder => Path.GetFullPath(options.Value.Folder);

    private string ConfigPath => Path.Combine(Folder, "sounds.json");

    public SoundsView Read()
    {
        var config = Config();
        return new SoundsView(config.Muted, config.Volume, config.Rules, Files());
    }

    public SoundsView Save(SoundConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var files = Files();
        if (config.Volume is < 0 or > 1 || double.IsNaN(config.Volume))
        {
            throw new DomainException("The volume goes from 0 to 1.");
        }

        if (config.Rules.Count > MaxRules)
        {
            throw new DomainException($"At most {MaxRules} sound rules.");
        }

        foreach (var rule in config.Rules)
        {
            if (!Events.Contains(rule.Event))
            {
                throw new DomainException($"Unknown moment '{rule.Event}': choose one of {string.Join(", ", Events)}.");
            }

            var known = rule.Sound == "silent"
                || rule.Sound.StartsWith("builtin:", StringComparison.Ordinal) && Builtins.Contains(rule.Sound["builtin:".Length..])
                || rule.Sound.StartsWith("file:", StringComparison.Ordinal) && files.Contains(rule.Sound["file:".Length..]);
            if (!known)
            {
                throw new DomainException($"Unknown sound '{rule.Sound}'.");
            }
        }

        lock (_gate)
        {
            Directory.CreateDirectory(Folder);
            var temporary = ConfigPath + ".tmp";
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json));
            System.IO.File.Move(temporary, ConfigPath, overwrite: true);
        }

        return Read();
    }

    /// <summary>Keeps a sound file of the person's, under a safe name; returns that name.</summary>
    public async Task<string> AddFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var extension = Path.GetExtension(file.FileName);
        if (!Types.ContainsKey(extension))
        {
            throw new DomainException($"Sounds can be {string.Join(", ", Types.Keys)} files.");
        }

        if (file.Length is 0 or > MaxFileBytes)
        {
            throw new DomainException($"A sound file is at most {MaxFileBytes / 1024 / 1024} MB.");
        }

        var stem = UnsafeCharacters().Replace(Path.GetFileNameWithoutExtension(file.FileName), "-").Trim('-', '.');
        stem = stem.Length == 0 ? "sound" : stem[..Math.Min(stem.Length, 60)];
        Directory.CreateDirectory(Folder);
        var name = stem + extension.ToLowerInvariant();
        for (var n = 2; System.IO.File.Exists(Path.Combine(Folder, name)); n++)
        {
            name = $"{stem}-{n}{extension.ToLowerInvariant()}";
        }

        await using var target = System.IO.File.Create(Path.Combine(Folder, name));
        await file.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return name;
    }

    /// <summary>The path of a kept sound file and its type, or null when there is none by that name.</summary>
    public (string Path, string Type)? Find(string name) =>
        Files().Contains(name) ? (Path.Combine(Folder, name), Types[Path.GetExtension(name)]) : null;

    /// <summary>Deletes a sound file; the rules that played it play the default again.</summary>
    public void DeleteFile(string name)
    {
        if (!Files().Contains(name))
        {
            throw new DomainException($"There is no sound file named '{name}'.");
        }

        var config = Config();
        Save(config with { Rules = [.. config.Rules.Where(r => r.Sound != $"file:{name}")] });
        System.IO.File.Delete(Path.Combine(Folder, name));
    }

    private SoundConfig Config()
    {
        lock (_gate)
        {
            return System.IO.File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<SoundConfig>(System.IO.File.ReadAllText(ConfigPath), Json) ?? Default
                : Default;
        }
    }

    private static readonly SoundConfig Default = new(false, 0.6, []);

    private List<string> Files() =>
        Directory.Exists(Folder)
            ? [.. Directory.EnumerateFiles(Folder).Select(Path.GetFileName).OfType<string>().Where(f => Types.ContainsKey(Path.GetExtension(f))).Order(StringComparer.Ordinal)]
            : [];

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeCharacters();
}
