using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure.Tests;

/// <summary>Backs up a real SQLite file in a throwaway folder.</summary>
public sealed class SqliteBackupsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("openspace-backups-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero));

    private string ConnectionString => $"Data Source={Path.Combine(_folder, "openspace.db")};Pooling=False";

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SqliteBackups Backups(int keep = 14) =>
        new(ConnectionString, Options.Create(new BackupOptions { Keep = keep }), _clock);

    private async Task WriteAsync(string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_backup_is_a_full_copy_next_to_the_database()
    {
        await WriteAsync("CREATE TABLE Offices (Name TEXT); INSERT INTO Offices VALUES ('E Corp');");
        using var backups = Backups();

        var copy = await backups.BackUpAsync(default);

        Assert.Equal(Path.Combine(_folder, "backups"), Path.GetDirectoryName(copy.Path));
        await using var connection = new SqliteConnection($"Data Source={copy.Path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT Name FROM Offices";
        Assert.Equal("E Corp", await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Only_the_latest_backups_are_kept_newest_first()
    {
        await WriteAsync("CREATE TABLE Notes (Text TEXT);");
        using var backups = Backups(keep: 2);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await backups.BackUpAsync(default);
            _clock.Advance(TimeSpan.FromDays(1));
        }

        var names = (await backups.ListAsync(default)).Select(b => b.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.True(string.CompareOrdinal(names[0], names[1]) > 0);
    }

    [Fact]
    public async Task A_database_in_memory_has_nothing_to_back_up()
    {
        using var backups = new SqliteBackups("Data Source=tests;Mode=Memory;Cache=Shared", Options.Create(new BackupOptions()), _clock);

        Assert.Null(backups.Folder);
        Assert.Empty(await backups.ListAsync(default));
        await Assert.ThrowsAsync<Application.RejectedException>(() => backups.BackUpAsync(default));
    }
}
