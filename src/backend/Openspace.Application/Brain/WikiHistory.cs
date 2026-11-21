namespace Openspace.Application.Brain;

/// <summary>One commit that changed a note.</summary>
/// <param name="Commit">The full hash.</param>
/// <param name="Author">Who wrote that version: an inhabitant, Shiori, or the person.</param>
/// <param name="Summary">What changed, without the author's name Openspace puts in front of it.</param>
public sealed record NoteCommit(string Commit, string Author, DateTimeOffset Date, string Summary);

/// <summary>A note as one commit left it, with the change from the version before.</summary>
/// <param name="Text">The file as it was, front matter included; null when that commit deleted it.</param>
/// <param name="Diff">The unified diff from the version before (from nothing, for the first one).</param>
/// <param name="Previous">The commit that wrote the version before, null for the first one.</param>
/// <param name="Truncated">True when the text or the diff was too long and was cut.</param>
public sealed record NoteVersion(
    NoteCommit Commit,
    string Path,
    string? Text,
    string Diff,
    string? Previous,
    bool Truncated);

/// <summary>
/// The vault's own git, read only: the wiki shows how each note changed. Kept apart from
/// <see cref="IVault"/> because reading history is the wiki's business, not the writers'.
/// </summary>
public interface IVaultHistory
{
    /// <summary>The commits that changed the note, newest first; empty when the vault has no history yet.</summary>
    Task<IReadOnlyList<NoteCommit>> HistoryAsync(string path, int limit, CancellationToken cancellationToken);

    /// <summary>The note at that commit with its diff; null when the commit is not in the vault.</summary>
    Task<NoteVersion?> VersionAsync(string path, string commit, CancellationToken cancellationToken);
}
