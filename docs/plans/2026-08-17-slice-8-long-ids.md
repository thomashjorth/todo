# Skive 8 — `long` som id

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** `Guid` erstattes af `long` på `TaskItem`, `SubTask` og `UserAlias`, uden at én række af brugerens rigtige data går tabt.

**Architecture:** Migreringen skrives **i hånden**, ikke af `dotnet-ef`. En automatisk konvertering af en TEXT-kolonne til INTEGER lader SQLite kaste `CAST` efter Guid-strengene, og det ødelægger data — målt nedenfor. I stedet omdøbes de gamle tabeller til side, `ROW_NUMBER()` bygger en mapningstabel pr. tabel, de nye tabeller oprettes under de rigtige navne, og rækkerne kopieres forældre før børn. Vagten er en migreringstest der sår rigtige Guid-rækker gennem den forrige migrering og kræver dem intakte bagefter.

**Tech Stack:** EF Core 10.0.11 · SQLite 3.50.4 (via Microsoft.Data.Sqlite) · NSwag · Angular 22 signals · xunit.v3 · Playwright 1.62.0

## Hvorfor

`Guid` v4 er tilfældig. I SQLite bliver `INTEGER PRIMARY KEY` et alias for tabellens rowid, så en `long` koster ingenting i indeksplads og opslag — og **et opgavenummer kan siges højt**. "Opgave 42" er brugbart; `a1b2c3d4-e5f6-…` er det ikke.

**Bemærk premisset**, som designdokumentets afsnit 10 allerede gør: branchen gik **ikke** fra GUID til `long`. Den gik fra tilfældig v4 til tidsordnet UUIDv7 (`Guid.CreateVersion7()`, .NET 9+). Argumentet her er SQLite-specifikt og ergonomisk. **Fragmentering betyder ingenting ved denne størrelse** — det er ikke grunden, og det skal ikke bruges som grund.

Det er også det **sidste** punkt der bliver dyrere jo længere det venter. Hver skive tilføjer kode og tests der rører id'er; skive 6 og 7 lagde ni nye testfiler oveni.

## Hvad målingen viste

Målt 2026-08-17. **Ikke** ved at læse dokumentationen — den overdriver aftrykket på tre punkter, se nedenfor.

### Migreringen er den eneste farlige del, og den nemme udgave ødelægger data

SQLites `CAST` på en Guid-streng læser et ledende tal-præfiks og giver ellers `0`. Målt på fem realistiske Guid'er:

| Guid | `CAST(… AS INTEGER)` |
| --- | --- |
| `11111111-1111-1111-1111-111111111111` | `11111111` |
| `11111111-2222-3333-4444-555555555555` | `11111111` ← samme som ovenfor |
| `a1b2c3d4-e5f6-7890-abcd-ef1234567890` | `0` |
| `deadbeef-0000-0000-0000-000000000000` | `0` |
| `00000000-0000-0000-0000-000000000001` | `0` |

**Fem distinkte Guid'er blev to distinkte heltal.** Tre af dem blev `0`. En primærnøgle på den kolonne fejler — eller, hvis noget undertrykker fejlen, fletter rækker sammen i stilhed. Det er derfor migreringen skrives i hånden.

### Opskriften der virker — verificeret hele vejen

Målt med fremmednøgler **slået til** gennem hele kørslen, fordi `PRAGMA foreign_keys` er en no-op inde i en transaktion og EF Core pakker hver migrering i én:

| Kontrol | Resultat |
| --- | --- |
| Rækker bevaret | tasks 4 → 4, subtasks 4 → 4 |
| Nye id'er | `1, 2, 3, 4` i `CreatedAt`-orden — ældste opgave bliver nummer 1 |
| Forældre-barn-par | alle intakte efter ommapning |
| `PRAGMA foreign_key_check` | clean |
| Indeks genskabt | ja |
| `AUTOINCREMENT` fortsætter | næste indsættelse fik `5` |
| Cascade delete | virker stadig |

`ROW_NUMBER()` er tilgængelig (SQLite 3.50.4; vinduesfunktioner siden 3.25). `sqlite_sequence` sås automatisk af indsættelserne med eksplicitte id'er, så der skal ikke pilles ved den.

**Rækkefølgen er ikke til forhandling:** omdøb de *gamle* tabeller til side, opret de nye under de *rigtige* navne, indsæt **forældre før børn**, drop **børn før forældre**, og opret indeksene **til sidst**. Hvert led har en grund, som står i Task 3.

### `Down` kan skrives, og skal

`DatabaseBackupTests` ruller den **sidste** migrering tilbage for at fremkalde en pending migrering (`RollBackLastMigrationAsync` → `IMigrator.MigrateAsync(applied[^2])`). Kaster vores `Down`, **fejler den eksisterende test**.

SQLite har ingen `uuid()`, men idiomet med `randomblob` virker: 200 genererede værdier gav 200 distinkte, alle 36 tegn, alle parsebare som UUID, alle version 4. `Down` kan altså genopbygge TEXT-id'er — med **nye** identiteter. Det er ærligt og skal stå i migreringens kommentar: en rulning tilbage bevarer data, ikke id'er.

