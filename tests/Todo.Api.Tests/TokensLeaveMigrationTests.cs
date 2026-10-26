using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Persistence;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The tokens leave the database, and only the tokens: the base URLs and the rest of the Jira and
/// Azure DevOps settings stay, because the helper's hello writes the URLs and the user chose the rest.
/// </summary>
public class TokensLeaveMigrationTests
{
    [Fact]
    public async Task The_two_tokens_are_deleted_and_every_other_setting_stays()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TodoApp.Tests", $"tokens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "todo.db");

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await RollBackAsync(host);
            }

            using (var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                foreach (var (key, value) in new[]
                {
                    ("jira.token", "a-jira-secret"),
                    ("ado.token", "an-ado-secret"),
                    ("jira.baseUrl", "https://jira.example"),
                    ("ado.baseUrl", "https://ado.example/Collection"),
                    ("jira.projectKey", "SAAS"),
                })
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($key, $value)";
                    insert.Parameters.AddWithValue("$key", key);
                    insert.Parameters.AddWithValue("$value", value);
                    insert.ExecuteNonQuery();
                }
            }

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
                var keys = await db.Settings.Select(s => s.Key).OrderBy(k => k).ToListAsync();

                Assert.Equal(["ado.baseUrl", "jira.baseUrl", "jira.projectKey"], keys);
            }
        }
        finally
        {
            RunningHost.ClearConnectionPoolFor(databasePath);
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open leaves a temporary folder behind, not a failed test.
            }
        }
    }

    /// <summary>Rolls back to the migration before, so the next start-up finds this one pending.</summary>
    private static async Task RollBackAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var index = applied.FindIndex(m => m.EndsWith("TokensLeave", StringComparison.Ordinal));
        Assert.True(index > 0, $"TokensLeave is not among the applied migrations: {string.Join(", ", applied)}");

        await db.Database.GetService<IMigrator>().MigrateAsync(applied[index - 1]);

        Assert.EndsWith("TokensLeave", (await db.Database.GetPendingMigrationsAsync()).First(), StringComparison.Ordinal);
    }
}
