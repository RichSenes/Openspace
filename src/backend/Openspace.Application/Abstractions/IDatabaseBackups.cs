namespace Openspace.Application.Abstractions;

/// <param name="Path">Where the copy is, to restore it with <c>scripts/openspace restore</c>.</param>
public sealed record BackupView(string Name, string Path, long Size, DateTimeOffset CreatedAt);

/// <summary>Copies of the database, taken while Openspace runs, newest first.</summary>
public interface IDatabaseBackups
{
    /// <summary>Where copies go; null when the database is not a file (in tests, say).</summary>
    string? Folder { get; }

    Task<IReadOnlyList<BackupView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Takes a consistent copy now, even while agents write, and keeps only the latest ones.</summary>
    Task<BackupView> BackUpAsync(CancellationToken cancellationToken);
}