### Aftrykket er mindre end dokumenteret — tre rettelser

Designdokumentet siger, at skiven rører *"primærnøgler, fremmednøgler, kontrakten, begge genererede klienter, **alle builders** og **næsten hver test**"*. Målt:

| Sted | `Guid`-forekomster |
| --- | --- |
| Entiteter (`TaskItem`, `SubTask`, `UserAlias`) | **4** |
| `TaskEndpoints.cs` (rute-parametre) | **6** |
| `contracts/openapi.yaml` (`format: uuid`) | **8** — 6 stiparametre, 2 skemafelter |
| Genereret kode | `Contracts.g.cs` 2, `todo-client.ts` — regenereres |
| Tests, **id-relateret** | **11** i 5 filer |
| **Builders** | **0** |

**`TaskItemBuilder` og `UserAliases` nævner slet ikke `Guid`.** De sætter aldrig et id; `TaskItem.Id` får sin værdi af `= Guid.NewGuid()` på entiteten. Når id'et bliver databasegenereret, kræver builderne **ingen ændring**.

**"Næsten hver test" er også for stærkt.** Af de 14 `Guid`-forekomster i tests er 3 slet ikke id'er: `RunningHost` bruger `Guid.NewGuid()` til et midlertidigt filnavn, og `DatabaseBackupTests` til en midlertidig mappe og en unik titel. De skal **ikke** røres.

### `UserAlias` har intet id udadtil

Alias-API'et er `GET`/`PUT /api/retro/aliases` med en liste af **strenge**. `UserAlias.Id` optræder hverken i kontrakten, i den genererede klient eller i frontenden. Ændringen til `long` er dermed usynlig uden for entiteten og migreringen — den skal med, men den koster kun de to linjer.

### Det nuværende skema, som migreringen skal genskabe

| Tabel | Kolonner | Indeks |
| --- | --- | --- |
| `Tasks` | `Id`, `SourceId`, `Title`, `Note`, `Deadline`, `Requester`, `ExternalKey`, `Status`, `WaitingOn`, `WaitingSince`, `CompletedAt`, `CreatedAt` | `IX_Tasks_Deadline`, `IX_Tasks_SourceId_ExternalKey` |
| `SubTasks` | `Id`, `TaskItemId`, `Title`, `IsDone`, `SortOrder` | `IX_SubTasks_TaskItemId` |
| `Aliases` | `Id`, `Value` | `IX_Aliases_Value` (unik) |
| `Settings` | `Key`, `Value` | — |

`Settings` har intet `Guid` og røres ikke.

## Beslutninger

| Emne | Valg |
| --- | --- |
| Migreringen | Skrives i hånden som `migrationBuilder.Sql(...)`. **Ikke** EF's automatiske tabelombygning. |
| Id-tildeling | `ROW_NUMBER() OVER (ORDER BY CreatedAt, Id)` — ældste opgave bliver 1. Deterministisk og meningsfuld. |
| Undermappen | `ORDER BY <ny forælder-id>, SortOrder, Id`, så en opgaves underopgaver får sammenhængende numre. |
| Aliaser | `ORDER BY Value`. Rækkefølgen er vilkårlig; alfabetisk er den eneste stabile. |
| `Down` | Implementeres, med nye Guid'er. Ellers brækker `DatabaseBackupTests`. |
| Kontrakten | `type: integer, format: int64`. |
| Frontend | `string` → `number` i stores og komponenter. |
| Builders | Røres ikke. De sætter aldrig et id. |
| Vagten | En migreringstest der sår Guid-rækker gennem den forrige migrering. |

**Hvorfor `int64` og ikke `string`.** Nogle API'er sender 64-bit heltal som streng, fordi JSON-tal over 2⁵³ mister præcision i JavaScript. Det er en reel faldgrube — men den rammer ved ~9 billiarder, og en personlig opgaveliste har rowid'er der starter på 1. `int64` som tal er det der giver "opgave 42", og strengen ville tage ergonomien væk for at løse et problem denne app ikke har. **Skriv det ned frem for at genopdage det.**

## Fælder i denne skive

