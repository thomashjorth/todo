# Personlig todo-app — design

Dato: 2026-08-13
Status: valideret, klar til implementeringsplan

## 1. Formål

Én skærm der svarer på "hvad haster for mig lige nu?" på tværs af egne opgaver,
Jira-issues tildelt mig, Azure DevOps work items tildelt mig, @mentions i
work item-kommentarer, og actions tildelt mig i en retro-board-eksport.
Enkeltbruger, kører lokalt på Windows.

Uden for scope: deling, samarbejde, mobil, tilbageskrivning til Jira/ADO.

## 2. Beslutninger

| Emne | Valg |
| --- | --- |
| Sync-retning | Læs eksternt, berig lokalt. Eksterne felter er read-only. |
| Platform | Photino.NET-vindue + ASP.NET Core i én proces, Angular som UI. |
| Hovedvisning | Én liste sorteret efter deadline, med tidssektioner. |
| Vinduesbredde | Primært mål ~480 px (kvart 1080p-skærm). Skal forblive brugbar ved fuld bredde. |
| Styling | Standard Tailwind utility-klasser. Ingen egne CSS/SCSS-regler, ingen egne tokens. |
| Tilgængelighed | WCAG AA i begge temaer. Hver handling skal kunne nås med tastaturet. |
| Genveje | Hold **Alt** for at vise genvejene på knapperne — Windows-konventionen. |
| Dark mode | Følger Windows via `prefers-color-scheme`. Ingen knap, intet at gemme. |
| Database | SQLite via EF Core, code-first med migrationer der køres ved opstart. |
| Tokens | I `Setting`-tabellen i klartekst. **Bevidst valg** — se afsnit 3. |
| Indstillinger | Én side i appen til sprog, URL'er, tokens, aliaser og sync-interval. |
| Sprog | Dansk og engelsk med Transloco. Systemets sprog som standard, skiftes i indstillinger. |
| API-fejl | `{ code, message }` — `code` er en oversættelsesnøgle, `message` engelsk fallback til logs. |
| Kodeorganisering | Feature-mapper, én type pr. fil (også enums), namespaces følger mapper. |
| E2E-opsætning | Testdata-builders i `Todo.TestSupport`; navigation kædes via `TodoApp`. |
| Retro-board | Indsat CSV-eksport. Afstemningskort filtreres væk; rækker hvor `Action Owner` matcher mine aliaser er forudvalgt. |
| Underopgaver | Tjekliste under opgaven: titel + flueben, ét niveau. |
| Redigering | Inline i listen. Ingen dialog — ved 480 px ville den dække alt. |
| Færdige opgaver | Forsvinder straks; en "Vis færdige"-kontakt henter dem frem. |
| Angular state | Signal-baseret store-service. **Ikke NgRx** — bevidst fravalg. |
| Mentions | Indbakke der godkendes manuelt, ikke automatisk opgaveoprettelse. |
| Livscyklus | Frakoblede items markeres færdige og bevares med lokale felter. |
| Kørsel | Tray-ikon, baggrundssync, Windows-notifikationer. |
| Jira | Data Center / Server (`support.edora.dk`), REST v2, PAT som Bearer. |
| Azure DevOps | Azure DevOps Server (on-prem), PAT. |
| API-kontrakt | Contract-first: `contracts/openapi.yaml` er sandheden. |
| Kodegenerering | NSwag → C#-DTO'er + Angular-klient. Genereret kode committes. |
| Serverside | Håndskrevne minimal APIs + drift-test mod kontrakten. |
| Kilder | Registreres eksplicit. Ingen auto-discovery (bevidst fravalgt). |

## 3. Arkitektur

Én proces. Kestrel lytter kun på loopback med en tilfældig port; Photino åbner et
native vindue mod den. Angular serveres som statiske filer fra `wwwroot`.

```
C:\privat-git\todo\
  contracts\openapi.yaml
  src\
    Todo.Contracts\      Genererede DTO'er fra openapi.yaml
    Todo.Core\           Tasks\ Retro\ Settings\ Persistence\ Time\
    Todo.Host\           Program, TodoHost, Endpoints\
    Todo.Web\            Angular (standalone components, signals, Tailwind)
  tests\
    Todo.TestSupport\    RunningHost, RepoPaths, Builders\, Time\
    Todo.Core.Tests\     Unit — ingen I/O
    Todo.Api.Tests\      Rigtig host, rigtig database, rigtig HTTP
    Todo.E2E\            Playwright .NET mod hosten, TodoApp + skærmobjekter
  Todo.sln
```

