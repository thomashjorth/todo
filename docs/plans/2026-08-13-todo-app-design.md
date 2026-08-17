# Personlig todo-app — design

Dato: 2026-08-13
Status: skive 0–8 bygget. Aktuel tilstand og næste skridt står i `docs/HANDOFF.md`;
maskinens fælder og konventioner i `CLAUDE.md` i roden.

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
| Genveje | Hold **Alt** for at vise genvejene på knapperne — Windows-konventionen, hvor tasten udfører elementets aktiveringshandling og ikke blot flytter fokus. **Leveret i skive 8.** |
| Dark mode | Følger Windows via `prefers-color-scheme`. Ingen knap, intet at gemme. |
| Database | SQLite via EF Core, code-first med migrationer der køres ved opstart. |
| Id-type | `Guid` v4. Skiftet til `long` er besluttet men **udskudt og uplaceret** — se afsnit 9 og 10. |
| Tokens | I `Setting`-tabellen i klartekst. **Bevidst valg** — se afsnit 3. |
| Indstillinger | Én side i appen til sprog, URL'er, tokens, aliaser og sync-interval. |
| Sprog | Dansk og engelsk med Transloco. Systemets sprog som standard, skiftes i indstillinger. |
| Datoformat | `Intl.DateTimeFormat` med det aktive sprog. Ikke Angulars `DatePipe` — `LOCALE_ID` bindes ved opstart og kan ikke skiftes i runtime. |
| API-fejl | `{ code, message }` — `code` er en oversættelsesnøgle, `message` engelsk fallback til logs. |
| Kodeorganisering | Feature-mapper, én type pr. fil (også enums), namespaces følger mapper. |
| E2E-opsætning | Testdata-builders i `Todo.TestSupport`; navigation kædes via `TodoApp`. |
| Retro-board | Indsat CSV-eksport. Afstemningskort filtreres væk; rækker hvor `Action Owner` matcher mine aliaser er forudvalgt. |
| Underopgaver | Tjekliste under opgaven: titel + flueben, ét niveau. |
| Noter | Fuld CommonMark, vist renderet. Klik på noten for at redigere. |
| Venter på | Egen status. Egen sektion nederst, altid synlig når den ikke er tom. |
| Someday/Maybe | Egen status. Skjult bag en kontakt, ligesom færdige. |
| Redigering | Inline i listen. Ingen dialog — ved 480 px ville den dække alt. |
| Færdige opgaver | Forsvinder straks; en "Vis færdige"-kontakt henter dem frem. |
| Angular state | Signal-baseret store-service. **Ikke NgRx** — bevidst fravalg. |
| Mentions | Indbakke der godkendes manuelt, ikke automatisk opgaveoprettelse. |
| Livscyklus | Frakoblede items markeres færdige og bevares med lokale felter. |
| Kørsel | Tray-ikon, baggrundssync, Windows-notifikationer. |
| Jira | Selvhostet Data Center / Server, REST v2, PAT som Bearer. |
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

