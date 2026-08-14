# Skive 2 — retro-import

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Indsæt en retro-board-eksport, se hvilke actions der er dine, vælg dem der skal med, og få dem ind i opgavelisten — uden dubletter når du importerer det samme board igen.

**Architecture:** Parsing er en ren funktion i `Todo.Core` uden database eller HTTP, så alle randtilfælde kan testes på millisekunder. Hosten får to endpoints — `preview` som ikke gemmer noget, og `import` som gemmer det du valgte. Angular får en anden skærm, og dermed sin første route.

**Tech Stack:** CsvHelper 33.1.0 · EF Core 10.0.11 · Angular 22 signals · Tailwind 4.3.3 · xunit.v3 · Playwright 1.62.0

**Verificeret 2026-08-14:** `CsvHelper` 33.1.0 findes på nuget.org. Datoformaterne er afprøvet med `InvariantCulture`: `d.M.yyyy` læser `24.7.2026` som 2026-07-24, og `M/d/yy, h:mm tt` læser `7/13/26, 4:09 PM` som 2026-07-13 16:09.

## Eksportens format

```csv
"Content","Author","Created","Zone","Action Due Date","Action Owner"
"Since we dont have resqueue on FRH …","Thomas Hjorth Hansen","7/13/26, 4:09 PM","Add","24.7.2026","Filip Taskovski Medarbejder"
"8","Aleksandra","7/17/26, 1:32 PM","Quality","",""
```

Feltafbildning: `Content` → titel, `Action Owner` → hvem rækken tilhører, `Action Due Date` → deadline, `Author` → opgavestiller. `Zone` og `Created` bruges kun til dedup og kontekst.

## Fem ting der styrer designet

**Afstemningskort er flertallet.** I en typisk eksport er 18 af 25 rækker karakterer som `8`, `9/10`, `10/10`. De må aldrig kunne blive til opgaver.

**Filtrér på indhold, ikke på zone.** Det er fristende at kaste `Quality`, `Mood` og `Velocity` væk, men zonenavne er board-konfigurerbare, og en rigtig kommentar i en Mood-zone er værd at beholde. Afstemningskort er derimod altid bare et tal. Reglen er derfor `^\d+(\s*/\s*\d+)?$` på indholdet.

**Ejerskab kommer fra `Action Owner`, ikke fra zonen.** Både ejer og deadline kan stå på et kort i en hvilken som helst zone — den første række i eksemplet ovenfor ligger i `Add` og har begge dele.

**To datoformater i samme fil.** `Action Due Date` er `d.M.yyyy`; `Created` er `M/d/yy, h:mm tt`. Begge parses med eksplicit format og `InvariantCulture`. Under da-DK fejler `7/13/26`, og et forkert format bytter stille dag og måned.

**Dedup kan ikke ske på indhold alene.** Teksten *"Be better at writting down information about everything (bugs, user stories)"* optræder to gange, ord for ord: én gang som observation i `Improve` skrevet af Goran, én gang som aftalt handling i `Actions` skrevet af Aleksandra. Nøglen er `Content` + `Zone` + `Author` + `Created`.

## Bevidst uden for skive 2

Redigering af en importeret række inde på import-skærmen (importér den, redigér den bagefter i listen). Sletning af aliaser fra en rigtig indstillingsside — den bygges i skive 3. Automatisk genkendelse af hvilket board en eksport kommer fra.

---

## Task 1: Skema — `ExternalKey` og aliaser

**Files:**
- Modify: `src/Todo.Core/TaskItem.cs`
- Create: `src/Todo.Core/UserAlias.cs`
- Modify: `src/Todo.Core/TodoDbContext.cs`
- Create: `src/Todo.Core/Migrations/**` (genereret)

**Step 1: Tilføj `ExternalKey` til `TaskItem`**

```csharp
    public string? ExternalKey { get; set; }
```

Feltet er den nøgle en importeret række genkendes på. Det er `null` for alt du selv har oprettet.

**Step 2: Opret `src/Todo.Core/UserAlias.cs`**

```csharp
namespace Todo.Core;

public class UserAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Value { get; set; } = string.Empty;
}
```

En egen tabel frem for en generisk `Setting`-nøgle med JSON i: aliaser er en liste af strenge, og en typet tabel kan indekseres og valideres. Den generelle `Setting`-tabel kommer i skive 3, hvor der faktisk er URL'er og intervaller at gemme.