`Todo.Contract.Tests` med WireMock kommer først når der er en ekstern server at
lyve om — altså i skive 6 med Jira.

Udvikling: `dotnet run` + `ng serve` med proxy mod API'et, så frontend har hot
reload. Publish: `ng build` → `Todo.Host/wwwroot`, derefter self-contained exe.

**Data** i `%APPDATA%\EdoraTodo\todo.db` (SQLite/EF Core), aldrig i programmappen.
**Tokens og konfiguration** i `Setting`-tabellen i databasen, redigerbart fra
indstillingssiden. Frontend får aldrig et token at se — den gemmer det, og derefter
er det kun C# der lægger det på udgående kald.

**Tokens gemmes i klartekst. Det er et bevidst valg, truffet 2026-08-14.**
Alternativet var Windows Credential Manager, og afvejningen blev lagt frem: ét sted
at administrere alt, mod at to arbejdstokens ligger læsbare i en fil. Konsekvenser
der følger med, og som skal stå her frem for at blive genopdaget:

- `%APPDATA%\EdoraTodo\todo.db` indeholder adgang til Jira og Azure DevOps. Mappen
  bør ikke synkroniseres til OneDrive eller lignende.
- `todo.db.bak-*` fra migreringerne er fulde kopier og dermed lige så hemmelige.
- Databasefilen må aldrig vedhæftes en fejlrapport eller lægges i et repo.

`ICredentialStore` beholdes som interface med en databasebaseret implementering.
Skifter beslutningen senere, udskiftes én klasse frem for hvert kaldested.

Hosten skal kunne starte **uden vindue** (`--headless`), så Playwright kan ramme
Kestrel direkte. Det er et krav fra dag ét, ikke en senere tilføjelse.

## 4. Datamodel

Regel: synkronisering må kun skrive i `Ext*`-felter. Dine egne felter røres aldrig.

### `TaskItem`

Bygget i skive 1: `Id`, `SourceId`, `Title`, `Note`, `Deadline` (`DateOnly?`),
`Requester`, `Status` (`TodoStatus`, gemt som tekst), `CompletedAt`, `CreatedAt`,
`SubTasks`. Tilføjet i skive 2: `ExternalKey` (nullable, indekseret sammen med
`SourceId`) — nøglen en importeret række genkendes på.

Resten af felterne herunder findes **endnu ikke** — de kommer sammen med de
eksterne API-kilder, ikke før: `SortOrder`, `TitleOverridden`, `ExternalUrl`,
`ExtTitle`, `ExtStatus`, `ExtAssignee`, `ExtRequester`, `LastSyncedAt`,
`DetachedAt`, `DetachedReason`.

`Title` og `Requester` fødes fra kilden ved import. Retter du dem, sætter appen
`TitleOverridden` og holder fingrene fra dem derefter. **Deadline ejes altid
lokalt**: Jiras due date foreslås ved import, men overskrives aldrig af sync.

`SourceId` er en streng, ikke et enum, så en ny kilde ikke kræver en migrering.
I dag står der `manual` i hver eneste række.

### `SubTask`

`TaskItemId` (FK, cascade delete), `Title`, `IsDone`, `SortOrder`.

En underopgave er en tjeklistelinje: titel og flueben, ét niveau, ingen egen
deadline og ingen egen plads i deadline-sektionerne. Forælderen viser fremdrift
som "2/5". **Flueben ved alle underopgaver afslutter ikke forælderen** — det
gør du selv; automatikken rammer forkert, første gang en tjekliste ikke er
udtømmende.

### `Mention`

`WorkItemId`, `CommentId` (unikt indeks — dedup gratis), `Author`, `Text`,
`CreatedAt`, `State` (Ny / Accepteret / Afvist), `TaskItemId?`.

### `SyncState`

Pr. kilde: `LastRunAt`, `LastSuccessAt`, `Watermark`, `LastError`. Driver
"sidst opdateret"-visningen og gør inkrementel mention-hentning mulig.

### `Setting`

Nøgle/værdi: sprog, URL'er, bruger-id pr. system, sync-interval **og tokens**.
Findes endnu ikke — bygges i skive 3 sammen med indstillingssiden.

