using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Persistence;
using Todo.Core.Tags;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The TagColors migration's hand-written UPDATE: the user's existing tags take the rainbow in the
/// order they were created. Every other test meets the migration on an empty table.
/// </summary>
public class TagColorMigrationTests
{
    /// <summary>
    /// 22 tags, inserted with the highest id first, so a migration that numbered them in the order
    /// the rows were written would give the reverse; and one past the 21 slots, so the wrap shows.
    /// </summary>
    [Fact]
    public async Task Existing_tags_take_the_colours_in_the_order_they_were_created()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TodoApp.Tests", $"tagcolors-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "todo.db");
        var ids = Enumerable.Range(1, TagColors.Count + 1).Select(i => (long)i * 3).ToList();

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await RollBackTagColorsAsync(host);
            }

            using (var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                foreach (var id in Enumerable.Reverse(ids))
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Tags (Id, Name) VALUES ($id, $name)";
                    insert.Parameters.AddWithValue("$id", id);
                    insert.Parameters.AddWithValue("$name", $"tag {id}");
                    insert.ExecuteNonQuery();
                }
            }

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
                var colours = await db.Tags.OrderBy(t => t.Id).Select(t => t.Color).ToListAsync();

                Assert.Equal([.. Enumerable.Range(0, TagColors.Count), 0], colours);
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

    /// <summary>Runs TagColors' Down for real, so the next start-up finds it pending with the rows in place.</summary>
    private static async Task RollBackTagColorsAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var tagColors = applied.FindIndex(m => m.EndsWith("TagColors", StringComparison.Ordinal));
        Assert.True(tagColors > 0, $"TagColors is not among the applied migrations: {string.Join(", ", applied)}");

        await db.Database.GetService<IMigrator>().MigrateAsync(applied[tagColors - 1]);

        Assert.EndsWith("TagColors", (await db.Database.GetPendingMigrationsAsync()).First(), StringComparison.Ordinal);
    }
}
