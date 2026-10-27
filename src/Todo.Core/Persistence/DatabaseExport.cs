using Microsoft.Data.Sqlite;

namespace Todo.Core.Persistence;

/// <summary>
/// The database as one file to carry to the NAS. VACUUM INTO rather than a file copy: it reads through
/// the write-ahead log, so the newest writes are in the copy, and it writes a single file with nothing
/// beside it - copying todo.db alone would leave them behind (CLAUDE.md, "todo.db alone is not the
/// database").
/// </summary>
public static class DatabaseExport
{
    public static async Task ExportAsync(string source, string target, CancellationToken ct = default)
    {
        if (File.Exists(target))
        {
            throw new InvalidOperationException(
                $"'{target}' exists already, and an export never overwrites a file. Name another one.");
        }

        var fullTarget = Path.GetFullPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);

        // Built beside the target and moved into place only once the tokens are out of it, so a failure
        // halfway never leaves a file with tokens in it under the name the user is told to copy.
        var pending = $"{fullTarget}.{Guid.NewGuid():N}.partial";

        try
        {
            // Read-only, so the export never checkpoints or otherwise touches the user's own database.
            await using (var connection = new SqliteConnection(ConnectionString(source, SqliteOpenMode.ReadOnly)))
            {
                await connection.OpenAsync(ct);
                await ExecuteAsync(connection, "VACUUM INTO $target;", ct, ("$target", pending));
            }

            // The tokens 1.16.0 stored, out of the copy and never out of the source. On the NAS the
            // TokensLeave migration would delete them too - but only after the backup it takes first,
            // which would keep them there for good. The VACUUM after the DELETE is load-bearing: a DELETE
            // alone only unlinks the row, the bundled SQLite leaves the value on the page, and the token
            // was measured still readable in the file's bytes. VACUUM writes the file anew from the live
            // rows; PRAGMA secure_delete would also do, and was measured to, but one is enough.
            await using (var copy = new SqliteConnection(ConnectionString(pending, SqliteOpenMode.ReadWrite)))
            {
                await copy.OpenAsync(ct);
                await ExecuteAsync(copy, "DELETE FROM Settings WHERE Key IN ('jira.token', 'ado.token');", ct);
                await ExecuteAsync(copy, "VACUUM;", ct);
            }

            File.Move(pending, fullTarget);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    /// <summary>Through the builder, so a path with a semicolon in it cannot add a keyword of its own.</summary>
    private static string ConnectionString(string path, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = mode,
        Pooling = false,
    }.ToString();

    private static async Task ExecuteAsync(
        SqliteConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