- **Kør aldrig noget mod `%APPDATA%\TodoApp\todo.db`.** Det er brugerens rigtige opgaver, og denne skive omskriver primærnøgler. Giv altid `--Data:Path <midlertidig fil>`.
- **`todo.db` alene er ikke databasen.** WAL betyder at de nyeste skrivninger ligger i `todo.db-wal`. `TodoDatabase.PrepareAsync` tager selv en backup før en pending migrering, laver `wal_checkpoint(TRUNCATE)` først, og **nægter at migrere** hvis loggen ikke kunne foldes ind. Rør ikke den mekanisme — den er sidste forsvar for netop denne migrering.
- **`PRAGMA foreign_keys` er en no-op inde i en transaktion.** Migreringen kan derfor ikke slå fremmednøgler fra. Derfor rækkefølgen forældre-før-børn; opskriften er målt med enforcement slået til.
- **Indeksnavne er optaget indtil de gamle tabeller er droppet.** Et indeks følger sin tabel gennem `ALTER TABLE … RENAME TO` og beholder sit navn, så `CREATE INDEX IX_Tasks_Deadline` kolliderer hvis `Tasks_old` stadig findes. Opret indeksene **efter** droppene.
- **`dotnet-ef` er `dotnet tool run dotnet-ef`, aldrig `dotnet ef`.** En global 7.0.16 ligger på maskinen og kan ikke læse en EF Core 10-model.
- **Kør scripts fra repo-roden**, ellers henter `dotnet tool restore` et andet repos værktøjer.
- **Et databasegenereret id findes først efter `SaveChanges`.** Bruger en test `task.Id` før den gemmer, får den `0`. `Host.AddAndSaveChangesAsync(...)` gemmer, så id'et er gyldigt bagefter — men et builder-objekt der aldrig blev gemt har ikke noget id længere.
- **Rør ikke de tre `Guid.NewGuid()` der ikke er id'er** — midlertidige filnavne i `RunningHost` og `DatabaseBackupTests`. At "rydde op" i dem er at ændre noget der virker.
- **Ændrer du kontrakten, så kør `scripts\generate-api.ps1`.** Ellers fejler friskheds-testen, som sammenligner en hash af `openapi.yaml`.
- **Drift-testen sammenligner kun stier og metoder.** Den fanger **ikke** at et id skiftede type. Wire-format-testene, der ser på det rå JSON, er dem der gør.

## Bevidst uden for skive 8

Ingen ny funktion. Ingen oprydning i noget der ikke er et id. Ikke `TreatWarningsAsErrors` — den intention står i slice-0-planen og peger i dag på et forkert skivenummer; den hører i et selvstændigt valg, ikke som en passager her.

Og **ingen omnummerering af skiver.** Alt-genvejene har bevidst intet nummer.

---

## Task 1: Kontrakten

**Files:**
- Modify: `contracts/openapi.yaml`
- Regenerate: `src/Todo.Contracts/Generated/`, `src/Todo.Web/src/app/api/todo-client.ts`

**Step 1: Skift de otte steder**

Otte forekomster af `format: uuid`. Seks er stiparametre (`id` på tre task-ruter, `id` + `subTaskId` på to subtask-ruter), to er skemafelter (`TodoSubTask.id`, `TodoTask.id`). Hvert sted:

```yaml
            type: string
            format: uuid
```

bliver

```yaml
            type: integer
            format: int64
```

og i skemaerne bliver

```yaml
        id: { type: string, format: uuid }
```

til

```yaml
        id: { type: integer, format: int64 }
```

Rør intet andet i kontrakten. `sourceId` og `externalKey` er strenge og bliver det.

**Step 2: Regenerér**

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

Forventet: `Contracts.g.cs` får `public long Id { get; set; }` i stedet for `System.Guid`, og `todo-client.ts` får `id: number` og `taskId: number` i stedet for `string`. **Genereret kode committes** — det er repoets valg.

**Step 3: Se at kun det forventede ændrede sig**

```
git diff --stat
```

Forventet: kontrakten, de to genererede filer, og `.source-hash`. Ændrede prettier eller NSwag hele klientfilen, så kig efter linjeskift-støj før du committer — arbejdskopien er CRLF.

Bygningen fejler nu, fordi endpoints stadig tager `Guid`. Det er forventet og rettes i Task 2.

**Step 4: Commit**

```
git add contracts src/Todo.Contracts src/Todo.Web/src/app/api
git commit -F <UTF-8-fil med beskeden>
```

Besked: `📝 Skift id-typen i kontrakten fra uuid til int64`

---

## Task 2: Entiteter, endpoints og tests

Solutionen kan ikke bygge midt i denne opgave. Det er derfor entiteter, endpoints og tests hører i **én** commit.

**Files:**
- Modify: `src/Todo.Core/Tasks/TaskItem.cs`, `src/Todo.Core/Tasks/SubTask.cs`, `src/Todo.Core/Settings/UserAlias.cs`
- Modify: `src/Todo.Host/Endpoints/TaskEndpoints.cs`
- Modify: `tests/Todo.Api.Tests/SubTaskEndpointsTests.cs`, `TaskEndpointsTests.cs`, `TaskApiTest.cs`, `ApiErrorTests.cs`
- Modify: `tests/Todo.E2E/SettingsJourneyTests.cs`

**Step 1: Entiteterne**

```csharp
public class TaskItem
{
    public long Id { get; set; }
    // resten uændret
}
```

```csharp
public class SubTask
{
    public long Id { get; set; }

    public long TaskItemId { get; set; }
    // resten uændret
}
```

```csharp
public class UserAlias
{
    public long Id { get; set; }

    public string Value { get; set; } = string.Empty;
}
```

