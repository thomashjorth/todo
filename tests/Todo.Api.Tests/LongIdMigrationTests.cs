using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Tasks;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Every other test meets the LongIds migration on an empty database, where all it does is create
/// the tables. The rows it has to carry are the user's, they exist nowhere else, and a Guid cannot
/// be cast to an integer without collapsing. That is why this is the only test that stands real
/// Guid-era rows in front of it and asks what came out.
/// </summary>
public class LongIdMigrationTests
{
    /// <summary>
    /// The Guids are picked to break a CAST: two share a leading numeric prefix, one starts with a
    /// letter, one is zeroes but for its last digit. CreatedAt differs for every one of them,
    /// because the migration sorts on <c>CreatedAt, Id</c> and a tie would otherwise leave the
    /// order to the Guid text. The order here is the expected id order: the oldest becomes number
    /// 1. Note that the text runs the exact opposite way, so a migration sorting on Id alone fails.
    /// </summary>
    private static readonly (string Id, string Title, string CreatedAt)[] GuidEraTasks =
    [
        ("deadbeef-0000-0000-0000-000000000000", "Aeldste opgave", "2026-01-01 08:00:00"),
        ("11111111-2222-3333-4444-555555555555", "Delt talpraefiks", "2026-02-01 08:00:00"),
        ("a1b2c3d4-e5f6-7890-abcd-ef1234567890", "Starter med bogstav", "2026-03-01 08:00:00"),
        ("00000000-0000-0000-0000-000000000001", "Nuller og et enkelt et", "2026-04-01 08:00:00"),
        ("11111111-1111-1111-1111-111111111111", "Nyeste opgave", "2026-05-01 08:00:00"),
    ];

    /// <summary>
    /// Sub-tasks under three different parents, and with a SortOrder that does not follow the
    /// insertion order. <c>Parent</c> is the index into <see cref="GuidEraTasks"/>.
    /// </summary>
    private static readonly (int Parent, string Id, string Title, int SortOrder)[] GuidEraSubTasks =
    [
        (0, "deadbeef-1111-0000-0000-000000000000", "Aeldste: foerste", 0),
        (0, "deadbeef-2222-0000-0000-000000000000", "Aeldste: anden", 1),
        (2, "a1b2c3d4-1111-7890-abcd-ef1234567890", "Bogstav: eneste", 0),
        (4, "11111111-aaaa-1111-1111-111111111111", "Nyeste: sidst", 5),
        (4, "11111111-bbbb-1111-1111-111111111111", "Nyeste: foerst", 2),
    ];

    /// <summary>
    /// Inserted in reverse alphabetical order, because <c>Aliases</c> has no CreatedAt to sort on
    /// and the migration therefore numbers them by Value.
    /// </summary>
    private static readonly string[] GuidEraAliases = ["thomas", "anna"];

    /// <summary>The three tables the migration rebuilds. <c>Settings</c> it leaves alone.</summary>
    private static readonly string[] MigratedTables = ["Tasks", "SubTasks", "Aliases"];

    /// <summary>
    /// The same three, but as entity types rather than table names. The model knows for itself
    /// what the table is called, so the pairing type → table has to be written down nowhere and
    /// cannot go stale.
    /// </summary>
    private static readonly Type[] MigratedEntities =
        [typeof(TaskItem), typeof(SubTask), typeof(UserAlias)];

    /// <summary>
    /// One fully populated task: each of the twelve columns around <c>Id</c> carries its own
    /// unmistakable value. The distinctness is what makes this a correctness test rather than
    /// merely a presence test — with <c>"x"</c> in both <c>Note</c> and <c>Requester</c>, a
    /// migration that read the wrong source column would pass.
    /// </summary>
    private static readonly (string Column, object Value)[] FullTask =
    [
        ("SourceId", "kilde-outlook"),
        ("Title", "Titlen og ingen anden kolonnes tekst"),
        ("Note", "Noten, der kun staar i Note"),
        ("Deadline", "2026-09-30"),
        ("Requester", "Opgavestiller Rikke"),
        ("ExternalKey", "ekstern-noegle-4711"),
        ("Status", "WaitingFor"),
        ("WaitingOn", "Ventet paa Bo"),
        ("WaitingSince", "2026-07-04 09:15:00"),
        ("DeferUntil", "2026-09-01"),
        ("CompletedAt", "2026-08-11 17:45:00"),
        ("CreatedAt", "2026-06-02 07:30:00"),
    ];