`contracts\openapi.yaml` er desuden **indlejret i `Todo.Host`** som ressourcen
`Todo.Host.openapi.yaml` (2026-08-17) og udstilles på `/openapi/contract.yaml`, fordi
dokumentationssiden på `/scalar/` viser kontrakten selv. Filen kopieres ikke til output og
læses aldrig fra disk: en publiceret exe har ingen `contracts\`-mappe ved siden af sig. Se
afsnit 10 om hvorfor der er to OpenAPI-dokumenter.

`Todo.Contract.Tests` med WireMock kommer først når der er en ekstern server at
lyve om — altså i skive 9 med Jira.

Udvikling: `dotnet run` + `ng serve` med proxy mod API'et, så frontend har hot
reload. Publish: `ng build` → `Todo.Host/wwwroot`, derefter self-contained exe.

**Data** i `%APPDATA%\TodoApp\todo.db` (SQLite/EF Core), aldrig i programmappen.
**Tokens og konfiguration** i `Setting`-tabellen i databasen, redigerbart fra
indstillingssiden. Frontend får aldrig et token at se — den gemmer det, og derefter
er det kun C# der lægger det på udgående kald.

**Tokens gemmes i klartekst. Det er et bevidst valg, truffet 2026-08-14.**
Alternativet var Windows Credential Manager, og afvejningen blev lagt frem: ét sted
at administrere alt, mod at to arbejdstokens ligger læsbare i en fil. Konsekvenser
der følger med, og som skal stå her frem for at blive genopdaget:

- `%APPDATA%\TodoApp\todo.db` indeholder adgang til Jira og Azure DevOps. Mappen
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
I dag står der `manual` eller `retro`.

Skive 5 tilføjer `WaitingOn` (hvem du venter på) og `WaitingSince` (hvornår du
begyndte at vente), og udvider `TodoStatus` med `WaitingFor` og `Someday`.
**`WaitingOn` er ikke det samme som `Requester`** — den ene er hvem du venter på,
den anden er hvem der bad dig. De peger hver sin vej, og at slå dem sammen ville
gøre begge lister ubrugelige.

### `SubTask`

`TaskItemId` (FK, cascade delete), `Title`, `IsDone`, `SortOrder`.

En underopgave er en tjeklistelinje: titel og flueben, ét niveau, ingen egen
deadline og ingen egen plads i deadline-sektionerne. Forælderen viser fremdrift
som "2/5". **Flueben ved alle underopgaver afslutter ikke forælderen** — det
gør du selv; automatikken rammer forkert, første gang en tjekliste ikke er
udtømmende.

### Noter i markdown

`TaskItem.Note` er markdown. Den vises renderet, og et klik skifter til
redigering — ingen knap, ingen dialog. Fuld CommonMark, så tekst kopieret ind
fra Jira eller et andet værktøj ikke bliver forvansket.

Fire ting følger af det, og de er ikke valgfrie:

- **Tabeller og kodeblokke scroller inde i sig selv**, ikke på siden. Kravet om at
  siden aldrig scroller vandret ved 465 px står ved magt; det løses med
  `[&_table]:block [&_table]:overflow-x-auto` og tilsvarende for `pre` — stadig
  Tailwind-klasser, ingen CSS-fil.
- **Renderet markdown styles med `@tailwindcss/typography`** (`prose prose-sm
  dark:prose-invert`). Det er den eneste vej udenom håndskrevet CSS: man kan ikke
  sætte utility-klasser på HTML, en renderer selv genererer.
- **Links åbnes i systemets browser.** Et almindeligt link ville navigere hele
  Photino-vinduet væk til et website uden vej tilbage.
- **Sanitering sker gennem Angulars `[innerHTML]`**, som fjerner scripts og
  `javascript:`-URL'er. `dompurify` er unødvendig oveni. Det er ikke teoretisk:
  noter kan komme fra retro-import og senere fra Jira og ADO, altså tekst andre
  har skrevet.

### `Mention`

`WorkItemId`, `CommentId` (unikt indeks — dedup gratis), `Author`, `Text`,
`CreatedAt`, `State` (Ny / Accepteret / Afvist), `TaskItemId?`.

### `SyncState`

Pr. kilde: `LastRunAt`, `LastSuccessAt`, `Watermark`, `LastError`. Driver
"sidst opdateret"-visningen og gør inkrementel mention-hentning mulig.

### `Setting`

Nøgle/værdi: sprog, URL'er, bruger-id pr. system, sync-interval **og tokens**.
Bygget i skive 3 med `Key` som primærnøgle. Sproget er indtil videre den eneste nøgle,
og API'et er typet (`{ language }`), så kontrakten ikke lækker lagringsformen.

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
3. **Indstillinger og lokalisering** — `Setting`-tabellen, én indstillingsside, og
   dansk/engelsk med Transloco og systemets sprog som standard. Slået sammen, fordi
   siden ellers kun ville flytte aliasredigeringen fra import-skærmen, og tabellen
   ville blive bygget uden en eneste indstilling at gemme. Sproget er den første.
   Aliasredigeringen flytter hertil; import-skærmen beholder et link. **Færdig.**
4. **Markdown i noter** — noten på en opgave skrives i markdown og vises renderet.
   Klik på den for at redigere. Fuld CommonMark. **Færdig.**
5. **Venter på og Someday/Maybe** — to nye tilstande, så en opgave kan ligge hos
   en anden eller være parkeret uden at forurene deadline-sektionerne. Den billige
   halvdel af GTD; se afsnit 11. **Færdig.**
6. **TypeScript strict mode** — `strict` og `strictTemplates` slås til, og opgaverækken
   udtrækkes til en rigtig børnekomponent med et typet `input()`. **Færdig.**
   *Bemærk at flagene alene var gratis og ikke fangede noget: den delte
   `#taskRow`-skabelon gav konteksten typen `any`, og `strictTemplates` afstemmer ikke
   `[ngTemplateOutletContext]`. Børnekomponenten var rettelsen; se afsnit 10.*
