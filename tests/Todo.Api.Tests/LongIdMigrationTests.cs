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
/// Alle andre tests møder LongIds-migreringen på en tom database, hvor den kun opretter tabeller.
/// Rækkerne den skal bære er brugerens, de findes ingen andre steder, og en Guid kan ikke castes
/// til et heltal uden at falde sammen. Det er derfor den eneste test, der stiller rigtige
/// Guid-rækker op foran den og spørger hvad der kom ud.
/// </summary>
public class LongIdMigrationTests
{
    /// <summary>
    /// Guid'erne er valgt for at knække en CAST: to deler et ledende talpræfiks, én starter med
    /// et bogstav, én er nuller på nær sidste ciffer. CreatedAt er forskellig for hver, fordi
    /// migreringen sorterer på <c>CreatedAt, Id</c> og et sammenfald ellers ville lade Guid-teksten
    /// afgøre rækkefølgen. Rækkefølgen her er den forventede id-rækkefølge: ældst bliver nummer 1.
    /// Bemærk at teksten går den stik modsatte vej, så en migrering der kun sorterer på Id fejler.
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
    /// Underopgaver under tre forskellige forældre, og med en SortOrder der ikke følger
    /// indsættelsen. <c>Parent</c> er indekset i <see cref="GuidEraTasks"/>.
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
    /// Indsat i omvendt alfabetisk rækkefølge, fordi <c>Aliases</c> ikke har en CreatedAt at
    /// sortere på og migreringen derfor nummererer efter Value.
    /// </summary>
    private static readonly string[] GuidEraAliases = ["thomas", "anna"];

    /// <summary>De tre tabeller migreringen bygger om. <c>Settings</c> rører den ikke.</summary>
    private static readonly string[] MigratedTables = ["Tasks", "SubTasks", "Aliases"];

    /// <summary>
    /// De samme tre, men som entitetstyper frem for tabelnavne. Modellen ved selv hvad tabellen
    /// heder, så parringen type → tabel skal ikke skrives ned nogen steder og kan ikke blive
    /// forældet.
    /// </summary>
    private static readonly Type[] MigratedEntities =
        [typeof(TaskItem), typeof(SubTask), typeof(UserAlias)];

    /// <summary>
    /// Én fuldt udfyldt opgave: hver af de tolv kolonner uden om <c>Id</c> har sin egen
    /// umiskendelige værdi. Det er distinktheden der gør det til en korrekthedstest og ikke bare
    /// en tilstedeværelsestest — stod der <c>"x"</c> i både <c>Note</c> og <c>Requester</c>, ville
    /// en migrering der læste den forkerte kildekolonne bestå.
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
    /// Underopgavens kolonner uden om <c>Id</c> og <c>TaskItemId</c>. Fremmednøglen står for sig,
    /// fordi den er den ene værdi migreringen med vilje *skal* ændre.
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
    /// Grunden til at migreringen er skrevet i hånden, som en påstand frem for en kommentar.
    /// Fem distinkte Guid'er bliver to distinkte heltal, hvoraf tre er 0 — altså sammenfaldende
    /// primærnøgler. Holder det her op med at gælde, er den håndskrevne ommapning ikke længere
    /// nødvendig, og så skal nogen få det at vide.
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

            // Det er her testen kunne blive grøn af den forkerte grund. SQLites typeaffinitet
            // gemmer en Guid-streng i en INTEGER-kolonne som TEXT uden at klage, så var
            // tilbagerulningen udeblevet, ville seedingen lykkes ind i long-verdenen og hele
            // resten af testen måle en migrering der aldrig kørte.
            Assert.Equal("TEXT", DeclaredTypeOfTaskId(databasePath));

            SeedGuidEraRows(databasePath);

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                Assert.Empty(await PendingMigrationsAsync(host));
                Assert.Equal("INTEGER", DeclaredTypeOfTaskId(databasePath));

                // 1. Ingen række er tabt. En naiv CAST lader tre af de fem Guid'er blive 0 og
                //    kan ikke overholde primærnøglen.
                Assert.Equal(GuidEraTasks.Length, await CountAsync(host, "Tasks"));
                Assert.Equal(GuidEraSubTasks.Length, await CountAsync(host, "SubTasks"));

                // 4. Ingen underopgave peger på en forælder der ikke findes.
                //
                //    Målt: den her kan næsten ikke fejle alene. Fremmednøgler er slået til under
                //    migreringen, så en ommapning til en forælder der ikke findes bliver afvist
                //    på stedet med 'FOREIGN KEY constraint failed' - SQLite lader aldrig den
                //    forældreløse række komme ind i tabellen. Og bygges den nye SubTasks *uden*
                //    fremmednøglen, har foreign_key_check intet at tjekke og melder også nul.
                //    Derfor står nøglens eksistens ved siden af gennemgangen: den ene siger at
                //    reglen er der, den anden at ingen række bryder den.
                Assert.Equal(
                    "Tasks: TaskItemId -> Id ON DELETE CASCADE",
                    Assert.Single(await ForeignKeysOfSubTasksAsync(host)));

