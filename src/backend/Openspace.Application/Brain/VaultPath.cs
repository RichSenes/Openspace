namespace Openspace.Application.Brain;

/// <summary>
/// Paths of notes as the vault knows them: relative, with forward slashes, ending in <c>.md</c>.
/// Anything that could reach outside the vault, or into its git folder, is refused.
/// </summary>
public static class VaultPath
{
    public const string RejectedCode = "brain-path";

    /// <summary>The note's path in its one written form; <c>.md</c> is added when left out.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw Refused("A note needs a path.");
        }

        var slashed = path.Trim().Replace('\\', '/');

        // Rooted on any system: "/etc", "C:/x", "//server".
        if (slashed.StartsWith('/') || Path.IsPathRooted(slashed) || (slashed.Length > 1 && slashed[1] == ':'))
        {
            throw Refused($"\"{path}\" is not inside the vault: give a path relative to it.");
        }

        var segments = slashed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.StartsWith('.')))
        {
            // Hidden folders hold git and Obsidian's own files: notes never live there.
            throw Refused($"\"{path}\" is not inside the vault, or is in one of its hidden folders.");
        }

        var joined = string.Join('/', segments);
        return joined.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? joined : joined + ".md";
    }

    private static RejectedException Refused(string message) => new(RejectedCode, message);
}