**Fjern `= Guid.NewGuid()`.** Med `long` er `Id` databasegenereret: EF Core ser en `long`-primærnøgle, sætter `ValueGeneratedOnAdd`, og SQLite tildeler rowid ved indsættelse. Et initialiseringsudtryk ville tvinge et eksplicit id ind.

**Step 2: Endpoints**

Seks `Guid` i `TaskEndpoints.cs` bliver `long` — fem rute-parametre og `FindSubTaskAsync`. Ingen logik ændrer sig; minimal APIs binder `long` fra stien på samme måde.

**En ting at bemærke frem for at opdage:** en sti der før gav `400` på en ugyldig Guid, giver nu `400` på en ugyldig `long`. Findes der en test på formatfejl, skal dens input skifte fra "not-a-guid" til noget der ikke er et tal.

**Step 3: Tests**

De 11 id-relaterede forekomster. Mønsteret er overalt det samme: en hårdkodet Guid-streng eller et `Guid.NewGuid()` brugt som "et id der ikke findes" bliver et tal.

- `Guid.NewGuid()` som "findes ikke" → et tal der ikke findes, fx `999999`.
- En hårdkodet Guid i en URL → tallet.
- `Guid.Empty` → `0`.

**Rør ikke** `RunningHost`s midlertidige filnavn eller `DatabaseBackupTests`' mappe og unikke titel. De er ikke id'er.

**Step 4: Byg**

```
dotnet build Todo.sln
```

Forventet: lykkes. Fejler den i en fil der ikke står ovenfor, så rapportér filen — målingen fandt 11 steder, og et tolvte er værd at kende.

**Step 5: Kør de tests der kan køre**

`dotnet test Todo.sln` vil stadig fejle: modellen er `long`, men databasen har TEXT-kolonner, og der er ingen migrering endnu. **Det er forventet.** Kør den alligevel og rapportér *hvordan* den fejler — fejlteksten er nyttig, og den skal være om skemaet, ikke om typer i C#.

**Step 6: Commit**

Besked: `♻️ Skift id-typen til long i entiteter, endpoints og tests`

---

## Task 3: Migreringen

Skivens kerne. Den skrives i hånden, og den rører brugerens data.

**Files:**
- Create: `src/Todo.Core/Persistence/Migrations/<tidsstempel>_LongIds.cs` (+ `.Designer.cs`)
- Modify: `src/Todo.Core/Persistence/Migrations/TodoDbContextModelSnapshot.cs`

**Step 1: Lad EF lave stilladset**

```
dotnet tool restore
dotnet tool run dotnet-ef migrations add LongIds --project src\Todo.Core --startup-project src\Todo.Host
```

Det opdaterer `TodoDbContextModelSnapshot.cs` korrekt — det er derfor kommandoen bruges. **Men `Up` og `Down` som EF genererer dem, skal kastes væk.** EF's SQLite-udbyder løser en typeændring med en tabelombygning der kopierer TEXT ind i INTEGER, og det er præcis den `CAST` der ødelægger data.