**Step 3: Konfigurér i `TodoDbContext.OnModelCreating`**

```csharp
        task.Property(t => t.ExternalKey).HasMaxLength(200);
        task.HasIndex(t => new { t.SourceId, t.ExternalKey });

        var alias = modelBuilder.Entity<UserAlias>();
        alias.Property(a => a.Value).IsRequired().HasMaxLength(200);
        alias.HasIndex(a => a.Value).IsUnique();
```

Og `public DbSet<UserAlias> Aliases => Set<UserAlias>();`

Det sammensatte indeks på `SourceId` + `ExternalKey` er dét, dedup-opslaget bruger. Uden det skanner hver import hele tabellen.

**Step 4: Lav migrationen**

```bash
dotnet tool run dotnet-ef migrations add RetroImport --project src/Todo.Core --startup-project src/Todo.Host
```

**Læs den igennem.** Bekræft at `ExternalKey` tilføjes som nullable `TEXT`, at `UserAliases` oprettes, og at **ingen eksisterende kolonne droppes**. SQLite bygger tabellen om ved en ændring, og en droppet kolonne tager sine data med sig lydløst.

**Step 5: Verificér**

Run: `dotnet test Todo.sln`
Expected: **48 passed**, 0 warnings. Skemaændringen må ikke bryde noget.

Bekræft desuden at appen stadig starter mod en *eksisterende* database: kopiér `%APPDATA%\EdoraTodo\todo.db` til en midlertidig sti, kør hosten med `--Data:Path` mod kopien, og se at migrationen kører og at en backupfil `todo.db.bak-*` bliver skrevet ved siden af. Det er første gang backup-stien bruges i praksis.

**Step 6: Commit**

```bash
git add -A && git commit -m "🗃️ Add an external key for imported rows and a table of name aliases"
```

---

## Task 2: CSV-parseren

Skivens egentlige logik. Ingen database, ingen HTTP — derfor kan hvert randtilfælde testes direkte.

**Files:**
- Modify: `src/Todo.Core/Todo.Core.csproj`
- Create: `src/Todo.Core/RetroRow.cs`
- Create: `src/Todo.Core/RetroCsvParser.cs`
- Create: `src/Todo.Core/RetroOwnership.cs`
- Create: `tests/Todo.Core.Tests/RetroCsvParserTests.cs`
- Create: `tests/Todo.Core.Tests/RetroOwnershipTests.cs`
- Create: `tests/Todo.Core.Tests/Fixtures/retro-board.csv`

**Step 1: Pakke**

```bash
dotnet add src/Todo.Core package CsvHelper --version 33.1.0
```

Håndrullet CSV-parsing er fristende, men et retro-kort kan indeholde både komma, citationstegn og linjeskift inde i et citeret felt. Det er præcis de tilfælde en håndrullet parser fejler på, og de er svære at få øje på.

**Step 2: Læg fixturen ind**

`tests/Todo.Core.Tests/Fixtures/retro-board.csv` — den rigtige eksport, men **saniteret**: erstat kollegernes navne med opfundne (behold `Thomas Hjorth Hansen`, da testene matcher på det). Behold antallet af rækker, zonerne, begge datoformater, de tomme felter og de to rækker med identisk indhold i forskellige zoner. Marker filen som `<Content>` med `CopyToOutputDirectory=PreserveNewest` i csproj'en.

**Step 3: Skriv de fejlende tests**

`RetroCsvParserTests` skal mindst dække:

1. Fixturen giver **7 rækker** — de 18 afstemningskort er væk.
2. `8`, `9/10` og `10/10` filtreres fra; en tekstkommentar i en `Mood`-zone gør ikke.
3. `Action Due Date` `24.7.2026` bliver til `DateOnly(2026, 7, 24)`.
4. Tom `Action Due Date` giver `null` — ikke en fejl.
5. `Created` `7/13/26, 4:09 PM` bliver til 2026-07-13 16:09.
6. Titlen får sammenklappet dobbelte mellemrum: `"Multi sub system tests  FSTYR"` → `"Multi sub system tests FSTYR"`.
7. Et felt med komma i citationstegn parses som ét felt.
8. Et felt med linjeskift i citationstegn parses som ét felt.
9. **De to rækker med identisk `Content` får forskellig `DedupKey`**, fordi zone og author adskiller dem.
10. Samme række parset to gange giver **samme** `DedupKey`.
11. En CSV uden `Content`-kolonne kaster en undtagelse med en læsbar besked — ikke en `NullReferenceException`.
12. Manglende valgfri kolonner (`Action Owner`, `Action Due Date`) er i orden.

