using Microsoft.EntityFrameworkCore;

namespace Todo.Core;

public static class TodoDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EdoraTodo",
        "todo.db");

    public static async Task PrepareAsync(TodoDbContext db, string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // The data exists only on this machine, so a failed migration is permanent loss.
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any() && File.Exists(databasePath))
        {
            await BackUpAsync(db, databasePath);
        }

        await db.Database.MigrateAsync();

        // Background sync will write while the UI reads.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    private static async Task BackUpAsync(TodoDbContext db, string databasePath)
    {
        if (!await CheckpointAsync(db))
        {
            // A backup missing the newest writes is worse than no backup, because it looks
            // like a safeguard, so refuse to migrate rather than take one.
            throw new InvalidOperationException(
                $"Cannot back up '{databasePath}' before migrating: the write-ahead log could "
                + "not be folded into the database file, so the copy would miss the newest "
                + "writes. Close every other program using the database and start again.");
        }

        var backupPath = $"{databasePath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Copy(databasePath, backupPath);

        var backupLength = LengthOf(backupPath);
        var databaseLength = LengthOf(databasePath);
        if (backupLength < databaseLength)
        {
            File.Delete(backupPath);
            throw new InvalidOperationException(
                $"Cannot back up '{databasePath}' before migrating: the copy is {backupLength} "
                + $"of {databaseLength} bytes.");
        }
    }

    /// <summary>
    /// Folds the write-ahead log back into the database file, so a copy of that file alone is
    /// the whole database. Reports whether the log was emptied: another connection can hold a
    /// checkpoint off, and then the copy would be missing the newest writes.
    /// </summary>
    private static async Task<bool> CheckpointAsync(TodoDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return false;
            }

            // busy, then how much of the log is left. Both are -1 outside WAL mode, where
            // there is no side file to fold in and the database file is already whole.
            return reader.GetInt64(0) == 0 && reader.GetInt64(1) <= 0;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Reads the length through a handle. FileInfo.Length reads the directory entry, which
    /// Windows updates lazily while SQLite still holds the file open, so it can report the
    /// length the file had before the checkpoint.
    /// </summary>
    private static long LengthOf(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return stream.Length;
    }
}