`UserAlias` er bevidst en egen tabel og flytter ikke ind her: aliaser er en liste,
ikke en enkelt værdi, og en typet tabel kan have et unikt indeks.

Tidsstempler gemmes i UTC og vises i lokal tid. Deadline er en dato uden klokkeslæt.
"Denne uge" regnes fra mandag (da-DK).

### Persistering

SQLite via EF Core, **code-first med migrationer**. Contract-first betaler sig for
API'et, fordi kontrakten har to forbrugere der ikke kan se hinandens typer; skemaet
har kun én, så et separat SQL-skema ville være en kopi der kan komme ud af trit.

Fire ting følger af at SQLite er valgt, og de skal gøres rigtigt fra skive 1:

- **Migrationer køres ved opstart** (`Database.MigrateAsync()`). Der er ingen deploy
  og ingen DBA — brugeren dobbeltklikker på en exe, og skemaet skal være rigtigt
  bagefter. `EnsureCreated()` må aldrig bruges: den springer
  `__EFMigrationsHistory` over, og næste migration fejler på en database der
  "allerede findes".
- **`todo.db` kopieres til `todo.db.bak-<tidsstempel>` før migrationer køres.**
  Dataene findes kun på maskinen — ingen server, ingen replika, intet natligt
  backup. En mislykket migration er permanent tab.
  **Der skal køres `PRAGMA wal_checkpoint(TRUNCATE)` først.** I WAL-tilstand ligger
  de nyeste skrivninger i `todo.db-wal`, ikke i `todo.db`; en kopi af `.db` alene
  er en tom header. Det blev opdaget i skive 2 ved at måle på en rigtig backup:
  4 KB kopieret, 103 KB data efterladt.
- **Datoer er `DateTime` i UTC, ikke `DateTimeOffset`.** SQLite har ikke rigtige
  typer; EF Core lagrer `DateTimeOffset`, `decimal`, `TimeSpan` og `ulong` som
  tekst, og sortering og sammenligning på dem er ikke korrekt.
- **WAL-tilstand slås til.** Baggrundssyncen skriver, mens UI'et læser; uden WAL
  giver det sporadiske "database is locked".

SQLite kan ikke `ALTER COLUMN`, så EF Core bygger tabellen om: opret ny, kopiér,
drop, omdøb. Det virker ved denne størrelse, men **fjernes en property, ryger
kolonnens data lydløst med** — genererede migrationer skal læses igennem, ikke
bare køres.

Værktøj: `dotnet-ef` pinnes som lokalt værktøj i `.config/dotnet-tools.json`, hvor
NSwag allerede ligger. Den globalt installerede er 7.0.16 og kan ikke håndtere
EF Core 10.

## 5. Kontrakt-pipeline

```
contracts/openapi.yaml
   ├─→ src/Todo.Contracts/Generated/*.cs
   └─→ src/Todo.Web/src/app/api/todo-client.ts
```

Genereret kode committes, så ændringer i kontrakten er synlige som diff og appen
kan bygges uden generator installeret. En test regenererer og fejler ved afvigelse.

Endpoints skrives i hånden som minimal APIs. En drift-test starter hosten, henter
dens kørende OpenAPI-dokument og sammenligner med `contracts/openapi.yaml`.
YAML'en ejer kontrakten; testen håndhæver den.

## 6. Integrationer

```csharp
ITaskSource      FetchAssignedAsync(ct) → IReadOnlyList<ExternalTask>
IMentionSource   FetchMentionsAsync(since, ct) → IReadOnlyList<ExternalMention>
```

Jira og ADO implementerer `ITaskSource`. Kun ADO implementerer `IMentionSource` —
Jira skal ikke tvinges til at kaste `NotSupportedException`.

Sync-motoren tager `IEnumerable<ITaskSource>` og kender ingen konkrete kilder.
Selve afstemningen "eksterne items ↔ `TaskItem`-rækker" er en ren funktion uden
database eller HTTP.

Autentifikation ligger i en `DelegatingHandler` pr. kilde. Tokens bag
`ICredentialStore`. Tid bag `IClock` — "overskredet" og "i dag" er forretningslogik
og må ikke afhænge af `DateTime.Now`.

### Jira Data Center

- Tildelte issues: `POST /rest/api/2/search` med JQL
  `assignee = currentUser() AND resolution = Unresolved`.
- PAT som `Authorization: Bearer`. Kræver Jira 8.14+.
- Opgavestiller = `fields.reporter.displayName`. Deadline-forslag = `fields.duedate`.