7. **Tilgængelighed, tastatur og dark mode** — audit af alt eksisterende mod
   WCAG AA, kontrastrettelser, synligt fokus og `prefers-color-scheme`. Efter 3, 4 og 5,
   så gennemgangen rammer alle skærme, alle strenge og alle markdown-elementer én gang.
   Samlet i én skive, fordi hver farve ellers skulle kontrasttjekkes to gange.
   **Udskudt to gange** — først af markdown, så af GTD-tilstandene. **Færdig.**
   *Løftet om markdown-elementerne hviler på en test frem for en gennemlæsning: `ContrastTests`
   sår en note med overskrift, brødtekst, link, kode inline, kodeblok, punktopstilling, citat og
   tabel, og måler dem alle i **begge** farvetemaer. `@tailwindcss/typography`s egen palette holdt
   AA på begge baggrunde, så ingen farve skulle rettes for det. Hvad vagten stadig **ikke** måler,
   står i afsnit 10.*
   *Alt-genvejssystemet blev skilt ud undervejs og er nu skive 8: skiven var en audit, afgrænset af en vagt der siger hvornår den er
   færdig, mens genvejene er en ny funktion med egne designvalg. Argumentet om at
   kontrasttjekke hver farve to gange gælder farver, ikke genveje. Bemærk også, at
   tilgængelighed var lettere at måle end at gætte: kontrastvagten blev committet rød, og
   dens fejlliste blev arbejdslisten. Se afsnit 10.*
8. **Alt-genvejssystemet** — hold Alt for at se genvejene på knapperne, og Alt+bogstav
   for at aktivere dem. Indfrier løftet i afsnit 2. **Placeret her 2026-08-17**, da
   `long` som id blev udskudt og frigjorde nummeret; ingen skive blev omnummereret.
   Planen ligger i `docs/plans/2026-08-17-alt-shortcuts.md`, og skive 7's kontrastvagt
   er dens forudsætning, fordi mærkaterne er nye farver på skærmen. **Færdig.**
   *Konventionen måtte rettes undervejs, og rettelsen er pointen: den første udgave gav kun
   elementet fokus — netop som skivens egen plan foreskrev, med begrundelsen at en browser
   aktiverer et fokuseret link på Enter. Sandt, men det går uden om konventionen afsnit 2
   navngiver. En genvej udfører elementets **aktiveringshandling**: et tekstfelt får fokus,
   fordi det ikke har andet at gøre, et afkrydsningsfelt skifter, et link følges. Og fordi
   et programmatisk `click()` ikke selv flytter fokus, kalder den aktiverende gren både
   `focus()` og `click()` — Windows flytter også fokusringen. Direktivet har derfor
   `appShortcutAction="focus" | "activate"`, og fem af de seks elementer aktiverer. Fejlen blev
   fundet af brugeren, ikke af en test. Hvad genvejene stadig **ikke** vagter, står i afsnit 10.*
9. **Jira-import** — `ITaskSource`, afstemning, lokale felter der overlever sync.
   Her bygges også "Test forbindelse" ind i indstillingssiden, nu hvor der er
   en server at teste mod.
10. **ADO-import** — samme mønster. Her viser det sig om abstraktionen fra 9 duer.
11. **Mentions-indbakke** — WIQL, dedup, "gør til opgave". Mest usikre del, derfor sent.
12. **Baggrundssync, tray og notifikationer.**
13. **Livscyklus og arkiv** — detached-håndtering, "vis afsluttede".
14. **Pakning** — self-contained exe, autostart.