`RetroOwnershipTests`:

13. `IsOwnedBy("Thomas Hjorth Hansen", ["thomas hjorth hansen"])` er sand — match er ufølsomt for store bogstaver og omkringstående mellemrum.
14. `IsOwnedBy(null, [...])` er falsk. Et kort uden ejer tilhører ingen.
15. `StripOwnerPrefix("THOMAS - Multi sub system tests", ["Thomas Hjorth Hansen", "Thomas"])` giver `"Multi sub system tests"`.
16. `StripOwnerPrefix` rører ikke en titel hvis præfikset ikke matcher et alias.

**Step 4: Kør testene — de skal fejle**

Run: `dotnet test tests/Todo.Core.Tests`
Expected: compile error, typerne findes ikke.

**Step 5: Implementér**

`RetroRow.cs`:

```csharp
namespace Todo.Core;

public sealed record RetroRow(
    string Title,
    string? Owner,
    string? Author,
    string Zone,
    DateOnly? DueDate,
    DateTime? Created,
    string DedupKey);
```

`RetroCsvParser.cs` — krav frem for færdig kode, fordi CsvHelper's API skal læses efter:

- `public static IReadOnlyList<RetroRow> Parse(string csv)`.
- Kolonner slås op **på navn** fra headeren, ikke på position. Rækkefølgen har allerede ændret sig én gang mellem to eksporter.
- Mangler `Content`-kolonnen: kast `FormatException` med en besked der nævner hvilke kolonner der blev fundet.
- Spring rækker over hvor `Content` er tom, og hvor den trimmede værdi matcher `^\d+(\s*/\s*\d+)?$`.
- Titel: trim, og klap alle løb af whitespace sammen til ét mellemrum.
- `DueDate`: `TryParseExact` mod `["d.M.yyyy", "dd.MM.yyyy"]`, `InvariantCulture`. Mislykkes det, bliver feltet `null` — en ulæselig dato må ikke vælte hele importen.
- `Created`: `TryParseExact` mod `["M/d/yy, h:mm tt", "M/d/yyyy, h:mm tt"]`, `InvariantCulture`.
- `DedupKey`: SHA-256 over `"{content}|{zone}|{author}|{created}"` af de **rå** felter med whitespace normaliseret og små bogstaver, som hex. Brug den rå `Content` — ikke den præfiks-strippede titel. Stripning afhænger af aliaslisten, og nøglen skal være stabil selv når du redigerer dine aliaser.

`RetroOwnership.cs`:

```csharp
public static bool IsOwnedBy(string? owner, IReadOnlyCollection<string> aliases)
public static string StripOwnerPrefix(string title, IReadOnlyCollection<string> aliases)
```

`StripOwnerPrefix` fjerner et indledende `"NAVN - "` når `NAVN` matcher et alias uafhængigt af store bogstaver. Det er et ejerskabsmærke i boardet og larmer i en opgaveliste.

**Step 6: Grøn**

Run: `dotnet test Todo.sln`
Expected: 48 + de nye, 0 warnings.

**Step 7: Commit**

```bash
git add -A && git commit -m "✨ Parse retro board exports into rows, filtering out rating cards"
```

---

## Task 3: Kontrakt og endpoints

Kontrakten først. Drift-testen bliver rød — det er den røde fase.

**Files:**
- Modify: `contracts/openapi.yaml`
- Create: `src/Todo.Host/RetroEndpoints.cs`
- Modify: `src/Todo.Host/TodoHost.cs`
- Create: `tests/Todo.Api.Tests/RetroEndpointsTests.cs`

**Step 1: Udvid kontrakten**

Fire operationer, alle med tag `Retro` så de lander på én genereret `RetroClient`:

```
POST /api/retro/preview          → RetroPreviewResponse
POST /api/retro/import           → RetroImportResponse
GET  /api/retro/aliases          → RetroAliasesResponse
PUT  /api/retro/aliases          → RetroAliasesResponse
```

Skemaer, alle med `additionalProperties: false`:

- `RetroPreviewRequest`: `csv` (string, required).
- `RetroPreviewRow`: `key` (required), `title` (required), `owner?`, `author?`, `zone` (required), `deadline?` (`format: date`), `isMine` (bool, required), `alreadyImported` (bool, required).
- `RetroPreviewResponse`: `rows` (array, required), `skippedRatingCards` (int, required).
- `RetroImportRequest`: `rows` (array of `RetroImportRow`, required).
- `RetroImportRow`: `key`, `title` (required), `requester?`, `deadline?`.
- `RetroImportResponse`: `imported` (int), `skipped` (int), begge required.
- `RetroAliasesRequest` / `RetroAliasesResponse`: `aliases` (array of string, required).

`skippedRatingCards` er med, fordi en import hvor 18 af 25 rækker forsvinder skal kunne forklare sig selv. Uden tallet ligner det en parser der tabte data.

**Step 2: Regenerér og se drift-testen fejle**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **`ContractDriftTests` FEJLER** med fire manglende operationer. Noter fejlteksten.

**Step 3: Implementér**

`RetroEndpoints.cs` med `MapRetro()`, registreret i `TodoHost.Build` før `MapFallbackToFile`.

- **`preview` gemmer intet.** Den parser, slår aliaser op, sætter `isMine` med `RetroOwnership.IsOwnedBy`, strippper præfiks fra titlen, og markerer `alreadyImported` for rækker hvis `key` allerede findes som `ExternalKey` med `SourceId = "retro"`.
- **`import`** opretter kun rækker hvis `key` ikke allerede findes. `SourceId = "retro"`, `ExternalKey = key`, `Status = Open`, `CreatedAt = clock.UtcNow`. Returnerer hvor mange der blev oprettet og hvor mange der blev sprunget over.
- **Import er idempotent.** Kør den samme body to gange: anden gang er `imported: 0`, `skipped: n`. Det er den vigtigste test i tasken.
- Ugyldig CSV giver `400` med en læsbar besked, ikke `500`.
- `aliases` trimmer, fjerner tomme, og afviser dubletter uafhængigt af store bogstaver.

**Step 4: Tests**

`RetroEndpointsTests` skal mindst dække: preview markerer `isMine` korrekt ud fra aliaslisten; preview gemmer intet i databasen (assertér mod `RunningHost.Services`); import opretter opgaver med `sourceId = "retro"`, deadline og opgavestiller; anden import af samme body opretter intet; en importeret række vises som `alreadyImported` ved næste preview; ugyldig CSV giver 400; aliaser kan sættes og hentes.

**Step 5: Grøn og commit**

Run: `dotnet test Todo.sln`

```bash
git add -A && git commit -m "✨ Add retro preview and import endpoints"
```

---

## Task 4: Routing og import-skærmen

**Files:**
- Modify: `src/Todo.Web/src/app/app.routes.ts`, `app.html`
- Create: `src/Todo.Web/src/app/retro/retro-store.ts`
- Create: `src/Todo.Web/src/app/retro/retro-import.ts` og `.html`

**Step 1: Tag routeren i brug**

Appen har `app.routes.ts` og `provideRouter` fra scaffoldet, men `app.html` renderer `<app-task-list />` direkte. Skift til `<router-outlet />` med to routes: `''` → `TaskList`, `'import'` → `RetroImport`.

Der kommer tre skærme mere i senere skiver — indstillinger, mentions-indbakke og arkiv. Routeren skal ind nu, hvor der er to skærme, ikke når der er fem.

Naviger med to links øverst. Ved 465 px er der ikke plads til en menulinje: to tekstlinks side om side er nok. `data-testid="nav-tasks"` og `data-testid="nav-import"`.

**Step 2: Store**

`RetroStore` med signals: `rows`, `skippedRatingCards`, `aliases`, `error`. Metoder `preview(csv)`, `import(selectedRows)`, `loadAliases()`, `saveAliases(list)`. Al HTTP bor her; komponenten kalder aldrig klienten direkte og bruger aldrig `.subscribe()`.

**Step 3: Skærmen**

- En `<textarea>` til at indsætte eksporten. `data-testid="retro-csv"`.
- En "Analysér"-knap. `data-testid="retro-analyse"`.
- Resultatet som **kort i én kolonne**, ikke en tabel — der er ikke plads til seks kolonner ved 465 px. Hvert kort: afkrydsning, titel, og under den zone, ejer og deadline i småt. `data-testid="retro-row"`.
- Rækker hvor `isMine` er sand er **forudvalgt**. Rækker med `alreadyImported` er slået fra og mærket "importeret tidligere" — synlige, ikke skjulte, så du kan se at boardet blev genkendt.
- Under listen: "Sprang N afstemningskort over." Ellers ligner det tabte data.
- **Når intet er forudvalgt, skal skærmen sige hvorfor**: "Ingen af rækkerne har dig som ejer." Det sker hver gang du ikke har deltaget i retroen, og må ikke ligne en fejl.