### Azure DevOps Server

- Tildelte work items: WIQL på `[System.AssignedTo] = @Me`, derefter batch-hent.
- PAT som Basic auth (tomt brugernavn).
- API-version afhænger af serverudgaven og skal verificeres.
- Opgavestiller = `System.CreatedBy`.

### Mentions (usikker del)

Azure DevOps har intet "vis mine mentions"-endpoint. Planen er WIQL med
`[System.History] Contains Words '<dit navn>'` afgrænset af `System.ChangedDate`
siden sidste watermark, derefter hent kommentarerne på de fundne work items og
match på mention-markup. **Skal verificeres mod jeres egen instans, tidligt.**
Falder den, er alternativet at læse ADO's notifikations-/alert-feed.

### Retro-board (indsat CSV)

Ikke en `ITaskSource` — der er intet API og ingen polling. Du indsætter en
board-eksport i et tekstfelt, og appen parser den. Kolonnerne er:

```csv
"Content","Author","Created","Zone","Action Due Date","Action Owner"
"Since we dont have resqueue on FRH …","Thomas Hjorth Hansen","7/13/26, 4:09 PM","Add","24.7.2026","Filip Taskovski Medarbejder"
```

**Feltafbildning:** `Content` → titel, `Action Owner` → hvem rækken tilhører,
`Action Due Date` → deadline, `Author` → opgavestiller (den der rejste punktet).
`Zone` og `Created` bruges kun til dedup og til at vise kontekst.

- **Rigtig CSV-parsing** (RFC 4180, citerede felter). `split(',')` går i stykker
  første gang et kort indeholder et komma, og det gør de.
- **Afstemningskort filtreres væk.** Zonerne `Quality`, `Mood` og `Velocity`
  indeholder karakterer som `8`, `9/10`, `10/10` — i en typisk eksport er det
  størstedelen af rækkerne. De må aldrig kunne blive til opgaver.
- **Alle øvrige rækker vises, med dine forudvalgt.** Match sker på `Action Owner`
  mod en liste af aliaser i indstillingerne, ikke på `Zone`: ejer og deadline kan
  stå på et kort i en hvilken som helst zone, ikke kun `Actions`.
- **Import-skærmen skal kunne vise "ingen af dem er dine"** uden at ligne en fejl.
  Det sker, når du ikke har deltaget i den pågældende retro — eksporten ovenfor er
  netop sådan en. Skærmen må ikke antage, at der er noget forudvalgt, og skal sige
  hvorfor listen er tom frem for bare at vise ingenting.
- **To datoformater i samme fil.** `Action Due Date` er `d.M.yyyy` (`24.7.2026`),
  `Created` er `M/d/yy, h:mm tt` (`7/13/26, 4:09 PM`). Begge parses med eksplicit
  format og `InvariantCulture`. Under da-DK fejler `7/13/26` som dato, og et
  forkert format bytter stille dag og måned.
- **Ingen id-kolonne**, så dedup sker på `Content` + `Zone` + `Author` +
  `Created`. **Indhold alene er ikke nok:** samme tekst optræder både som
  observation i `Improve` og som aftalt handling i `Actions`, og de to er ikke det
  samme. Ved gen-import vises kendte rækker som "importeret tidligere" og er slået
  fra — ikke skjult, så du kan se at boardet blev genkendt.
- Matcher et alias, strippes et indledende `"NAVN - "` fra titlen; det er et
  ejerskabsmærke i boardet og larmer i en opgaveliste.

## 7. SOLID i praksis

- **SRP** — sync-motor, afstemningslogik, HTTP-klienter og persistering er
  adskilte typer. Afstemningen kender hverken database eller netværk.
- **OCP** — ny kilde = ny klasse + én DI-registrering. Sync-motoren rettes ikke.
- **LSP** — enhver `ITaskSource` skal kunne returnere en tom liste og kaste en
  fælles `SourceUnavailableException`; ingen implementering må kræve særbehandling.
- **ISP** — `IMentionSource` er skilt fra `ITaskSource`.
- **DIP** — `ICredentialStore`, `IClock` og kildeinterfaces gør al ekstern kontakt
  udskiftelig. Det er også dét, der gør E2E-mocking mulig uden hacks.

## 8. Teststrategi

1. **Unit** (`Todo.Core.Tests`) — afstemning, deadline-inddeling, mention-dedup,
   override-regler. Ingen I/O.