**Step 2: Erstat `Up`**

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Hverken EF eller SQLite kan konvertere en TEXT-primærnøgle til INTEGER: en CAST af
    // en Guid-streng læser et ledende tal-præfiks og giver ellers 0. Målt på fem rigtige
    // Guid'er blev fem distinkte værdier to, hvoraf tre var 0 — altså sammenfaldende
    // primærnøgler. Derfor ommappes id'erne eksplicit her.
    //
    // Fremmednøgler er slået til hele vejen: PRAGMA foreign_keys er en no-op inde i en
    // transaktion, og EF pakker migreringen i én. Rækkefølgen er derfor bærende —
    // forældre indsættes før børn, børn droppes før forældre.
    migrationBuilder.Sql("""
        ALTER TABLE Tasks RENAME TO Tasks_old;
        ALTER TABLE SubTasks RENAME TO SubTasks_old;
        ALTER TABLE Aliases RENAME TO Aliases_old;

        CREATE TABLE _TaskIdMap (OldId TEXT NOT NULL PRIMARY KEY, NewId INTEGER NOT NULL);
        INSERT INTO _TaskIdMap (OldId, NewId)
        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) FROM Tasks_old;

        CREATE TABLE _SubTaskIdMap (OldId TEXT NOT NULL PRIMARY KEY, NewId INTEGER NOT NULL);
        INSERT INTO _SubTaskIdMap (OldId, NewId)
        SELECT s.Id, ROW_NUMBER() OVER (ORDER BY m.NewId, s.SortOrder, s.Id)
        FROM SubTasks_old s JOIN _TaskIdMap m ON m.OldId = s.TaskItemId;

        CREATE TABLE Tasks (
            Id INTEGER NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY AUTOINCREMENT,
            SourceId TEXT NOT NULL,
            Title TEXT NOT NULL,
            Note TEXT NULL,
            Deadline TEXT NULL,
            Requester TEXT NULL,
            ExternalKey TEXT NULL,
            Status TEXT NOT NULL,
            WaitingOn TEXT NULL,
            WaitingSince TEXT NULL,
            CompletedAt TEXT NULL,
            CreatedAt TEXT NOT NULL
        );

        INSERT INTO Tasks (Id, SourceId, Title, Note, Deadline, Requester, ExternalKey,
                           Status, WaitingOn, WaitingSince, CompletedAt, CreatedAt)
        SELECT m.NewId, t.SourceId, t.Title, t.Note, t.Deadline, t.Requester, t.ExternalKey,
               t.Status, t.WaitingOn, t.WaitingSince, t.CompletedAt, t.CreatedAt
        FROM Tasks_old t JOIN _TaskIdMap m ON m.OldId = t.Id;

        CREATE TABLE SubTasks (
            Id INTEGER NOT NULL CONSTRAINT PK_SubTasks PRIMARY KEY AUTOINCREMENT,
            TaskItemId INTEGER NOT NULL,
            Title TEXT NOT NULL,
            IsDone INTEGER NOT NULL,
            SortOrder INTEGER NOT NULL,
            CONSTRAINT FK_SubTasks_Tasks_TaskItemId FOREIGN KEY (TaskItemId)
                REFERENCES Tasks (Id) ON DELETE CASCADE
        );

        INSERT INTO SubTasks (Id, TaskItemId, Title, IsDone, SortOrder)
        SELECT sm.NewId, tm.NewId, s.Title, s.IsDone, s.SortOrder
        FROM SubTasks_old s
        JOIN _SubTaskIdMap sm ON sm.OldId = s.Id
        JOIN _TaskIdMap tm ON tm.OldId = s.TaskItemId;

        CREATE TABLE Aliases (
            Id INTEGER NOT NULL CONSTRAINT PK_Aliases PRIMARY KEY AUTOINCREMENT,
            Value TEXT NOT NULL
        );

        INSERT INTO Aliases (Id, Value)
        SELECT ROW_NUMBER() OVER (ORDER BY Value), Value FROM Aliases_old;

        DROP TABLE SubTasks_old;
        DROP TABLE Tasks_old;
        DROP TABLE Aliases_old;
        DROP TABLE _SubTaskIdMap;
        DROP TABLE _TaskIdMap;

        CREATE INDEX IX_Tasks_Deadline ON Tasks (Deadline);
        CREATE INDEX IX_Tasks_SourceId_ExternalKey ON Tasks (SourceId, ExternalKey);
        CREATE INDEX IX_SubTasks_TaskItemId ON SubTasks (TaskItemId);
        CREATE UNIQUE INDEX IX_Aliases_Value ON Aliases (Value);
        """);
}
```

Hvert led har en grund:

- **De *gamle* tabeller omdøbes, de nye får de rigtige navne.** Omdøbte man i stedet de nye til sidst, ville man afhænge af at SQLite omskriver fremmednøgle-referencer under `RENAME`, hvilket `legacy_alter_table` slår fra.
- **Forældre indsættes før børn**, så en fremmednøgle aldrig peger i luften — nødvendigt, fordi enforcement ikke kan slås fra inde i transaktionen.
- **Børn droppes før forældre**, af samme grund.
- **Indeksene oprettes til sidst.** Et indeks følger sin tabel gennem `RENAME` og beholder sit navn, så `IX_Tasks_Deadline` er optaget indtil `Tasks_old` er droppet.
- **Ingen `sqlite_sequence`-pillerier.** Indsættelserne med eksplicitte id'er sætter sekvensen selv; målt til at fortsætte korrekt ved næste indsættelse.

**Step 3: Skriv `Down`**

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    // En rulning tilbage bevarer rækkerne, men ikke identiteterne: de oprindelige Guid'er
    // er væk, og der genereres nye. Det er den ærlige pris, og Down findes alligevel,
    // fordi DatabaseBackupTests ruller den sidste migrering tilbage for at fremkalde en
    // pending migrering. Kastede den her, ville den test fejle.
    //
    // SQLite har ingen uuid(); randomblob-idiomet giver v4-formede værdier — målt til 200
    // distinkte ud af 200, alle parsebare som UUID.
    migrationBuilder.Sql("""
        ALTER TABLE Tasks RENAME TO Tasks_new;
        ALTER TABLE SubTasks RENAME TO SubTasks_new;
        ALTER TABLE Aliases RENAME TO Aliases_new;

        CREATE TABLE _TaskIdMap (OldId INTEGER NOT NULL PRIMARY KEY, NewId TEXT NOT NULL);
        INSERT INTO _TaskIdMap (OldId, NewId)
        SELECT Id, lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
               substr(hex(randomblob(2)), 2) || '-' ||
               substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
               '-' || hex(randomblob(6)))
        FROM Tasks_new;

        CREATE TABLE _SubTaskIdMap (OldId INTEGER NOT NULL PRIMARY KEY, NewId TEXT NOT NULL);
        INSERT INTO _SubTaskIdMap (OldId, NewId)
        SELECT Id, lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
               substr(hex(randomblob(2)), 2) || '-' ||
               substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
               '-' || hex(randomblob(6)))
        FROM SubTasks_new;

        CREATE TABLE Tasks (
            Id TEXT NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY,
            SourceId TEXT NOT NULL,
            Title TEXT NOT NULL,
            Note TEXT NULL,
            Deadline TEXT NULL,
            Requester TEXT NULL,
            ExternalKey TEXT NULL,
            Status TEXT NOT NULL,
            WaitingOn TEXT NULL,
            WaitingSince TEXT NULL,
            CompletedAt TEXT NULL,
            CreatedAt TEXT NOT NULL
        );

        INSERT INTO Tasks (Id, SourceId, Title, Note, Deadline, Requester, ExternalKey,
                           Status, WaitingOn, WaitingSince, CompletedAt, CreatedAt)
        SELECT m.NewId, t.SourceId, t.Title, t.Note, t.Deadline, t.Requester, t.ExternalKey,
               t.Status, t.WaitingOn, t.WaitingSince, t.CompletedAt, t.CreatedAt
        FROM Tasks_new t JOIN _TaskIdMap m ON m.OldId = t.Id;

        CREATE TABLE SubTasks (
            Id TEXT NOT NULL CONSTRAINT PK_SubTasks PRIMARY KEY,
            TaskItemId TEXT NOT NULL,
            Title TEXT NOT NULL,
            IsDone INTEGER NOT NULL,
            SortOrder INTEGER NOT NULL,
            CONSTRAINT FK_SubTasks_Tasks_TaskItemId FOREIGN KEY (TaskItemId)
                REFERENCES Tasks (Id) ON DELETE CASCADE
        );

        INSERT INTO SubTasks (Id, TaskItemId, Title, IsDone, SortOrder)
        SELECT sm.NewId, tm.NewId, s.Title, s.IsDone, s.SortOrder
        FROM SubTasks_new s
        JOIN _SubTaskIdMap sm ON sm.OldId = s.Id
        JOIN _TaskIdMap tm ON tm.OldId = s.TaskItemId;

        CREATE TABLE Aliases (
            Id TEXT NOT NULL CONSTRAINT PK_Aliases PRIMARY KEY,
            Value TEXT NOT NULL
        );

        INSERT INTO Aliases (Id, Value)
        SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' ||
               substr(hex(randomblob(2)), 2) || '-' ||
               substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) ||
               '-' || hex(randomblob(6))), Value
        FROM Aliases_new;

        DROP TABLE SubTasks_new;
        DROP TABLE Tasks_new;
        DROP TABLE Aliases_new;
        DROP TABLE _SubTaskIdMap;
        DROP TABLE _TaskIdMap;

        CREATE INDEX IX_Tasks_Deadline ON Tasks (Deadline);
        CREATE INDEX IX_Tasks_SourceId_ExternalKey ON Tasks (SourceId, ExternalKey);
        CREATE INDEX IX_SubTasks_TaskItemId ON SubTasks (TaskItemId);
        CREATE UNIQUE INDEX IX_Aliases_Value ON Aliases (Value);
        """);
}
```

