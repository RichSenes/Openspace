namespace Openspace.Application.Brain;

/// <summary>The vault on disk: notes in Markdown files, every write a commit in its own git.</summary>
public interface IVault
{
    /// <summary>The vault's folder, in full.</summary>
    string Folder { get; }

    /// <summary>Creates what is missing of the standard structure; existing files are never touched.</summary>
    Task EnsureAsync(CancellationToken cancellationToken);

    /// <summary>Every note of the vault, outside its hidden folders.</summary>
    Task<IReadOnlyList<Note>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One note, or null when there is no such file.</summary>
    Task<Note?> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes the note at once and whole, then commits it with its author in the message.</summary>
    /// <param name="author">Who wrote it: an inhabitant, Shiori, or the person.</param>
    /// <param name="summary">What changed, in a few words; the commit says "wrote" the note when left out.</param>
    Task WriteAsync(Note note, string author, CancellationToken cancellationToken, string? summary = null);
}