### Ønsket, men ikke placeret endnu

Tre ting er besluttet uden en plads i rækkefølgen. De står her frem for at blive
glemt, og de skal placeres bevidst frem for at glide ind foran de nummererede skiver.
Den fjerde, Swagger-linket, blev leveret uden for skiverne og står nedenfor som lukket.

- **`long` som id.** `Guid` v4 erstattes af `long` på `TaskItem`, `SubTask` og `UserAlias`.
  **Udskudt 2026-08-17**, efter at planen var skrevet: beslutningen står, men den fik ikke
  en plads i rækkefølgen, og Alt-genvejene overtog nummer 8. Planen ligger klar i
  `docs/plans/2026-08-17-long-ids.md` med migreringen målt igennem — se afsnit 10
  for hvorfor den migrering ikke kan overlades til EF. **Bliver dyrere for hver skive der
  lægges imellem**, og det var netop det argument der blev sat til side her; sker det igen,
  er det værd at spørge hvorfor.
- **Revisionslog med trends.** En hændelseslog ved siden af opgaverne — hvad
  ændrede sig hvornår — som kan bære spørgsmål som "hvor mange lukker jeg om ugen"
  og "hvor længe ligger noget i Venter på". Den er også fundamentet for GTD's
  ugentlige gennemgang, som appen slet ikke understøtter i dag. Største af de fire.
- **"Sådan er den tænkt"-side.** En side der beskriver brugen i GTD-termer.
  Skrives som markdown-filer pr. sprog og renderes med kæden fra skive 4 — prosa
  hører ikke hjemme i oversættelsesnøgler. **Skal også sige hvad værktøjet ikke
  gør**, ellers lover den GTD og leverer en deadline-liste; afsnit 11 er materialet.
- **Swagger-link på health-linjen — leveret uden for skiverne 2026-08-17.** Et klik ved siden af
  "API: ok" beder `/api/system/open-link` åbne `/scalar/` i systemets browser; Photino-vinduet
  navigerer aldrig selv derhen, for det har ingen vej tilbage. UI-pakken blev `Scalar.AspNetCore`,
  som lægger sin bundle i sin egen assembly og dermed virker uden netværk — påstanden om at .NET 10
  ikke har en indbygget UI holdt. **Den fik bevidst ikke et skivenummer**: én affordance, ingen
  datamodel og ingen ny skærm. Planen ligger i `docs/plans/2026-08-17-swagger-link.md`, og hvad
  arbejdet efterlod af viden står i afsnit 10.

## 10. Risici og åbne punkter

- **ADO-mentions** er den mest usikre antagelse. Verificér i skive 9, ikke i 10 —
  altså mens "Test forbindelse" bygges, ikke først når ADO-importen skal bruge den.
- **Serverversioner** for Jira DC og ADO Server afgør endpoints og API-versioner.
  Verificeres med "Test forbindelse" i skive 9.
- **Skallen har nu farver i begge temaer, og det var mere end kosmetik** (løst i skive 7).
  En `<body>` uden baggrund giver ikke blot forkerte farver — den gør hele `dark:`-systemet
  til **usynlig tekst**: `dark:text-gray-100` på hvid er 1,10:1. `<body>` har nu
  `bg-white text-gray-900 dark:bg-gray-900 dark:text-gray-100` og `scheme-light-dark`, og
  `task-list.html` og `task-row.html` har fået `dark:`-modparter så godt som overalt. Lektionen
  er, at kontrasten ikke længere holdes af øjemål: `ContrastTests` går appens **tre** skærme
  igennem i **begge** farvetemaer — `app.routes.ts` har præcis tre ruter: opgavelisten, importen
  og indstillingerne — og måler derudover det **udvidede detaljepanel** med noten, underopgaverne
  og statusvælgeren. Panelet er en tilstand på opgavelisten, ikke en fjerde skærm.
