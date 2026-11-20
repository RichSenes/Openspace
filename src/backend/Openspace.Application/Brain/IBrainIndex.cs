namespace Openspace.Application.Brain;

/// <summary>The notes' words and fields, kept in Openspace's database so search is quick. Deleting it loses nothing.</summary>
public interface IBrainIndex
{
    /// <summary>Reads every note of the vault again.</summary>
    Task RebuildAsync(CancellationToken cancellationToken);

    /// <summary>Reads these notes again; one that no longer exists leaves the index.</summary>
    Task RefreshAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken);

    /// <summary>The notes whose words match, or every note when there are no words.</summary>
    /// <param name="words">Words to look for, all of them, each also as the start of a longer word.</param>
    /// <param name="kind">Only notes of this kind, when given.</param>
    Task<IReadOnlyList<IndexedNote>> FindAsync(IReadOnlyList<string> words, string? kind, CancellationToken cancellationToken);
}

/// <param name="TextRank">How well the words match, lower is better; 0 when no words were asked.</param>
/// <param name="Excerpt">A short piece of the body around the words.</param>
public sealed record IndexedNote(
    string Path,
    string Title,
    string? Kind,
    string? Status,
    IReadOnlyList<string> Offices,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<string> Paths,
    DateOnly? Review,
    DateTimeOffset Modified,
    double TextRank,
    string Excerpt);