    /// <summary>
    /// The sub-task's columns around <c>Id</c> and <c>TaskItemId</c>. The foreign key stands
    /// apart, because it is the one value the migration deliberately *has* to change.
    /// </summary>
    private static readonly (string Column, object Value)[] FullSubTaskWithoutParent =
    [
        ("Title", "Underopgavens egen titel"),
        ("IsDone", 1),
        ("SortOrder", 7),
    ];

    private static readonly (string Column, object Value)[] FullAlias =
    [
        ("Value", "aliasset-og-ingen-anden-tekst"),
    ];

    private const string FullTaskId = "cafebabe-0000-4000-8000-000000000001";
    private const string FullSubTaskId = "cafebabe-1111-4000-8000-000000000002";
    private const string FullAliasId = "cafebabe-2222-4000-8000-000000000003";

    /// <summary>
    /// The reason the migration is hand-written, as an assertion rather than as a comment. Five
    /// distinct Guids become two distinct integers of which three are 0 — colliding primary keys,
    /// in other words. Should this stop being true, the hand-written remap is no longer needed,
    /// and somebody ought to be told.
    /// </summary>
    [Fact]
    public void Casting_a_Guid_to_an_integer_collapses_distinct_ids()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();

        var cast = new List<long>();
        foreach (var guid in GuidEraTasks.Select(t => t.Id))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CAST($id AS INTEGER)";
            command.Parameters.AddWithValue("$id", guid);
            cast.Add(Convert.ToInt64(command.ExecuteScalar()));
        }

        Assert.Equal(5, GuidEraTasks.Select(t => t.Id).Distinct().Count());
        Assert.Equal(2, cast.Distinct().Count());
        Assert.Equal(3, cast.Count(value => value == 0));
    }

    [Fact]
    public async Task Guid_era_rows_survive_the_migration_to_long_ids()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "TodoApp.Tests", $"longids-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "todo.db");

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await RollBackLongIdsAsync(host);
            }

            // This is where the test could go green for the wrong reason. SQLite's type affinity
            // stores a Guid string in an INTEGER column as TEXT without complaining, so had the
            // rollback not happened, the seeding would succeed into the long world and the whole
            // rest of the test would measure a migration that never ran.
            Assert.Equal("TEXT", DeclaredTypeOfTaskId(databasePath));

            SeedGuidEraRows(databasePath);

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                Assert.Empty(await PendingMigrationsAsync(host));
                Assert.Equal("INTEGER", DeclaredTypeOfTaskId(databasePath));

                // 1. No row is lost. A naive CAST lets three of the five Guids become 0 and
                //    cannot keep the primary key.
                Assert.Equal(GuidEraTasks.Length, await CountAsync(host, "Tasks"));
                Assert.Equal(GuidEraSubTasks.Length, await CountAsync(host, "SubTasks"));

                // 4. No sub-task points at a parent that does not exist.
                //
                //    Measured: this one can hardly fail on its own. Foreign keys are enforced
                //    during the migration, so a remap onto a parent that does not exist is
                //    rejected on the spot with 'FOREIGN KEY constraint failed' - SQLite never
                //    lets the orphaned row into the table at all. And build the new SubTasks
                //    *without* the foreign key and foreign_key_check has no rule to test, so it
                //    reports zero as well. That is why the key's existence stands beside the
                //    check: the one says the rule is there, the other that no row breaks it.
                Assert.Equal(
                    "Tasks: TaskItemId -> Id ON DELETE CASCADE",
                    Assert.Single(await ForeignKeysOfSubTasksAsync(host)));

                var orphans = await ForeignKeyViolationsAsync(host);
                Assert.True(
                    orphans.Count == 0,
                    $"PRAGMA foreign_key_check fandt {orphans.Count} brud: "
                    + string.Join("; ", orphans));

                // 2. Every sub-task still hangs off its own parent. The pair, not the count: a
                //    remap pointing every child at one parent has the right number of them.
                var expectedPairs = GuidEraSubTasks
                    .Select(s => $"{GuidEraTasks[s.Parent].Title} -> {s.Title}")
                    .Order(StringComparer.Ordinal)
                    .ToList();
                Assert.Equal(expectedPairs, await ParentChildPairsAsync(host));

                // 3. The ids are 1..n in CreatedAt order, so the oldest task is number 1.
                var byId = await TaskTitlesByIdAsync(host);
                Assert.Equal(
                    Enumerable.Range(1, GuidEraTasks.Length).Select(i => (long)i),
                    byId.Select(row => row.Id));
                Assert.Equal(GuidEraTasks.Select(t => t.Title), byId.Select(row => row.Title));

                // 6. The aliases survived, and their ids follow the text rather than the insertion.
                Assert.Equal(
                    GuidEraAliases.Order(StringComparer.Ordinal).ToList(),
                    await AliasValuesByIdAsync(host));

                // The rows do not merely exist, they can be used: EF materialises them through the
                // API, which raw SQL cannot prove - Status is an enum and CreatedAt a date.
                var listed = await host.Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");
                Assert.NotNull(listed);
                Assert.Equal(
                    GuidEraTasks.Select(t => t.Title).Order(StringComparer.Ordinal),
                    listed.Items.Select(item => item.Title).Order(StringComparer.Ordinal));

                // 5. The sequence carries on over a populated database. No other test can see
                //    that: they all start on an empty one.
                //
                //    Measured what it does *not* see: take AUTOINCREMENT out of Tasks in the
                //    migration and this one stays green, because SQLite then falls back on
                //    max(rowid) + 1 and answers 6 anyway. So it does not hold the keyword in
                //    place. What it holds is that the first id the app hands out on top of
                //    migrated rows does not collide with one of them.
                var created = await host.Client.PostAsJsonAsync(
                    "/api/tasks", new CreateTodoTaskRequest { Title = "Efter migreringen" });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var task = await created.Content.ReadFromJsonAsync<TodoTask>();
                Assert.NotNull(task);
                Assert.Equal(GuidEraTasks.Length + 1, task.Id);
            }
        }
        finally
        {
            RunningHost.ClearConnectionPoolFor(databasePath);
            TryDelete(directory);
        }
    }

    /// <summary>
    /// The migration names every column explicitly in its <c>CREATE TABLE</c> and its
    /// <c>INSERT … SELECT</c>, and SQLite drops a column without saying anything. Of this and the
    /// field-by-field assertion further down, this is the one that does not rot: it covers a
    /// column added to the model *later on* without anyone having to remember to give that column
    /// a value there. Exactly that happened to <c>DeferUntil</c>, which was found by measuring the
    /// plan again rather than by a test.
    ///
    /// Compares the *set of names*, not the order: the order of the columns after a rebuild is the
    /// order <c>CREATE TABLE</c> names them in, and pinning that would turn a harmless
    /// rearrangement red. <c>ORDER BY name</c> at both ends is what makes it a set.
    /// </summary>
    [Fact]
    public Task Every_column_of_every_table_survives_the_migration() =>
        WithFullRowsThroughTheMigrationAsync(async (host, columnsBefore) =>
        {
            foreach (var table in MigratedTables)
            {
                var before = columnsBefore[table];

                // Without this the test could pass for the wrong reason: a misspelled table name
                // gives pragma_table_info zero rows at *both* ends, and two empty sets are
                // equal.
                Assert.Contains("Id", before);

                Assert.Equal(before, await ColumnNamesAsync(host, table));
            }
        });

    /// <summary>
    /// The guard above compares the migration with itself. Its before picture is built by the
    /// migration's own <c>Down</c>, and the two bodies name the same columns — so a column missing
    /// in <em>both</em> directions is invisible to it: the two sets agree on the wrong thing. That
    /// is exactly the shape the real bug had. <c>DeferUntil</c> did not exist when the plan was
    /// written, slice 9 added it, and the hand-written SQL left it out of both halves. It was
    /// caught by measuring the plan again, not by a test.
    ///
    /// So this one asks a source other than the migration: EF's model. The database's columns have
    /// to be the ones the model expects, table by table.
    ///
    /// <c>PendingModelChangesWarning</c> does not cover it, and neither does the
    /// <c>Assert.Empty(pending)</c> above: that warning compares the model <em>snapshot</em> with
    /// the <em>model</em>, and the snapshot is generated <em>from</em> the model. They agree, even
    /// when the hand-written SQL disagrees with both of them. The database is the one nobody
    /// compares.
    ///
    /// Runs on a fresh database and has no need of the rollback: it is LongIds' <c>Up</c> that
    /// builds the three tables, whether or not there were rows in them. That makes the assertion
    /// independent of the seeding below — a field missing from the schema would otherwise topple
    /// the insertion first, and the failure would point at the test's SQL rather than at the
    /// migration's.
    /// </summary>
    [Fact]
    public async Task Every_column_the_model_expects_exists_in_the_database()
    {
        await using var host = await RunningHost.StartAsync();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        var visited = new List<string>();

        foreach (var entity in MigratedEntities)
        {
            var entityType = db.Model.FindEntityType(entity);
            Assert.NotNull(entityType);

            var table = entityType.GetTableName();
            Assert.NotNull(table);

            var expected = entityType.GetProperties()
                .Select(property => property.GetColumnName()!)
                .Order(StringComparer.Ordinal)
                .ToList();

            var actual = await ColumnNamesAsync(host, table);

            // These two do not measure the columns, but that columns *were* measured. Without
            // them the test could pass by comparing nothing with nothing: a model with no mapped
            // properties gives an empty expectation, and a table name that does not exist gives
            // pragma_table_info zero rows — two empty lists are equal.
            Assert.Contains("Id", expected);
            Assert.Contains("Id", actual);

            Assert.Equal(expected, actual);

            visited.Add(table);
        }

        // And that the loop really did reach all three tables, under the names the migration
        // writes. Otherwise an empty or half-filled MigratedEntities could turn the whole walk
        // into a no-op.
        Assert.Equal(
            MigratedTables.Order(StringComparer.Ordinal),
            visited.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The two structural assertions above cannot see a value that landed in the wrong column —
    /// every column still exists. That is why this one stands beside them: one fully populated row
    /// per table, read back and compared field by field.
    /// </summary>
    [Fact]
    public Task Every_field_of_a_fully_populated_row_survives_the_migration() =>
        WithFullRowsThroughTheMigrationAsync(async (host, _) =>
        {
            // There is only one task, so it is number 1. That is what makes the expectation for
            // the sub-task's foreign key readable below.
            Assert.Equal([1L], await QueryAsync(host, "SELECT Id FROM Tasks", r => r.GetInt64(0)));
            Assert.Equal([1L], await QueryAsync(host, "SELECT Id FROM Aliases", r => r.GetInt64(0)));

            Assert.Equal(Describe(FullTask), await FieldsAsync(host, "Tasks", FullTask));

            (string Column, object Value)[] expectedSubTask =
                [("TaskItemId", 1L), .. FullSubTaskWithoutParent];
            Assert.Equal(Describe(expectedSubTask), await FieldsAsync(host, "SubTasks", expectedSubTask));

            Assert.Equal(Describe(FullAlias), await FieldsAsync(host, "Aliases", FullAlias));
        });

    /// <summary>
    /// Rolls LongIds back, reads the column names in the Guid world, stands the fully populated
    /// rows up and lets the migration run forwards again.
    /// </summary>
    private static async Task WithFullRowsThroughTheMigrationAsync(
        Func<RunningHost, Dictionary<string, List<string>>, Task> assert)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "TodoApp.Tests", $"longids-columns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "todo.db");

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await RollBackLongIdsAsync(host);
            }

            // The same guard as in the test above, for the same reason: SQLite's type affinity
            // will happily take a Guid string into an INTEGER column, so had the rollback not
            // happened, everything below would measure a migration that never ran.
            Assert.Equal("TEXT", DeclaredTypeOfTaskId(databasePath));

            var columnsBefore = ColumnNamesBeforeTheMigration(databasePath);

            SeedFullRows(databasePath);

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                Assert.Empty(await PendingMigrationsAsync(host));
                Assert.Equal("INTEGER", DeclaredTypeOfTaskId(databasePath));

                await assert(host, columnsBefore);
            }
        }
        finally
        {
            RunningHost.ClearConnectionPoolFor(databasePath);
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Reads the column names over a raw connection, because no host is running at that point —
    /// the migration has been rolled back and the next host would run it forwards again.
    /// </summary>
    private static Dictionary<string, List<string>> ColumnNamesBeforeTheMigration(
        string databasePath)
    {
        using var connection = OpenOutsideThePool(databasePath);

        return MigratedTables.ToDictionary(
            table => table,
            table =>
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    $"SELECT name FROM pragma_table_info('{table}') ORDER BY name";

                using var reader = command.ExecuteReader();
                var names = new List<string>();
                while (reader.Read())
                {
                    names.Add(reader.GetString(0));
                }

                return names;
            });
    }

    private static Task<List<string>> ColumnNamesAsync(RunningHost host, string table) =>
        QueryAsync(
            host,
            $"SELECT name FROM pragma_table_info('{table}') ORDER BY name",
            read => read.GetString(0));

    /// <summary>
    /// Builds the INSERT out of the column lists, so a column added to a list is seeded too
    /// without the SQL having to be corrected in a second place.
    /// </summary>
    private static void SeedFullRows(string databasePath)
    {
        using var connection = OpenOutsideThePool(databasePath);

        InsertRow(connection, "Tasks", FullTaskId, FullTask);
        InsertRow(
            connection,
            "SubTasks",
            FullSubTaskId,
            [("TaskItemId", FullTaskId), .. FullSubTaskWithoutParent]);
        InsertRow(connection, "Aliases", FullAliasId, FullAlias);
    }

    private static void InsertRow(
        SqliteConnection connection,
        string table,
        string id,
        (string Column, object Value)[] fields)
    {
        var columns = new[] { "Id" }.Concat(fields.Select(f => f.Column)).ToList();
        var placeholders = columns.Select((_, i) => $"$p{i}").ToList();

        var parameters = new[] { ("$p0", (object)id) }
            .Concat(fields.Select((f, i) => ($"$p{i + 1}", f.Value)))
            .ToArray();

        Execute(
            connection,
            $"INSERT INTO {table} ({string.Join(", ", columns)}) "
            + $"VALUES ({string.Join(", ", placeholders)});",
            parameters);
    }

    /// <summary>
    /// Reads exactly the columns that were seeded and formats them as <c>Column = value</c>, so a
    /// failure points at which column carried the wrong value rather than at an index.
    /// </summary>
    private static async Task<List<string>> FieldsAsync(
        RunningHost host, string table, (string Column, object Value)[] fields)
    {
        var columns = fields.Select(f => f.Column).ToList();

        var rows = await QueryAsync(
            host,
            $"SELECT {string.Join(", ", columns)} FROM {table}",
            read => columns.Select((column, i) => $"{column} = {Format(read.GetValue(i))}").ToList());

        return Assert.Single(rows);
    }

    private static List<string> Describe((string Column, object Value)[] fields) =>
        [.. fields.Select(f => $"{f.Column} = {Format(f.Value)}")];

    /// <summary>
    /// A lost column value comes back as <see cref="DBNull"/>, and has to be told apart from the
    /// empty string in the failure text.
    /// </summary>
    private static string Format(object value) =>
        value is DBNull ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture)!;

    /// <summary>
    /// Runs LongIds' Down for real and removes its history row, so the database stands in the Guid
    /// world and the next start-up finds exactly one migration pending.
    /// </summary>
    private static async Task RollBackLongIdsAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.EndsWith("LongIds", applied[^1], StringComparison.Ordinal);

        await db.Database.GetService<IMigrator>().MigrateAsync(applied[^2]);

        var pending = Assert.Single(await db.Database.GetPendingMigrationsAsync());
        Assert.EndsWith("LongIds", pending, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> PendingMigrationsAsync(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return [.. await db.Database.GetPendingMigrationsAsync()];
    }

    /// <summary>
    /// Stands the rows up with raw SQL. The model is <c>long</c> and cannot express a Guid-era
    /// row, so neither a builder nor the DbContext can be used here.
    /// </summary>
    private static void SeedGuidEraRows(string databasePath)
    {
        using var connection = OpenOutsideThePool(databasePath);

        foreach (var (id, title, createdAt) in GuidEraTasks)
        {
            Execute(
                connection,
                """
                INSERT INTO Tasks (Id, SourceId, Title, Status, CreatedAt)
                VALUES ($id, 'manual', $title, 'Open', $createdAt);
                """,
                ("$id", id), ("$title", title), ("$createdAt", createdAt));
        }

        foreach (var (parent, id, title, sortOrder) in GuidEraSubTasks)
        {
            Execute(
                connection,
                """
                INSERT INTO SubTasks (Id, TaskItemId, Title, IsDone, SortOrder)
                VALUES ($id, $parent, $title, 0, $sortOrder);
                """,
                ("$id", id),
                ("$parent", GuidEraTasks[parent].Id),
                ("$title", title),
                ("$sortOrder", sortOrder));
        }

        foreach (var value in GuidEraAliases)
        {
            Execute(
                connection,
                "INSERT INTO Aliases (Id, Value) VALUES ($id, $value);",
                ("$id", Guid.NewGuid().ToString()), ("$value", value));
        }
    }

    private static string DeclaredTypeOfTaskId(string databasePath)
    {
        using var connection = OpenOutsideThePool(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM pragma_table_info('Tasks') WHERE name = 'Id'";

        return (string)command.ExecuteScalar()!;
    }

    /// <summary>
    /// Pooling=False, or else a pooled connection holds the file open and the clean-up fails.
    /// Mode=ReadWrite, so a wrong moment fails loudly rather than making an empty database beside
    /// the one under test.
    /// </summary>
    private static SqliteConnection OpenOutsideThePool(string databasePath)
    {
        var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadWrite;Pooling=False");

        connection.Open();

        return connection;
    }

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static async Task<long> CountAsync(RunningHost host, string table) =>
        (await QueryAsync(host, $"SELECT COUNT(*) FROM {table}", read => read.GetInt64(0)))[0];

    private static Task<List<string>> ParentChildPairsAsync(RunningHost host) =>
        QueryAsync(
            host,
            """
            SELECT t.Title || ' -> ' || s.Title
            FROM SubTasks s JOIN Tasks t ON t.Id = s.TaskItemId
            ORDER BY 1
            """,
            read => read.GetString(0));

    private static Task<List<(long Id, string Title)>> TaskTitlesByIdAsync(RunningHost host) =>
        QueryAsync(
            host,
            "SELECT Id, Title FROM Tasks ORDER BY Id",
            read => (read.GetInt64(0), read.GetString(1)));

    private static Task<List<string>> AliasValuesByIdAsync(RunningHost host) =>
        QueryAsync(host, "SELECT Value FROM Aliases ORDER BY Id", read => read.GetString(0));

    private static Task<List<string>> ForeignKeysOfSubTasksAsync(RunningHost host) =>
        QueryAsync(
            host,
            """
            SELECT "table" || ': ' || "from" || ' -> ' || "to" || ' ON DELETE ' || on_delete
            FROM pragma_foreign_key_list('SubTasks')
            """,
            read => read.GetString(0));

    private static Task<List<string>> ForeignKeyViolationsAsync(RunningHost host) =>
        QueryAsync(
            host,
            "PRAGMA foreign_key_check",
            read => $"{read.GetValue(0)} rowid {read.GetValue(1)} -> {read.GetValue(2)}");

    /// <summary>
    /// Reads through the host's own connection, so it sees exactly the database the app sees -
    /// including what is still only in the write-ahead log.
    /// </summary>
    private static async Task<List<T>> QueryAsync<T>(
        RunningHost host, string sql, Func<SqliteDataReader, T> read)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await using var reader = (SqliteDataReader)await command.ExecuteReaderAsync();
            var rows = new List<T>();
            while (await reader.ReadAsync())
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
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