- **Én farve står tilbage uden `dark:`-modpart, og det er bevidst** (opgjort i skive 7).
  Overskredet-sektionens ramme i `task-list.html` er
  `section.bucket === overdue ? 'border-red-500' : 'border-gray-200 dark:border-gray-700'` — den
  første gren har ingen modpart, mens dens søskende har. Kontrasten er i orden: `red-500` er
  **4,65:1** på `gray-900` og 3,82:1 på hvid, altså over 3:1 for ikke-tekst i begge temaer
  (regnet ud af `node_modules/tailwindcss/theme.css`, hvor `red-500` er
  `oklch(63.7% 0.237 25.331)`). **Ret den ikke i blinde** — påstanden om en modpart til *hver*
  farve var forkert, farven er ikke. Bemærk samtidig, at flere par med vilje har **samme** værdi
  på begge sider — `border-gray-500 dark:border-gray-500` på nyt-opgave-feltet og
  `border-amber-600 dark:border-amber-600` på Venter på-sektionen — fordi den lyse side blev
  hævet for at nå 3:1. De ser overflødige ud og er det ikke.
- **Tre fejllinjer er uden for vagtens rækkevidde** (opgjort i skive 7). `settings-error`,
  `retro-error` og health-linjens `failed()`-gren kræver hver en request der fejler, og testen har
  ingen ærlig måde at provokere det. Deres farvepar er dækket andetsteds:
  `text-red-700 dark:text-red-300` gennem `alias-error`, og `text-red-600 dark:text-red-400`
  gennem den overskredne deadline og `delete-subtask`. Det er farverne der er dækket, ikke
  linjerne.
- **Ikke-tekst-kontrast er slet ikke vagtet** (opgjort i skive 7). Vagten måler tekst, så rammer
  og fokusringen hviler på måling uden for testen. Det er præcis dét hul, der lod tre fejlende
  feltrammer på importskærmen leve, indtil et review fangede dem.
- **Fokusringen er kun sikret i lyst tema** (opgjort i skive 7). `FocusTests` måler den malede
  ring på nyt-opgave-feltet og sprogvælgeren i lyst tema;
  `dark:focus-visible:outline-blue-400` er uvagtet, det samme er `outline-offset-2`, og
  aliasfeltet har ingen fokustest overhovedet.
- **Sletning af en opgave taber fokus til `<body>`** (fundet i skive 7, ikke rettet).
  Rækkens undertræ med den fokuserede knap fjernes, så Chromiums udgangspunkt for
  sekventielt fokus dør med den, og næste Tab starter forfra øverst på siden. Slettes tre
  opgaver, tabbes der gennem hele navigationen og listen tre gange. Nåelig, men dyr.
- **Escape ud af noteredigeringen taber fokusringen** til `<body>` (fundet i skive 7, ikke
  rettet). Mildere end sletningen — næste Tab fortsætter i nærheden af tekstfeltets gamle
  plads — men ringen forsvinder.
- **En færdig række har slet ingen knap**, kun et afkrydsningsfelt og et `<span>`, så en
  færdig opgave kan ikke udvides med **noget** indtastningsudstyr. Det er en observation om
  paritet mellem mus og tastatur, ikke et tastaturhul.
- **Genvejsregistret er last-writer-wins og uden afgrænsning** (opgjort i skive 8).
  `register('n', …)` to gange overskriver lydløst, og `unregister(key)` kan ikke afgøre, om
  posten stadig tilhører den der kalder. Angular ødelægger en udgående skærm **efter** at den
  indkommende har initialiseret, så gjorde to skærme krav på samme bogstav, ville den udgående
  skærms `unregister` slette den indkommendes mål, og tasten ville dø — hvilket ville ligne
  "genvejen holdt op med at virke, efter jeg navigerede". **De seks bogstaver er globalt unikke,
  og det undgår problemet helt**; det er en begrænsning der skal holdes, ikke et tilfælde.
- **Fem ting om genvejene er uvagtede** (opgjort i skive 8). `window:blur`-grenen der rydder
  `altHeld` — slettes den, ville mærkaterne blive hængende efter Alt+Tab — er der ingen test på.
  **Hvilket bogstav hver mærkat viser** er heller ikke dækket: mærkattesten tæller elementer frem
  for at læse bogstaver, og det er bevidst, fordi kontakten ved siden af V-mærkaten hedder "Vis
  færdige", så en søgning efter bogstavet ville finde et uanset om mærkaten var tegnet; men en
  mærkat der renderede `X` frem for `V`, ville bestå. `aria-keyshortcuts` asserteres ingen
  steder, og direktivet har slet ingen Vitest-spec, så E2E-testene er dens eneste dækning.
  Endelig er hverken tilbageskiftet med `Alt+V` to gange eller at `preventDefault()` faktisk
  kaldes dækket.
