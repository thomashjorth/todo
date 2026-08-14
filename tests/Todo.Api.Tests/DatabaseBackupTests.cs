using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The pre-migration backup is the only copy of data that exists nowhere else, so the thing
/// worth testing is that it holds the rows, not that a file with the right name appeared.
/// </summary>
public class DatabaseBackupTests
{
    [Fact]
    public async Task Backup_taken_before_a_migration_contains_the_data()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "EdoraTodo.Tests", $"backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "todo.db");
        var title = $"Survive the migration {Guid.NewGuid():N}";

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await CreateAsync(host, title);
                await RollBackLastMigrationAsync(host);
            }

            // The host left its pooled connections open, as the shipped app does when its
            // process ends, so the newest writes are still in the -wal side file and not in
            // the database file itself. That is the state this backup has to survive.
            var databaseFileAlone = Path.Combine(directory, "probe.db");
            File.Copy(databasePath, databaseFileAlone);
            Assert.False(
                ContainsTaskTitled(databaseFileAlone, title),
                "The database file already holds the task without its write-ahead log, so this "
                + "test would pass even if the backup were taken without a checkpoint.");

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                Assert.Empty(await PendingMigrationsAsync(host));
            }

            var backupPath = Assert.Single(Directory.GetFiles(directory, "todo.db.bak-*"));
            Assert.True(
                ContainsTaskTitled(backupPath, title),
                $"The backup '{backupPath}' does not contain the task that was in the database.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Undoes the last migration for real - its Down runs and its history row goes - so the
    /// next startup finds exactly one migration pending and takes a backup before applying it.
    /// </summary>
    private static async Task RollBackLastMigrationAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.True(applied.Count >= 2, "The rollback needs a migration to roll back to.");

        await db.Database.GetService<IMigrator>().MigrateAsync(applied[^2]);

        Assert.Single(await db.Database.GetPendingMigrationsAsync());
    }

    private static async Task<IReadOnlyList<string>> PendingMigrationsAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return [.. await db.Database.GetPendingMigrationsAsync()];
    }

    private static bool ContainsTaskTitled(string databasePath, string title)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Tasks'";
        if (Convert.ToInt64(command.ExecuteScalar()) == 0)
        {
            return false;
        }

        command.CommandText = "SELECT COUNT(*) FROM Tasks WHERE Title = $title";
        command.Parameters.AddWithValue("$title", title);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static async Task CreateAsync(RunningHost host, string title)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