**Step 4: Kør suiten**

```
dotnet test Todo.sln
```

Forventet: **33 Core, 109 Api, 14 E2E** — grønne igen. Alle tests kører mod en frisk midlertidig database, hvor migreringen bare opretter tomme tabeller, så det her beviser at skemaet passer til modellen — **ikke** at data overlever. Det er Task 4.

Fejler `DatabaseBackupTests`, er det `Down` der ikke kan køre. Rapportér fejlteksten.

**Step 5: Commit**

Besked: `🗃️ Migrér id'erne til long uden at tabe en række`

---

## Task 4: Vagten — data skal overleve

Task 3 beviste at skemaet passer. Den beviste **ikke** at brugerens rækker overlever, fordi en tom database ikke har noget at miste. Det gør den her.

**Files:**
- Create: `tests/Todo.Api.Tests/LongIdMigrationTests.cs`

**Step 1: Mønsteret**

`DatabaseBackupTests` har allerede maskineriet: `RollBackLastMigrationAsync` kalder `IMigrator.MigrateAsync(applied[^2])`, altså kører `Down` og fjerner historikrækken. Efter det står databasen i Guid-verdenen, og rå SQL kan indsætte Guid-rækker. Så kører `PrepareAsync`/`MigrateAsync` fremad igen, og testen kræver rækkerne intakte.

Læs `DatabaseBackupTests.cs` og genbrug dens mønstre — `RunningHost.StartAtAsync(databasePath)`, rollback-hjælperen, og den rå `SqliteConnection` med `Pooling=False` så filen kan ryddes op bagefter. **Kopiér ikke hele filen**; genbrug det der passer.

**Step 2: Hvad testen skal fastslå**

Sået gennem rå SQL, i Guid-verdenen, med bevidst ubehagelige Guid'er:

- to opgaver hvis Guid'er har **samme ledende tal-præfiks** (`11111111-1111-…` og `11111111-2222-…`)
- en opgave hvis Guid **starter med et bogstav** (`a1b2c3d4-…`)
- underopgaver under mindst to forskellige forældre
- et alias

