using System.Text;
using Microsoft.Data.Sqlite;
using Todo.Core.Persistence;
using Todo.TestSupport;
using Todo.TestSupport.Builders;

namespace Todo.Api.Tests;

/// <summary>
/// The copy the user carries to the NAS: one file with everything in it, and without the two tokens -
/// which would otherwise land on the NAS, in the database until the migration ran and in the backup the
/// migration takes just before.
/// </summary>
public class DatabaseExportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("TodoApp.Tests.Export.").FullName;

    private string Source => Path.Combine(_directory, "todo.db");

    private string Target => Path.Combine(_directory, "export", "todo.db");

    public void Dispose()
    {
        RunningHost.ClearConnectionPoolFor(Source);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_copy_is_one_file_with_the_tasks_and_without_the_tokens()
    {
        await using (var host = await RunningHost.StartAtAsync(Source))
        {
            await host.AddAndSaveChangesAsync(new TaskItemBuilder().Titled("Flyt til NAS'en").Build());
        }

        // Written the way 1.16.0 stored them, by key, because no code path writes them any more.
        Execute(Source, """
            INSERT INTO Settings (Key, Value) VALUES ('jira.token', 'a-jira-pat');
            INSERT INTO Settings (Key, Value) VALUES ('ado.token', 'an-ado-pat');
            INSERT INTO Settings (Key, Value) VALUES ('jira.projectKey', 'SAAS');
            """);

        await DatabaseExport.ExportAsync(Source, Target);

        Assert.True(File.Exists(Target));
        Assert.False(File.Exists($"{Target}-wal"), "The copy has a write-ahead log beside it.");
        Assert.Equal(["Flyt til NAS'en"], Query(Target, "SELECT Title FROM Tasks"));
        Assert.Equal(["jira.projectKey"], Query(Target, "SELECT Key FROM Settings ORDER BY Key"));

        // And not in the file's free space either: a deleted row stays on its page until something
        // overwrites it, so a query that finds no token says nothing about the bytes.
        var bytes = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(Target));
        Assert.DoesNotContain("a-jira-pat", bytes);
        Assert.DoesNotContain("an-ado-pat", bytes);

        // And the source keeps what it had: the export is a copy, not a clean-up.
        Assert.Contains("jira.token", Query(Source, "SELECT Key FROM Settings"));
    }

    [Fact]
    public async Task An_existing_file_is_never_overwritten()
    {
        await using (await RunningHost.StartAtAsync(Source))
        {
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        await File.WriteAllTextAsync(Target, "somebody's file");

        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseExport.ExportAsync(Source, Target));
        Assert.Equal("somebody's file", await File.ReadAllTextAsync(Target));
    }

    // The copy is whole before the tokens are taken out of it, so a failure in between must not leave it
    // under the name the user is told to carry to the NAS. Provoked with a database without a Settings
    // table: the VACUUM INTO succeeds and the DELETE does not.
    [Fact]
    public async Task An_export_that_fails_halfway_leaves_no_file_behind()
    {
        Execute(Source, "CREATE TABLE Tasks (Title TEXT); INSERT INTO Tasks VALUES ('a-jira-pat');");

        await Assert.ThrowsAsync<SqliteException>(() => DatabaseExport.ExportAsync(Source, Target));

        Assert.False(File.Exists(Target), "The half-made copy was left under the target's name.");
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Target)!));
    }

    private static void Execute(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Query(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