**Step 4: Verificér og commit**

Byg, kør begge testsuiter, og drív siden med Playwright: indsæt fixturen, tryk Analysér, og læs hvad DOM'en viser. Rapportér antal kort og hvor mange der er forudvalgt.

```bash
git add -A && git commit -m "✨ Add a retro import screen behind the app's first route"
```

---

## Task 5: Vælg, importér og redigér aliaser

**Files:**
- Modify: `retro-store.ts`, `retro-import.ts`, `.html`

- En "Importér N opgaver"-knap, deaktiveret når intet er valgt. `data-testid="retro-import"`.
- Efter import: en kvittering — "3 importeret, 1 sprunget over" — og listen genanalyseres, så de nu står som `alreadyImported`. Ingen navigation væk; du skal kunne se at det virkede.
- En sammenklappet sektion "Hvem er du på boardet?" med aliaslisten: tilføj med Enter, fjern med et kryds. `data-testid="alias-input"`, `data-testid="alias-row"`.
- Efter at have gemt aliaser skal listen genanalyseres, så forudvalget opdateres uden at du skal indsætte CSV'en igen.

Aliaserne bor her frem for på en indstillingsside, fordi det er her du opdager at de er forkerte. Den rigtige indstillingsside kommer i skive 3.

Vitest skal dække: intet valgt giver ingen request; `alreadyImported`-rækker kan ikke vælges; forudvalg følger `isMine`.

```bash
git add -A && git commit -m "✨ Import the selected retro rows and edit your board aliases"
```

---

## Task 6: E2E og dokumentation

**Files:**
- Create: `tests/Todo.E2E/RetroImportJourneyTests.cs`
- Modify: `README.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Rejsen**

Ved 480 × 1000, mod en tom database:

1. Gå til import-skærmen via `nav-import`.
2. Indsæt en lille CSV med to rækker, hvoraf én har dig som `Action Owner`, plus ét afstemningskort.
3. Analysér → to kort vises, ét er forudvalgt, og teksten om oversprungne afstemningskort står der.
4. Importér → kvitteringen viser 1 importeret.
5. Gå til opgavelisten via `nav-tasks` → opgaven står der med sin deadline og opgavestiller.
6. Tilbage til import, analysér samme CSV igen → rækken er nu mærket som importeret tidligere og kan ikke vælges.
7. `document.documentElement.scrollWidth <= 480`.

Kun `Assertions.Expect(...)`, aldrig `Task.Delay`. Husk at `Name = "..."` matcher på **delstreng** medmindre `Exact = true` sættes — det narrede allerede en test i skive 0.

**Step 2: Se den fejle**

Bryd dedup — lad `import` oprette uanset om nøglen findes — og bekræft at rejsen fejler på skridt 6. Ret tilbage.

**Step 3: Dokumentation**

README får et kort afsnit om retro-import: hvor du finder skærmen, at aliaser afgør hvad der er dit, og at gen-import er sikker. Designdokumentet får skive 2 markeret som færdig.

**Step 4: Alt fra bunden**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Run: `dotnet test Todo.sln`
Run: `npm.cmd run test --prefix src\Todo.Web -- --watch=false`

**Step 5: Commit**

```bash
git add -A && git commit -m "✅ Cover the retro import journey end to end"
```

---

## Færdig når

- Du kan indsætte en rigtig eksport, se dine actions forudvalgt, og importere dem.
- Gen-import af samme board opretter intet nyt og siger hvorfor.
- Afstemningskort bliver aldrig til opgaver, og antallet af oversprungne vises.
- `dotnet test Todo.sln` og Vitest er grønne, 0 warnings.
- Drift-testen er set fejle i task 3, og dedup er set fejle i task 6.
- Ingen CSS- eller SCSS-regler er skrevet.

## Til skive 3

- Aliaslisten flytter til den rigtige indstillingsside, men bliver liggende i databasen.
- `Setting`-tabellen bygges der, til URL'er og sync-interval. `UserAlias` bliver stående som egen tabel.
- Importerede opgaver har nu `SourceId = "retro"`, så listen kan begynde at vise et kilde-badge — det hører til når der er mere end én kilde.