2. **Kontrakt** (`Todo.Contract.Tests`) — de rigtige `HttpClient`-klienter mod
   WireMock.Net med fixtures nedskrevet fra jeres *egne* servere, ikke håndskrevne
   gæt. Fanger felt- og mapping-fejl som unit tests aldrig ser.
3. **E2E** (`Todo.E2E`) — Playwright for .NET starter hosten headless med SQLite i
   en midlertidig fil og kildernes base-adresser peget mod WireMock. Testen klikker
   i den rigtige Angular-UI. Playwright .NET frem for TypeScript, fordi testen så
   kan styre host, database og fixtures i samme proces.

Fixtures saniteres for personoplysninger før de committes.

## 9. Leveranceplan

Hver skive slutter med en app der kan startes og bruges, plus grønne tests.

0. **Skelet** — solution, Photino mod Kestrel, Angular der siger goddag,
   `--headless`, kontrakt-pipeline med NSwag, én Playwright-test. **Færdig.**
1. **Egne opgaver** — CRUD med titel, note, deadline, opgavestiller, status.
   Listen med Overskredet / I dag / Denne uge / Senere / Uden deadline,
   "Vis færdige" og underopgaver som tjekliste. **Færdig.**
   *Herefter er appen brugbar; resten er tilkobling.*
2. **Retro-import** — indsat CSV, forhåndsvisning, dedup. Den eneste eksterne
   kilde der hverken kræver tokens, netværk eller kendskab til serverversioner —
   ren tekstparsing ind i skive 1's datamodel. Derfor før Jira. **Færdig.**
3. **Indstillinger** — `Setting`-tabellen og én indstillingsside. Den er lille og
   giver et hjem til sprogvalg, aliaser og senere tokens og URL'er.
   Aliasredigeringen flytter hertil fra import-skærmen, som beholder et link —
   ellers ender samme data to steder og bliver uenige med sig selv.
4. **Lokalisering** — dansk og engelsk med Transloco, systemets sprog som standard,
   skiftes i indstillinger. Alle eksisterende strenge trækkes ud, datoer formateres
   pr. locale, og API-fejl bliver til nøgler frontend kan oversætte.
5. **Tilgængelighed, tastatur og dark mode** — audit af alt eksisterende mod
   WCAG AA, kontrastrettelser, synligt fokus, Alt-genvejssystemet og
   `prefers-color-scheme`. Efter 3 og 4, så gennemgangen rammer alle skærme og alle
   strenge én gang. Samlet i én skive, fordi hver farve ellers skulle
   kontrasttjekkes to gange.
6. **Jira-import** — `ITaskSource`, afstemning, lokale felter der overlever sync.
   Her bygges også "Test forbindelse" ind i indstillingssiden, nu hvor der er
   en server at teste mod.
7. **ADO-import** — samme mønster. Her viser det sig om abstraktionen fra 6 duer.
8. **Mentions-indbakke** — WIQL, dedup, "gør til opgave". Mest usikre del, derfor sent.
9. **Baggrundssync, tray og notifikationer.**
10. **Livscyklus og arkiv** — detached-håndtering, "vis afsluttede".
11. **Pakning** — self-contained exe, autostart.

## 10. Risici og åbne punkter

- **ADO-mentions** er den mest usikre antagelse. Verificér i skive 6, ikke i 8.
- **Serverversioner** for Jira DC og ADO Server afgør endpoints og API-versioner.
  Verificeres med "Test forbindelse" i skive 6.
- **Skallen har ingen baggrundsfarve.** `<body>` sætter hverken baggrund eller
  tekstfarve, så under mørkt systemtema ville komponenternes `dark:`-farver stå på
  hvid. Skive 5 skal sætte den; indtil da undgås fyldte flader.
- **Flertalsformer i UI-tekster** er ikke håndteret — "Importér 1 opgaver" er
  bevidst efterladt, fordi Transloco løser det rigtigt i skive 4. At rette strengen
  først ville betyde at skrive den om to gange.
- **SQLite-migrationer der fjerner en kolonne** taber dens data lydløst, fordi
  tabellen bygges om. Læs genererede migrationer; backup-kopien før migrering er
  sidste forsvar.
- **Photino** kræver WebView2-runtime. Til stede på Windows 11 som standard.
- **Fixtures** kan blive forældede når serverne opgraderes; kontrakt-testene er
  første sted det opdages.