                var orphans = await ForeignKeyViolationsAsync(host);
                Assert.True(
                    orphans.Count == 0,
                    $"PRAGMA foreign_key_check fandt {orphans.Count} brud: "
                    + string.Join("; ", orphans));

                // 2. Hver underopgave hænger stadig på sin egen forælder. Parret, ikke tallet:
                //    en ommapning der peger alle børn på én forælder har det rigtige antal.
                var expectedPairs = GuidEraSubTasks
                    .Select(s => $"{GuidEraTasks[s.Parent].Title} -> {s.Title}")
                    .Order(StringComparer.Ordinal)
                    .ToList();
                Assert.Equal(expectedPairs, await ParentChildPairsAsync(host));

                // 3. Id'erne er 1..n i CreatedAt-rækkefølge, så den ældste opgave er nummer 1.
                var byId = await TaskTitlesByIdAsync(host);
                Assert.Equal(
                    Enumerable.Range(1, GuidEraTasks.Length).Select(i => (long)i),
                    byId.Select(row => row.Id));
                Assert.Equal(GuidEraTasks.Select(t => t.Title), byId.Select(row => row.Title));

                // 6. Aliasset overlevede, og id'erne følger teksten frem for indsættelsen.
                Assert.Equal(
                    GuidEraAliases.Order(StringComparer.Ordinal).ToList(),
                    await AliasValuesByIdAsync(host));

                // Rækkerne findes ikke bare, de kan bruges: EF materialiserer dem gennem API'et,
                // hvilket rå SQL ikke kan bevise - Status er en enum og CreatedAt en dato.
                var listed = await host.Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");
                Assert.NotNull(listed);
                Assert.Equal(
                    GuidEraTasks.Select(t => t.Title).Order(StringComparer.Ordinal),
                    listed.Items.Select(item => item.Title).Order(StringComparer.Ordinal));

                // 5. Sekvensen fortsætter på en befolket database. Ingen anden test kan se det:
                //    de starter alle på en tom.
                //
                //    Målt hvad den *ikke* ser: fjerner man AUTOINCREMENT fra Tasks i migreringen,
                //    står den her stadig grøn, fordi SQLite så falder tilbage på max(rowid) + 1 og
                //    svarer 6 alligevel. Den holder altså ikke nøgleordet på plads. Den holder, at
                //    det første id appen uddeler oven på migrerede rækker ikke kolliderer med en
                //    af dem.
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
    /// Migreringen navngiver hver kolonne eksplicit i sin <c>CREATE TABLE</c> og sit
    /// <c>INSERT … SELECT</c>, og SQLite fjerner en kolonne uden at sige noget. Den her påstand er
    /// den holdbare af de to: den fanger en kolonne der bliver lagt på modellen *i fremtiden*,
    /// uden at nogen huskede at give den en værdi i testen nedenfor. Præcis det skete for
    /// <c>DeferUntil</c>, som blev opdaget ved at måle planen igen, ikke af en test.
    ///
    /// Sammenligner *navnemængden*, ikke rækkefølgen: kolonnernes orden efter en ombygning er den
    /// orden <c>CREATE TABLE</c> nævner dem i, og at pinne den ville gøre en harmløs omrokering
    /// rød. <c>ORDER BY name</c> i begge ender er det der gør det til en mængde.
    /// </summary>
    [Fact]
    public Task Every_column_of_every_table_survives_the_migration() =>
        WithFullRowsThroughTheMigrationAsync(async (host, columnsBefore) =>
        {
            foreach (var table in MigratedTables)
            {
                var before = columnsBefore[table];

                // Uden den her ville testen kunne bestå af den forkerte grund: et stavefejlet
                // tabelnavn giver pragma_table_info nul rækker i *begge* ender, og to tomme
                // mængder er ens.
                Assert.Contains("Id", before);

                Assert.Equal(before, await ColumnNamesAsync(host, table));
            }
        });

    /// <summary>
    /// Vagten ovenfor sammenligner migreringen med sig selv. Dens før-billede er lavet af
    /// migreringens egen <c>Down</c>, og de to kroppe navngiver de samme kolonner — så en kolonne
    /// der mangler i <em>begge</em> retninger er usynlig for den: de to mængder bliver enige om
    /// det forkerte. Præcis den form havde den rigtige fejl. Da planen blev skrevet fandtes
    /// <c>DeferUntil</c> ikke, skive 9 lagde den på, og den håndskrevne SQL udelod den i begge
    /// halvdele. Den blev fanget ved at måle planen igen, ikke af en test.
    ///
    /// Den her spørger derfor en anden kilde end migreringen: EF's model. Databasens kolonner skal
    /// være dem modellen forventer, tabel for tabel.
    ///
    /// <c>PendingModelChangesWarning</c> dækker det ikke, og <c>Assert.Empty(pending)</c> ovenfor
    /// gør det heller ikke: den advarsel sammenligner model-<em>snapshottet</em> med
    /// <em>modellen</em>, og snapshottet er genereret <em>ud fra</em> modellen. De er enige, også
    /// når den håndskrevne SQL er uenig med dem begge. Databasen er den ingen sammenligner.
    ///
    /// Kører på en frisk database og har ikke brug for tilbagerulningen: det er LongIds' <c>Up</c>
    /// der bygger de tre tabeller, uanset om der stod rækker i dem. Det gør påstanden uafhængig af
    /// seedingen nedenfor — et felt der mangler i skemaet vælter ellers indsættelsen først, og
    /// fejlen ville pege på testens SQL frem for på migreringens.
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