Efter migreringen skal testen fastslå:

1. **Antallet af opgaver og underopgaver er uændret.** Det er den assertion der fanger `CAST`-sammenfaldet — tre af fem Guid'er blev `0`, så en naiv migrering taber rækker eller fejler.
2. **Hver underopgave hænger stadig på sin egen forælder.** Sammenlign par af (opgavetitel, underopgavetitel), ikke bare antal — en ommapning der peger alle børn på samme forælder har stadig det rigtige antal.
3. **Id'erne er `1..n`** og i `CreatedAt`-orden, så den ældste opgave er nummer 1.
4. **`PRAGMA foreign_key_check` er tom.**
5. **En ny opgave oprettet gennem API'et bagefter får `n+1`**, altså at sekvensen fortsatte.
6. **Aliaset er stadig der.**

Assertion 2 er den vigtigste og den nemmeste at skrive forkert. Spørg: hvad ville få den her til at fejle?

**Step 3: Se vagten fejle — og det er hele pointen**

Erstat midlertidigt ommapningen i `Up` med den naive udgave:

```sql
INSERT INTO Tasks (Id, …) SELECT CAST(t.Id AS INTEGER), … FROM Tasks_old t;
```

Byg, kør testen. Forventet: **den fejler** — på sammenfaldende primærnøgler, tabte rækker eller forældreløse underopgaver. **Rapportér fejlteksten.** Det er beviset for at migreringen i Task 3 gør noget der betyder noget, og uden det er skiven ikke vist.

Sæt ommapningen tilbage og bekræft grøn.

**Step 4: Kør suiten**

Forventet: **110 Api** (109 + denne), 33 Core, 14 E2E. Rapportér de faktiske tal.

**Step 5: Commit**

Besked: `✅ Vagt der kræver at rigtige Guid-rækker overlever migreringen`

---

## Task 5: Frontenden

**Files:**
- Modify: `src/Todo.Web/src/app/tasks/task-store.ts`, `task-list.ts`, `task-row.ts`
- Modify: `src/Todo.Web/src/app/tasks/task-store.spec.ts`, `task-list.spec.ts`

**Step 1: Typerne**

Den genererede klient har `number` efter Task 1. Det der skal følge:

- `task-store.ts` — `remove(id: number)`, `addSubTask(taskId: number, …)`, `setSubTaskDone(taskId: number, …)`, `removeSubTask(taskId: number, subTaskId: number)`.
- `task-list.ts` — `expandedId = signal<number | null>(null)` og `editingNote = signal<number | null>(null)`.

`strictTemplates` fra skive 6 fanger stederne i templaterne selv, og `[expanded]="expandedId() === task.id"` fortsætter med at virke, fordi begge sider skifter type sammen.

**Bemærk `0`.** Et rowid starter på 1, så `0` optræder ikke som et rigtigt id — men `signal<number | null>` med `null` som "ingen valgt" er stadig det rigtige valg, netop fordi `0` ellers ville være faldgruben. Brug **ikke** `0` som "ingen".

**Step 2: Fixtures i tests**

Kun tre spec-filer nævner `id`, og de tre er ikke ens. Målt:

- **`task-list.spec.ts`** har fire Guid-strenge (`'11111111-…'` til `'44444444-…'`), to underopgave-id'er `'aaaa'` og `'bbbb'`, og ét `id: 'x'` i en enkelt test. Alle bliver tal. Vælg `1`–`4` til opgaverne og fx `11`, `12` til underopgaverne, så det er til at se hvad der er hvad; `'x'` skal bare være et id der ikke kolliderer med de andre.
- **`task-store.spec.ts`** er anderledes og den nemmeste at gøre forkert: `taskIn(bucket)` bygger sit id som `` `${bucket}-1` ``, altså en **afledt streng** som `"overdue-1"`. Der findes ikke et tal der svarer til den. `taskIn` skal have distinkte tal-id'er pr. kald — enten en tæller i funktionen eller en `bucket`→tal-tabel. **De skal være distinkte**, fordi `@for (… track task.id)` og storens filtre bygger på det; giver to opgaver samme id, fejler noget et helt andet sted. Underopgave-id'erne `'sub-1'`, `'a'`, `'b'`, `'c'` bliver også tal.
- **`app.spec.ts`** har `(id: string)` i to hjælpefunktioner. **Rør dem ikke** — det er `data-testid`-vælgere, ikke entitets-id'er. At "rette" dem ville være at ændre noget der virker.

Antallet af tests ændrer sig ikke: **134**.

**Step 3: Byg og kør**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

Forventet: bygningen lykkes, **134 Vitest**. Et andet tal betyder at en test er tabt eller duplikeret.

Får du en typefejl i en template, er det `strictTemplates` der gør sit arbejde — ret typen, sæt ikke `any` ind.

**Step 4: Kør E2E**

```
dotnet test tests/Todo.E2E/Todo.E2E.csproj
```

