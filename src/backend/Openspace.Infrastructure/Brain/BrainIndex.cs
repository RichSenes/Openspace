using System.Globalization;
using Microsoft.Data.Sqlite;
using Openspace.Application.Brain;

namespace Openspace.Infrastructure.Brain;

/// <summary>
/// The vault's notes in an SQLite FTS5 table of Openspace's database (made by the BrainIndex
/// migration). It only mirrors the files: rebuilt from them on start, and note by note when the
/// watcher sees a change. The folders' READMEs are left out: they explain the vault, they are not
/// what anyone searches for.
/// </summary>
internal sealed class BrainIndex(string connectionString, IVault vault) : IBrainIndex, IDisposable
{
    private const string Columns = "Path, Title, Body, Kind, Status, Offices, Rooms, Paths, Review, Modified";
    private const int ExcerptLength = 200;

    // Rebuilds and refreshes take turns, so a full rebuild never interleaves with a note's update.
    private readonly SemaphoreSlim _one = new(1, 1);
    private volatile bool _built;

    public void Dispose() => _one.Dispose();

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "DELETE FROM BrainNotes", null, cancellationToken).ConfigureAwait(false);
            foreach (var note in notes.Where(Indexed))
            {
                await InsertAsync(connection, transaction, note, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _built = true;
        }
        finally
        {
            _one.Release();
        }
    }

    public async Task RefreshAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var path in paths)
            {
                Note? note;
                try
                {
                    note = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                }
                catch (Application.RejectedException)
                {
                    // Not a note's path (a hidden folder, say): there is nothing to index.
                    continue;
                }

                await ExecuteAsync(connection, transaction, "DELETE FROM BrainNotes WHERE Path = $path", path, cancellationToken).ConfigureAwait(false);
                if (note is not null && Indexed(note))
                {
                    await InsertAsync(connection, transaction, note, cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _one.Release();
        }
    }

    public async Task<IReadOnlyList<IndexedNote>> FindAsync(IReadOnlyList<string> words, string? kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (!_built)
        {
            // Asked before the start-up rebuild finished: build now rather than answer from nothing.
            await RebuildAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var filters = new List<string>();
        if (words.Count > 0)
        {
            // Every word, each also as the start of a longer one; quoted so FTS5 reads no operators in it.
            filters.Add("BrainNotes MATCH $match");
            command.Parameters.AddWithValue("$match", string.Join(' ', words.Select(w => $"\"{w.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*")));
        }

        if (kind is not null)
        {
            filters.Add("Kind = $kind COLLATE NOCASE");
            command.Parameters.AddWithValue("$kind", kind);
        }

        // The title weighs more than the body; the other columns are not searched.
        var rank = words.Count > 0 ? "bm25(BrainNotes, 0, 5.0, 1.0)" : "0";
        var excerpt = words.Count > 0 ? "snippet(BrainNotes, 2, '', '', '…', 24)" : $"substr(Body, 1, {ExcerptLength})";
        command.CommandText = $"""
            SELECT Path, Title, Kind, Status, Offices, Rooms, Paths, Review, Modified, {rank}, {excerpt}
            FROM BrainNotes {(filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "")}
            """;

        var found = new List<IndexedNote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            found.Add(new IndexedNote(
                reader.GetString(0), reader.GetString(1), Text(reader, 2), Text(reader, 3),
                Lines(reader, 4), Lines(reader, 5), Lines(reader, 6),
                Text(reader, 7) is { } review ? DateOnly.ParseExact(review, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetDouble(9), Excerpt(reader.GetString(10))));
        }

        return found;
    }

    private static bool Indexed(Note note) => !string.Equals(Path.GetFileName(note.Path), "README.md", StringComparison.OrdinalIgnoreCase);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string? path, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (path is not null)
        {
            command.Parameters.AddWithValue("$path", path);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, Note note, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO BrainNotes ({Columns}) VALUES ($path, $title, $body, $kind, $status, $offices, $rooms, $paths, $review, $modified)";
        command.Parameters.AddWithValue("$path", note.Path);
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$body", note.Body);
        command.Parameters.AddWithValue("$kind", (object?)note.Kind ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)note.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("$offices", string.Join('\n', note.Offices));
        command.Parameters.AddWithValue("$rooms", string.Join('\n', note.Rooms));
        command.Parameters.AddWithValue("$paths", string.Join('\n', note.Paths));
        command.Parameters.AddWithValue("$review", (object?)note.Review?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$modified", note.Modified.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);

    private static string[] Lines(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? [] : reader.GetString(column).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>One line of plain words, without the heading the body usually starts with.</summary>
    private static string Excerpt(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#'));
        var flat = string.Join(' ', lines);
        return flat.Length > ExcerptLength ? flat[..ExcerptLength].TrimEnd() + "…" : flat;
    }
}