            // De to her måler ikke kolonnerne, men at der *blev* målt kolonner. Uden dem kunne
            // testen bestå ved at sammenligne ingenting med ingenting: en model uden kortlagte
            // egenskaber giver en tom forventning, og et tabelnavn der ikke findes giver
            // pragma_table_info nul rækker — to tomme lister er ens.
            Assert.Contains("Id", expected);
            Assert.Contains("Id", actual);

            Assert.Equal(expected, actual);

            visited.Add(table);
        }

        // Og at løkken faktisk nåede alle tre tabeller, med de navne migreringen skriver. Ellers
        // kunne en tom eller halv MigratedEntities gøre hele gennemgangen til en no-op.
        Assert.Equal(
            MigratedTables.Order(StringComparer.Ordinal),
            visited.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// De to strukturelle påstande ovenfor kan ikke se en værdi der landede i den forkerte kolonne —
    /// alle kolonner findes stadig. Derfor står den her ved siden af: én fuldt udfyldt række pr.
    /// tabel, læst tilbage og sammenlignet felt for felt.
    /// </summary>
    [Fact]
    public Task Every_field_of_a_fully_populated_row_survives_the_migration() =>
        WithFullRowsThroughTheMigrationAsync(async (host, _) =>
        {
            // Der er kun én opgave, så den er nummer 1. Det gør forventningen til underopgavens
            // fremmednøgle læselig nedenfor.
            Assert.Equal([1L], await QueryAsync(host, "SELECT Id FROM Tasks", r => r.GetInt64(0)));
            Assert.Equal([1L], await QueryAsync(host, "SELECT Id FROM Aliases", r => r.GetInt64(0)));

            Assert.Equal(Describe(FullTask), await FieldsAsync(host, "Tasks", FullTask));

            (string Column, object Value)[] expectedSubTask =
                [("TaskItemId", 1L), .. FullSubTaskWithoutParent];
            Assert.Equal(Describe(expectedSubTask), await FieldsAsync(host, "SubTasks", expectedSubTask));

            Assert.Equal(Describe(FullAlias), await FieldsAsync(host, "Aliases", FullAlias));
        });

    /// <summary>
    /// Ruller LongIds tilbage, læser kolonnenavnene i Guid-verdenen, stiller de fuldt udfyldte
    /// rækker op og lader migreringen køre forlæns igen.
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

            // Samme vagt som i testen ovenfor, af samme grund: SQLites typeaffinitet tager gerne
            // en Guid-streng ind i en INTEGER-kolonne, så udeblev tilbagerulningen, ville alt
            // herunder måle en migrering der aldrig kørte.
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
    /// Læser kolonnenavnene med en rå forbindelse, fordi der ikke kører nogen vært på det
    /// tidspunkt — migreringen er rullet tilbage og den næste vært vil køre den forlæns igen.
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
    /// Bygger INSERT'en ud af kolonnelisterne, så en kolonne der bliver lagt til listen også
    /// bliver seedet uden at SQL'en skal rettes to steder.
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
    /// Læser præcis de kolonner der blev seedet, og formatterer dem som <c>Kolonne = værdi</c>, så
    /// en fejl peger på hvilken kolonne der bar den forkerte værdi frem for på et indeks.
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
    /// En tabt kolonneværdi kommer tilbage som <see cref="DBNull"/>, og skal kunne skelnes fra
    /// den tomme streng i fejlteksten.
    /// </summary>
    private static string Format(object value) =>
        value is DBNull ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture)!;

    /// <summary>
    /// Kører LongIds' Down for rigtigt og fjerner dens historik-række, så databasen står i
    /// Guid-verdenen og næste opstart finder præcis én migrering i venteposition.
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
    /// Stiller rækkerne op med rå SQL. Modellen er <c>long</c> og kan ikke udtrykke en
    /// Guid-æra-række, så hverken en builder eller DbContext kan bruges her.
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
    /// Pooling=False, ellers holder en pooled forbindelse filen åben og oprydningen fejler.
    /// Mode=ReadWrite, så et forkert tidspunkt fejler højt frem for at lave en tom database
    /// ved siden af den der er under test.
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
    /// Læser gennem værtens egen forbindelse, så den ser præcis den database appen ser -
    /// inklusive det der endnu kun står i write-ahead-loggen.
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