- **`ShortcutStore` er `providedIn: 'root'`** (opgjort i skive 8), så Vitest-specs i samme fil
  deler registret på tværs af `TestBed`-instanser. En test der asserterer, at `activate()` giver
  `false` for et uregistreret bogstav, kan bestå eller fejle afhængigt af rækkefølgen. Skriv
  ikke en der afhænger af det.
- **Appen udstiller to OpenAPI-dokumenter, og det er med vilje** (afgjort uden for skiverne
  2026-08-17). `/openapi/v1.json` er runtime-afledningen fra `MapOpenApi()`, og
  `ContractDriftTests` læser **netop den** — fjernes `MapOpenApi()`, mister repoet sin vagt mod at
  implementeringen glider fra kontrakten. `/openapi/contract.yaml` er kontrakten selv, indlejret i
  assemblyen, og det er den dokumentationssiden på `/scalar/` viser. **De er ikke en dublet der
  skal ryddes op.** Formen er ens — 15 operationer og 22 skemaer i begge — men prosaen findes kun i
  kontrakten: afledningen har **0 summaries på 15 operationer** og kalder sig selv `Todo.Host | v1`
  (dannet af entry-assemblyens navn, så den hedder noget andet under en testkørsel), hvor
  kontrakten hedder `Todo API` og har 29 `description`-felter. At vise en afledning som om den var
  kilden inviterer desuden til, at nogen retter afledningen. `ContractDocumentTests` vagter at
  siden viser kontrakten, og den blev set fejle, da endpointet blev peget på afledningen.
  Bemærk samtidig, at kontrakten ikke er prosaløs *i modsætning til* at være fuldt beskrevet:
  målt har **kun 4 af de 15 operationer** en `summary`. Der er stadig prosa at skrive.
- **En ny minimal API uden for `/api/` skal have `.ExcludeFromDescription()`** (opdaget uden for
  skiverne 2026-08-17). Antagelsen var, at drift-testen kun så på `/api/`-præfikset. **Den holdt
  ikke.** ASP.NET Core beskriver hver minimal API i `/openapi/v1.json` uanset præfiks, så
  `/openapi/contract.yaml` dukkede op som en 16. operation, og `ContractDriftTests` fejlede på et
  mismatch mellem mængderne. Rettet på endpointet med `.ExcludeFromDescription()`, hvilket også er
  det rigtige på sagen: ruten dokumenterer API'et frem for at være en del af det. Det står her,
  fordi den næste der lægger en rute uden for `/api/`, vil se en fejl der **ligner** kontraktdrift
  og i virkeligheden er et manglende kald. Scalars egne ruter slipper kun, fordi biblioteket selv
  ekskluderer dem.
- **Dokumentationssiden er vagtet mod at kalde ud, og statisk gennemsyn fangede det ikke**
  (opgjort uden for skiverne 2026-08-17). Pakken blev valgt på bevis for at den virkede offline:
  den serverede HTML henviste ikke til nogen fremmed vært, og bundlen blev gennemsøgt for
  CDN-værtsnavne. **Begge tjek bestod, og begge var utilstrækkelige.** `ApiDocsJourneyTests` —
  som afviser hver request til en fremmed vært og derefter fastslår, at mængden af afviste URL'er
  er **tom** — fangede to kald fra JavaScript **efter mount**:
  `api.scalar.com/vector/registry/curated` og `/vector/registry/search?query=`, fra Scalars "Ask
  AI"-knap. Siden renderer fint uden dem; den kalder blot også ud. Lukket med `.DisableAgent()`,
  verificeret minimalt — `DisableMcp()` og `DisableTelemetry()` var ikke nødvendige. Et tidligere
  fund af samme form: bundlens `@font-face`-regler (Inter og JetBrains Mono) pegede på
  `fonts.scalar.com`, hvilket **HTML'en** ikke afslørede; lukket med `.DisableDefaultFonts()`. Lektionen er generel og står
  også i `CLAUDE.md`: for en lokal app der kan være uden netværk, er en sides statiske
  henvisninger ikke hele historien. Kun en vagt på rute-niveau, der blokerer fremmede værter og
  fastslår at intet blev **forsøgt**, ser et kald efter mount.
