using Openspace.Application.Queue;

namespace Openspace.Application.Brain;

/// <summary>A note as the tree lists it: enough to show and sort it without reading its body.</summary>
/// <param name="Name">The file's name, <c>.md</c> included.</param>
public sealed record WikiTreeNote(string Path, string Name, string Title, string? Kind, string? Status, DateTimeOffset Modified);

/// <summary>A folder of the vault with what is inside it; the root has an empty name and path.</summary>
public sealed record WikiFolder(string Name, string Path, IReadOnlyList<WikiFolder> Folders, IReadOnlyList<WikiTreeNote> Notes);

/// <summary>A note as the map draws it: where it lives, what it is, who wrote it, without its body.</summary>
public sealed record WikiGraphNote(string Path, string Title, string? Kind, string? Status, IReadOnlyList<string> Offices, string? Author);

/// <summary>A <c>[[wikilink]]</c> from one note to another, both resolved to their paths.</summary>
public sealed record WikiGraphLink(string From, string To);

/// <summary>Every note and every link between them: what the map lays out.</summary>
public sealed record WikiGraph(IReadOnlyList<WikiGraphNote> Notes, IReadOnlyList<WikiGraphLink> Links);

/// <summary>A note the person wrote, and whether it is new.</summary>
public sealed record WikiWrite(Note Note, bool Created);

/// <summary>
/// The wiki's engine: the vault as a tree, what links to a note, how it changed, the person's own
/// edits, and why a file is the way it is. It reads the files each time: the vault is small, and
/// the files are the truth even when Obsidian changed them a moment ago.
/// </summary>
public sealed class Wiki(IVault vault, IVaultHistory history)
{
    public const int DefaultHistory = 50;
    public const int MaxHistory = 500;

    /// <summary>The longest note the person may write in one go: a note, not a dump.</summary>
    public const int MaxText = 1024 * 1024;

    public const string NowRefusedCode = "brain-now";
    public const string TooLongCode = "brain-too-long";

    public async Task<WikiFolder> TreeAsync(CancellationToken cancellationToken) =>
        Tree(await vault.ListAsync(cancellationToken).ConfigureAwait(false));

    public async Task<WikiGraph> GraphAsync(CancellationToken cancellationToken) =>
        Graph(await vault.ListAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// The whole file of a note as an editor shows it, front matter included; null when there is no
    /// such note. Written back from what was read, so fields Openspace does not know are kept.
    /// </summary>
    public async Task<string?> TextAsync(string path, CancellationToken cancellationToken) =>
        await vault.ReadAsync(VaultPath.Normalize(path), cancellationToken).ConfigureAwait(false) is { } note
            ? VaultFormat.Write(note)
            : null;

    /// <summary>The notes that link to the one at <paramref name="path"/>; null when there is no such note.</summary>
    public async Task<IReadOnlyList<Note>?> BacklinksAsync(string path, CancellationToken cancellationToken)
    {
        var target = VaultPath.Normalize(path);
        var notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
        return notes.Any(n => n.Path == target) ? WikiLinks.Backlinks(target, notes) : null;
    }

    public Task<IReadOnlyList<NoteCommit>> HistoryAsync(string path, int? limit, CancellationToken cancellationToken) =>
        history.HistoryAsync(VaultPath.Normalize(path), Math.Clamp(limit ?? DefaultHistory, 1, MaxHistory), cancellationToken);

    public Task<NoteVersion?> VersionAsync(string path, string commit, CancellationToken cancellationToken) =>
        history.VersionAsync(VaultPath.Normalize(path), commit, cancellationToken);

    /// <summary>
    /// Writes a note as the person, front matter and all, committed with their name. This is the
    /// one way the constitution changes: agents only propose, Shiori only accepts what they propose.
    /// <c>Now.md</c> is refused: Openspace rewrites it every few seconds, so an edit would be lost.
    /// </summary>
    /// <param name="text">The whole file, as in an editor: front matter, then Markdown.</param>
    /// <param name="person">The person's name, the author of the commit.</param>
    public async Task<WikiWrite> WriteAsync(string path, string text, string person, string? summary, CancellationToken cancellationToken)
    {
        var normalized = VaultPath.Normalize(path);
        if (string.Equals(normalized, NowKeeper.NotePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new RejectedException(NowRefusedCode, "Now.md is written by Openspace from the running tasks: it cannot be edited.");
        }

        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxText)
        {
            throw new RejectedException(TooLongCode, $"A note can be up to {MaxText / 1024} KB long.");
        }

        var created = await vault.ReadAsync(normalized, cancellationToken).ConfigureAwait(false) is null;
        var what = string.IsNullOrWhiteSpace(summary) ? $"{(created ? "wrote" : "edited")} {normalized}" : summary;
        await vault.WriteAsync(VaultFormat.Parse(normalized, text), person, cancellationToken, what).ConfigureAwait(false);
        var written = await vault.ReadAsync(normalized, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{normalized} was written but cannot be read back.");
        return new WikiWrite(written, created);
    }

    public async Task<IReadOnlyList<WikiReason>> WhyAsync(string file, string? office, string? room, CancellationToken cancellationToken) =>
        WikiWhy.For(file, office, await vault.ListAsync(cancellationToken).ConfigureAwait(false), room);

    /// <summary>
    /// The notes in path order and their links, each pair once: links to no note, and a note's
    /// links to itself, are left out.
    /// </summary>
    public static WikiGraph Graph(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var ordered = notes.OrderBy(n => n.Path, StringComparer.Ordinal).ToList();
        var resolver = new WikiLinks(ordered.Select(n => n.Path));
        var links = new List<WikiGraphLink>();
        foreach (var note in ordered)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in note.Links)
            {
                if (resolver.Resolve(link, note.Path) is { } to && to != note.Path && seen.Add(to))
                {
                    links.Add(new WikiGraphLink(note.Path, to));
                }
            }
        }

        return new WikiGraph([.. ordered.Select(n => new WikiGraphNote(n.Path, n.Title, n.Kind, n.Status, n.Offices, n.Author))], links);
    }

    /// <summary>The notes in their folders: folders first, then notes, each by name.</summary>
    public static WikiFolder Tree(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return Build("", "", [.. notes.Select(n => (n.Path, n))]);
    }

    private static WikiFolder Build(string name, string path, List<(string Tail, Note Note)> inside)
    {
        var folders = inside
            .Where(x => x.Tail.Contains('/', StringComparison.Ordinal))
            .GroupBy(x => x.Tail[..x.Tail.IndexOf('/', StringComparison.Ordinal)], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Build(g.Key, path.Length == 0 ? g.Key : $"{path}/{g.Key}", [.. g.Select(x => (x.Tail[(g.Key.Length + 1)..], x.Note))]))
            .ToList();
        var notes = inside
            .Where(x => !x.Tail.Contains('/', StringComparison.Ordinal))
            .OrderBy(x => x.Tail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Tail, StringComparer.Ordinal)
            .Select(x => new WikiTreeNote(x.Note.Path, x.Tail, x.Note.Title, x.Note.Kind, x.Note.Status, x.Note.Modified))
            .ToList();
        return new WikiFolder(name, path, folders, notes);
    }
}