Forventet: **14** grønne. E2E'erne kender ikke id'er — de finder rækker på tilgængeligt navn og `data-testid` — så de er den bedste kontrol på at intet gik i stykker udadtil.

**Step 5: Formatér kun de filer du har rørt**

```
npx.cmd --prefix src/Todo.Web prettier --write src/Todo.Web/src/app/tasks/task-store.ts src/Todo.Web/src/app/tasks/task-list.ts src/Todo.Web/src/app/tasks/task-row.ts src/Todo.Web/src/app/tasks/task-store.spec.ts src/Todo.Web/src/app/tasks/task-list.spec.ts
```

Aldrig hele repoet — en fuld kørsel omskriver 3810 linjer genereret klientkode. `index.html` er ikke prettier-styret.

**Step 6: Commit**

Besked: `♻️ Lad frontenden regne id'er som tal`

---

## Task 6: Dokumentation

**Files:**
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Ret aftrykket i designdokumentet**

Afsnit 9's skive 8 og afsnit 10's `long`-punkt siger, at skiven rører *"alle builders og næsten hver test"*. Målt: **builderne nævner slet ikke `Guid`** og krævede ingen ændring, og af 14 `Guid`-forekomster i tests var 3 slet ikke id'er. Ret påstanden, og skriv **hvorfor** den var forkert — at builderne aldrig satte et id, og at et databasegenereret id gør dem endnu mere uafhængige.

Marker skiven **Færdig.** i afsnit 9. Luk `long`-punktet i afsnit 10 og erstat det — som skive 6 og 7 gjorde — med ét punkt der registrerer udfaldet og lektionen: at en TEXT-til-INTEGER-konvertering i SQLite ikke er en typeændring men en ommapning, fordi `CAST` af en Guid-streng giver et tal-præfiks eller `0`.

Ret også `Id-type`-rækken i afsnit 2: den siger *"`Guid` v4 i dag. Skifter til `long` i skive 8"*.

**Step 2: `docs/HANDOFF.md`**

Tilføj skive 8 til Færdigt-tabellen. Fjern `long` som id fra "Anbefalet rækkefølge" — **den var det sidste punkt i den dyre kategori**, så hele indledningen om at noget bliver dyrere jo længere det venter skal væk eller skrives om. Det er en mærkbar ændring i dokumentets form; læs afsnittet og skriv det som det nu er sandt, frem for at klippe en linje ud.

**Step 3: `CLAUDE.md`**

Under **Datoer** eller et nyt punkt hører migreringslektionen:

> **En TEXT-til-INTEGER-konvertering i SQLite er en ommapning, ikke en typeændring.** `CAST` af
> en Guid-streng læser et ledende tal-præfiks og giver ellers `0` — målt: fem distinkte Guid'er
> blev to distinkte heltal, tre af dem `0`. Skriv sådan en migrering i hånden med en
> mapningstabel og `ROW_NUMBER()`, og lad EF's automatiske tabelombygning være.
>
> **`PRAGMA foreign_keys` er en no-op inde i en transaktion**, og EF pakker hver migrering i én.
> En tabelombygning skal derfor indsætte forældre før børn og droppe børn før forældre.
>
> **Et indeks følger sin tabel gennem `ALTER TABLE … RENAME TO` og beholder sit navn.** Opret
> indeks efter at de gamle tabeller er droppet, ellers kolliderer navnet.

Og opdatér **Testtal** til "Efter skive 8" med de tal du **målte**. Forventet: 33 Core, **110** Api, 14 E2E, 134 Vitest.

**Step 4: Commit**

Besked: `📝 Ret aftrykket for long-skiven og skriv migreringslektionen ned`

---

## Færdig når

- `TaskItem`, `SubTask` og `UserAlias` har `long Id` uden initialiseringsudtryk.
- Kontrakten siger `type: integer, format: int64`, og begge genererede klienter er regenereret og committet.
- **Migreringstesten er set fejle** med den naive `CAST`, og fejlteksten står i rapporten.
- Rækker, forældre-barn-par, id-rækkefølge, `foreign_key_check` og sekvensfortsættelse er alle fastslået af en test — ikke ved at kigge.
- `Down` kan køre, så `DatabaseBackupTests` stadig virker.
- 33 Core, 110 Api, 14 E2E, 134 Vitest — grønne, og tallene skrevet ned som målt.
- Aftrykket er rettet i designdokumentet, med begrundelsen.
- **Ingen skive er omnummereret.**

## Til næste gang

Efter denne skive er der **intet punkt tilbage der bliver dyrere af at vente**. Tilbage står Alt-genvejene (plan klar, uden nummer), revisionsloggen, "Sådan er den tænkt"-siden, Swagger-linket, og de eksterne kilder fra skive 9 og frem. Rækkefølgen er derfra et frit valg — og designdokumentets afsnit 10 har stadig ADO-mentions som den mest usikre antagelse i hele designet, som bør verificeres mod jeres egen instans, før der bygges noget ovenpå.
