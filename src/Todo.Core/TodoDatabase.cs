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
            File.Copy(databasePath, $"{databasePath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
        }

        await db.Database.MigrateAsync();

        // Background sync will write while the UI reads.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
