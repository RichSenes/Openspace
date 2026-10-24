using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.Persistence;

public sealed class BackupOptions
{
    public const string SectionName = "Backups";

    /// <summary>Where copies go, relative to the database's own folder unless absolute.</summary>
    public string Folder { get; set; } = "backups";

    /// <summary>How many copies are kept; older ones are deleted.</summary>
    public int Keep { get; set; } = 14;

    /// <summary>How often a copy is taken while Openspace runs.</summary>
    public int EveryHours { get; set; } = 24;
}

/// <summary>Backs the SQLite database up with <c>VACUUM INTO</c>: one consistent file per copy.</summary>
internal sealed class SqliteBackups(string connectionString, IOptions<BackupOptions> options, TimeProvider clock) : IDatabaseBackups, IDisposable
{
    private const string Prefix = "openspace-";
    private readonly SemaphoreSlim _one = new(1, 1);

    public string? Folder { get; } = FolderOf(connectionString, options.Value.Folder);

    public Task<IReadOnlyList<BackupView>> ListAsync(CancellationToken cancellationToken)
    {
        if (Folder is null || !Directory.Exists(Folder))
        {
            return Task.FromResult<IReadOnlyList<BackupView>>([]);
        }

        IReadOnlyList<BackupView> copies =
        [
            .. new DirectoryInfo(Folder).EnumerateFiles($"{Prefix}*.db")
                .Select(f => new BackupView(f.Name, f.FullName, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
                .OrderByDescending(b => b.Name, StringComparer.Ordinal),
        ];
        return Task.FromResult(copies);
    }

    public async Task<BackupView> BackUpAsync(CancellationToken cancellationToken)
    {
        if (Folder is null)
        {
            throw new RejectedException("no-database-file", "This database lives in memory: there is nothing to back up.");
        }

        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Folder);
            var name = string.Create(CultureInfo.InvariantCulture, $"{Prefix}{clock.GetUtcNow().ToLocalTime():yyyy-MM-dd-HHmmss}.db");
            var path = Path.Combine(Folder, name);
            File.Delete(path);

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM INTO $path";
                vacuum.Parameters.AddWithValue("$path", path);
                await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var copies = await ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var old in copies.Skip(Math.Max(1, options.Value.Keep)))
            {
                File.Delete(old.Path);
            }

            return copies.First(c => c.Name == name);
        }
        finally
        {
            _one.Release();
        }
    }

    public void Dispose() => _one.Dispose();

    private static string? FolderOf(string connectionString, string folder)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.Memory || builder.DataSource is "" or ":memory:")
        {
            return null;
        }

        // SQLite reads a relative file name from the working folder: so does this.
        var database = Path.GetFullPath(builder.DataSource);
        return Path.Combine(Path.GetDirectoryName(database)!, folder);
    }
}
