using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
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
            Path.GetTempPath(), "TodoApp.Tests", $"backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "todo.db");
        var title = $"Survive the migration {Guid.NewGuid():N}";
        SqliteConnection? anchor = null;

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                anchor = OpenOutsideThePool(databasePath);

                await CreateAsync(host, title);
                await RollBackLastMigrationAsync(host);
            }

            // The host left its pooled connections open, as the shipped app does when its
            // process ends, and the anchor holds the database open on top of that, so the
            // newest writes are still in the -wal side file and not in the database file
            // itself. That is the state this backup has to survive.
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
            anchor?.Dispose();
            RunningHost.ClearConnectionPoolFor(databasePath);
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Opens the database outside the connection pool and keeps it open for the whole test.
    /// SQLite folds the write-ahead log back into the database file when the last connection
    /// to it closes, and a pooled connection is closed the moment any test in the process
    /// clears its pool. Without a connection no other test can close, whether the newest
    /// write is still in the log when this test looks is a race it loses at random.
    /// </summary>
    private static SqliteConnection OpenOutsideThePool(string databasePath)
    {
        // Mode=ReadWrite so a mistimed call fails loudly instead of creating a second,
        // empty database beside the one under test.
        var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadWrite;Pooling=False");

        connection.Open();

        // Opening is not enough: SQLite attaches a connection to the write-ahead log only
        // when it first reads through it, and until then a closing connection still counts
        // itself the last one and checkpoints.
        using var attach = connection.CreateCommand();
        attach.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        attach.ExecuteScalar();

        return connection;
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
        // Pooling=False so this read does not leave a connection behind holding the copy
        // open, which would defeat the cleanup at the end of the test.
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
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
