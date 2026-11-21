namespace Openspace.Application.Brain;

/// <summary>
/// Finds which note a <c>[[wikilink]]</c> points at, the way Obsidian does: a link may give the
/// whole path, the end of it, or only the file's name, with or without <c>.md</c>, in any case.
/// The label after <c>|</c> and the heading after <c>#</c> are already gone from
/// <see cref="Note.Links"/>, so only the target is resolved here.
/// </summary>
public sealed class WikiLinks
{
    private readonly HashSet<string> _paths;
    private readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public WikiLinks(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (var path in _paths)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (!_byName.TryGetValue(name, out var same))
            {
                _byName[name] = same = [];
            }

            same.Add(path);
        }
    }

    /// <summary>The path of the note <paramref name="link"/> points at, or null when no note answers to it.</summary>
    /// <param name="from">The note the link is written in: when two notes share a name, the one beside it wins.</param>
    public string? Resolve(string link, string from)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(from);
        var target = link.Trim().Replace('\\', '/').TrimStart('/');
        if (target.Length == 0)
        {
            return null;
        }

        if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            target += ".md";
        }

        if (_paths.Contains(target))
        {
            return target;
        }

        var name = target[(target.LastIndexOf('/') + 1)..];
        if (!_byName.TryGetValue(name, out var named))
        {
            return null;
        }

        // A link with folders in it must match the end of the path; a bare name matches any folder.
        var folder = from.Contains('/', StringComparison.Ordinal) ? from[..from.LastIndexOf('/')] : "";
        return named
            .Where(p => string.Equals(p, target, StringComparison.OrdinalIgnoreCase) || p.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => string.Equals(Folder(p), folder, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The notes that link to <paramref name="path"/>, itself left out, in path order.</summary>
    public static IReadOnlyList<Note> Backlinks(string path, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(notes);
        var links = new WikiLinks(notes.Select(n => n.Path));
        return [.. notes
            .Where(n => n.Path != path && n.Links.Any(l => links.Resolve(l, n.Path) == path))
            .OrderBy(n => n.Path, StringComparer.Ordinal)];
    }

    private static string Folder(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : "";
}
