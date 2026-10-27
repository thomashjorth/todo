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

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);

        await using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
        {
            await connection.OpenAsync(ct);
            await using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM INTO $target;";
            vacuum.Parameters.AddWithValue("$target", target);
            await vacuum.ExecuteNonQueryAsync(ct);
        }

        // The tokens 1.16.0 stored, out of the copy and never out of the source. On the NAS the
        // TokensLeave migration would delete them too - but only after the backup it takes first, which
        // would keep them there for good.
        await using (var copy = new SqliteConnection($"Data Source={target};Pooling=False"))
        {
            await copy.OpenAsync(ct);
            await using var strip = copy.CreateCommand();
            strip.CommandText = "DELETE FROM Settings WHERE Key IN ('jira.token', 'ado.token');";
            await strip.ExecuteNonQueryAsync(ct);
        }
    }
}