- **SQLite-migrationer der fjerner en kolonne** taber dens data lydløst, fordi
  tabellen bygges om. Læs genererede migrationer; backup-kopien før migrering er
  sidste forsvar.
- **Photino** kræver WebView2-runtime. Til stede på Windows 11 som standard.
- **Fixtures** kan blive forældede når serverne opgraderes; kontrakt-testene er
  første sted det opdages.
- **Opgaverækken er nu en typet børnekomponent** (løst i skive 6). `li[appTaskRow]` med
  `input.required<TodoTask>()` erstattede den delte `#taskRow`-skabelon, og en tastefejl
  i bindingen giver nu `TS2551`. Lektionen er, at `strictTemplates` **ikke** dækkede
  hullet: en delt `ng-template` med `let-`-variabler har konteksttype `any` og bliver
  aldrig typetjekket, uanset flag, fordi `[ngTemplateOutletContext]` ikke afstemmes.
- **Id'erne er `Guid` v4 og skal være `long`** (besluttet 2026-08-14, udskudt og
  uplaceret 2026-08-17). Bemærk at branchen ikke er gået fra GUID til `long` — den er gået fra
  **tilfældig v4** til **tidsordnet UUIDv7**, som `Guid.CreateVersion7()` giver i
  .NET 9+. Argumentet for `long` her er et andet: i SQLite bliver `INTEGER PRIMARY
  KEY` et alias for rowid, og "opgave 42" kan siges højt. Fragmentering er uden
  betydning ved denne størrelse. **Migreringen bliver dyrere for hver skive der
  lægges imellem**, fordi hver ny skive tilføjer kode og tests der rører id'er.

## 11. Forholdet til GTD

Appen er **ikke** et GTD-system, og det er et bevidst valg. Vurderet 2026-08-14
mod *Getting Things Done*, opdateret 2026-08-17 efter skive 5:

Det, appen allerede gør efter bogen: indfangning er friktionsfri (ét felt, Enter),
den samler fra flere kanaler, og mentions-indbakken er en rigtig *clarify*-fase,
hvor du beslutter frem for at få noget påtvunget.

Det, skive 5 lukkede — de to billigste huller:

- **Venter på** er bygget. En opgave, der ligger hos en anden, har hvem den venter
  på og hvor mange dage den har ventet, og den står i sin egen sektion frem for at
  optage plads i dagens.
- **Someday/Maybe** er bygget som "Måske". En parkeret opgave er ude af syne,
  indtil "Vis måske" slås til, så listen kan holdes kort uden at noget slettes.

Det, den stadig ikke gør, i rækkefølge efter hvor meget det betyder:

- **Deadline er den eneste organiserende akse.** GTD reserverer kalenderen til det,
  der *skal* ske en bestemt dag, og organiserer resten efter kontekst. Hos os bliver
  "Uden deadline" en skraldespand, og fristelsen til at sætte falske deadlines for at
  holde noget synligt er reel — hvilket underminerer de ægte deadlines. Skive 5 tog
  de ventende og de parkerede opgaver ud af sektionerne, men for alt det, der er
  tilbage, er deadline stadig den eneste akse.
- **Der er ingen projekter.** Underopgaver er en tjekliste under én opgave; de kan
  ikke have egen deadline eller stå selvstændigt på listen. Et udfald der kræver
  flere handlinger på forskellige tidspunkter, kan ikke repræsenteres.
- **Der er ingen kontekster** og ingen støtte til ugentlig gennemgang, som er GTD's
  nøglevane. "Måske"-listen er præcis den liste, en ugentlig gennemgang ville tage
  fat i, så hullet er blevet lettere at se — ikke mindre.

Det, der står tilbage, bliver stående her, så det er et valg og ikke en
forglemmelse — og så en senere beslutning om at gå hele vejen kan træffes med
åbne øjne.
