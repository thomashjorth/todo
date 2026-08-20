# Personlig todo-app — design

Dato: 2026-08-13
Status: skive 0–10 bygget. Aktuel tilstand og næste skridt står i `docs/HANDOFF.md`;
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
| Id-type | `long`, tildelt af SQLite ved indsættelse. **Leveret i skive 10.** `INTEGER PRIMARY KEY` er et alias for rowid, så "opgave 42" kan siges højt — argumentet er SQLite-specifikt og ergonomisk. **Ikke** fragmentering: branchen gik fra tilfældig `Guid` v4 til tidsordnet UUIDv7, ikke til `long`. Se afsnit 10. |
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
| Jira | **Data Center 10.3.24**, målt 2026-08-18. REST v2 med wiki-markup. **PAT som Bearer verificeret** mod `/rest/api/2/myself` (200), ikke kun udledt af versionen. |
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
lyve om — altså i skive 11 med Jira.

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

`Id` er en `long` siden skive 10, ikke en `Guid`, og **den tildeles af SQLite ved indsættelse**:
`INTEGER PRIMARY KEY AUTOINCREMENT` er et alias for tabellens rowid, så hverken klienten eller
entiteten sætter et id. Det samme gælder `SubTask.Id` og `UserAlias.Id`. Konsekvensen at kende er,
at et objekt der endnu ikke er gemt, har `Id == 0`.

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

Skive 9 tilføjer `DeferUntil` (`DateOnly?`) — dagen opgaven *begynder*. **Udskudtheden
er beregnet, ikke gemt**: der er ingen `Deferred`-status på `TodoStatus` og intet job der
skal køre ved midnat. `DeadlineBuckets.For` svarer `Deferred`, når startdatoen ligger
*strengt efter* i dag, så opgaven kommer af sig selv tilbage — ikke fordi noget skrev til
databasen, men fordi uret siger noget andet i morgen. Feltet ejes altid lokalt som
`Deadline`, og der valideres ikke på det: en startdato der er passeret betyder blot, at
opgaven er begyndt, og en startdato efter deadlinen er lovlig.

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

## 4a. Ventende statusser fra en ekstern kilde

Besluttet 2026-08-18, som krav til skive 11.

En Jira-sag der står i en **ventende** status — hos os `Afventer general` og `Afventer PO/FA` —
skal kunne komme med i importen, **hvis brugeren har slået det til i en indstilling. Default er
fra.** Det default er ikke vilkårligt: en import der tavst hentede ventende sager ind, ville
lægge ting i listen man ikke kan handle på, og det er præcis det problem "Venter på" blev bygget
for at løse i skive 5.

Tre ting følger, og de er grunden til at kravet står her frem for i en plan.

**Statusnavnet kan ikke hårdkodes, og der er seks af dem.** Målt 2026-08-18 mod
`/rest/api/2/project/SAAS/statuses`: projektet har elleve statusser, og **seks** af dem er
ventende — `Afventer Bug`, `Afventer general`, `Afventer KAM`, `Afventer Kunden`,
`Afventer PO/FA` og `Venter på support`. De øvrige fem er `I gang`, `Ny SLA`, `Løst`, `Lukket`
og `Annulleret`.

**Og navngivningen er ikke konsekvent.** Fem begynder med `Afventer`, én hedder
`Venter på support`. En præfiks-heuristik ville derfor tabe den sjette — det er det konkrete
argument for en **eksplicit liste** frem for at være smart. Jiras statuskategorier hjælper heller
ikke: alle seks ligger sammen med `I gang` i kategorien `indeterminate`, så kategorien kan ikke
skelne ventende fra igangværende.

Indstillingen er altså en liste af statusnavne brugeren vælger. Skal listen kunne vælges frem for
skrives, kræver det et kald til instansen, og det hænger sammen med "Test forbindelse" i samme
skive: en statusliste forudsætter en virkende forbindelse.

**Det er en mapning, ikke en filtrering — besluttet 2026-08-18: `WaitingFor`.** Appen har sin egen
`WaitingFor` med `WaitingOn` og `WaitingSince`, og en sag i `Afventer general` betyder semantisk
det samme: den ligger hos en anden. Den importeres derfor **som `WaitingFor` og lander i "Venter
på"**, ikke som `Open` i deadline-sektionerne.

Indstillingen er dermed ikke "medtag disse statusser". Den er **"disse Jira-statusser betyder
ventende"** — en liste af statusnavne fra instansen, som mapper til én lokal status. Navngiv den
efter det, ellers inviterer den til at blive læst som et filter.

To ting følger, og de er ikke afklaret:

**`WaitingSince` skal komme fra changeloggen — det billige alternativ findes ikke.** Skive 5
udregner `waitingDays` på serveren ud fra `WaitingSince`, og hele pointen var *"hvor længe har du
ventet"*. Sætter importen den til importtidspunktet, lyver dagtællingen.

Målt 2026-08-18: **`statuscategorychangedate` returneres ikke** af Jira DC 10.3.24, så den genvej
er ude. `expand=changelog` virker derimod, og giver tidsstemplede statusovergange med
sekundpræcision og tidszone (`2026-08-17T14:10:13.593+0200`). `WaitingSince` er dermed
**`created` på den nyeste changelog-post der indeholder et `status`-item**.

To konsekvenser. Tidsstemplet har **offset**, og appen gemmer tidsstempler som UTC `DateTime` —
aldrig `DateTimeOffset`, som SQLite ikke kan sortere — så det skal konverteres, ikke gemmes råt.
Og changeloggen er et **ekstra kald pr. sag**, medmindre den hentes med i søgningen; `total` var
10 for én bruger, så det er billigt her, men det er ikke gratis i almindelighed.

**Om `Status` bliver et eksternt felt, var en reel spænding — den er afgjort og implementeret i
skive 11.** Afsnittet ovenfor siger, at
synkronisering kun må skrive i `Ext*`-felter, og at dine egne felter aldrig røres. Men `Status`
er dit eget felt fra skive 1. Løsningen skal følge mønstret fra `Title` og `Requester`: statussen
**fødes fra kilden ved import** og er derefter din. Ellers kunne du ikke markere en importeret
opgave færdig lokalt, uden at næste synkronisering trak den tilbage. Det betyder også, at en sag
der forlader den ventende status i Jira, **ikke** automatisk forlader "Venter på" hos dig — og
det er det rigtige, men det skal stå skrevet, ellers ser det ud som en fejl.

*Afgjort og bygget 2026-08-19.* `POST /api/jira/import` skriver `Status` **én gang** ud fra det
statusnavn klienten sender, slået op i indstillingens ventelistes navne, og rører den aldrig igen.
Det er derfor `JiraImportRow` **ikke** bærer et `isWaiting`-flag: afgørelsen er serverens, fordi
indstillingen bor på serveren, og en påkrævet boolean kan ikke håndhæves på wiren. Bemærk også den
anden halvdel af "derefter er den din": importen er ikke idempotent på indhold — en sag der allerede
er importeret markeres `alreadyImported` i forhåndsvisningen og **springes over** frem for at blive
skrevet igen. Afstemningen mod en *senere* sync ligger i skive 14 med `Ext*`-felterne; skive 11 har
ingen.

*To ting fravalgt bevidst, så de ikke ser ud som huller.* `Ext*`-felterne, `TitleOverridden`,
`LastSyncedAt` og selve afstemningen ligger i **skive 14**, fordi de beskytter mod en senere sync der
ikke findes endnu: felterne ville blive skrevet og aldrig læst, og en vagt på dem kunne ikke bringes
til at fejle. Og `ExternalUrl` er **beregnet** af basisURL og `ExternalKey`, ikke en kolonne — så en
bruger der retter sin basisURL, retter samtidig hvert link. Skive 11 har derfor **ingen migrering**.

**`WaitingOn` kan formentlig ikke udledes.** Appen importerer sager *tildelt dig*. En sag tildelt
dig, som står i en ventende status, betyder at du venter på nogen der **ikke** står i
assignee-feltet. Afsnit 4 er udtrykkeligt om, at `Requester` er hvem der bad *dig*, og `WaitingOn`
er hvem *du* venter på — de må ikke blandes sammen. `WaitingOn` bliver derfor tom og noget
brugeren selv udfylder.

Bemærk at `DeferUntil` fra skive 9 nu er en tredje mulig placering for noget der ikke kan handles
på endnu — men den betyder *ikke endnu*, hvor en ventende status betyder *ligger hos en anden*.
Bland dem ikke sammen.

### Vagten — når en ventende status er handlingsklar alligevel

*Besluttet og bygget 2026-08-19, som en udvidelse af skive 11.*

Afsnittet ovenfor holder kun, når sagen ligger hos **en anden**. Der findes et tilfælde hvor den
ligger hos **dig**: den generelle pulje. Sager i `Afventer general` er ikke tildelt nogen og bliver
taget af den der har vagten i rotationen, så **samme status betyder to forskellige ting** — *venter
på puljen* når du ikke har vagten, og *venter på dig* når du har den. Det er ikke statussen der
afgør det. Det er en kendsgerning uden for Jira: hvis uge det er.

Derfor **en kontakt**, `jiraOnDuty`, plus sin egen liste af statusnavne, `jiraDutyStatuses`. Tre ting
følger.

**Der er nu fire Jira-indstillinger, og de to par gør hver sin ting.** `jiraWaitingStatuses` +
`jiraIncludeWaiting` er en **mapning**: navnene siger hvilke statusser der *betyder* ventende, og
kontakten siger om de sager alligevel må komme med. `jiraDutyStatuses` + `jiraOnDuty` **udvider hvad
der hentes**: navnene lægger et `OR status IN (…)`-led på JQL'en, så puljens sager — som ikke er
tildelt dig og derfor slet ikke er i svaret — overhovedet kommer med. Det ene oversætter et svar,
det andet ændrer spørgsmålet. Læs ikke det andet par som endnu et filter over det første.

Parenteserne om den disjunktion er load-bearing: `AND` binder tættere end `OR`, så leddet skal stå
som `project = X AND resolution = Unresolved AND (assignee = currentUser() OR status IN (…))`. Uden
dem ville højresiden stå frit og trække puljesager fra alle fire projekter PAT'en ser — kundens `KK`
iberegnet — og løste sager med. Skive 11's vagt mod en tom projektnøgle er blind for det: nøglen
*er* sat, den er blot havnet inde i en parentes der forsvandt.

**Reglen bor i `JiraStatusRoles.For` i `Todo.Core`, og grenenes rækkefølge er load-bearing.** Vagten
spørges først: står navnet i *begge* lister, og er kontakten slået til, er rollen `Duty` og ikke
`Waiting`. **Vagt slår ventende.** Rollen er en tredeling frem for en boolean — `Actionable`, `Duty`,
`Waiting` — fordi `Duty` og `Actionable` importeres ens (`Open`), men kun `Duty` mærkes på skærmen og
kun `Waiting` koster et changelog-kald. Præcis som `DeadlineBuckets.For` falder **gættet uden
begrundelsen den anden vej**: ventende er den ældste regel, så man tror den vinder. Byttes de om,
importeres puljens sager som `WaitingFor` og forsvinder ud af deadline-sektionerne — netop det
arbejde du har vagten for, skjult. Reglen er ét sted, fordi afgørelsen træffes to gange: i
forhåndsvisningen og i importen. Begge steder læses indstillingerne **som de står nu**, så en
forhåndsvisning kørt under en rotation der siden er slut, skriver ikke det gamle svar.

**Puljens størrelse: to tal fra to slags kilder.** Målt 2026-08-19 stod der **2** sager i
`Afventer general`. Brugeren oplyser desuden, at puljen sjældent overstiger **10** — men det er en
**procesgrænse**, ikke en måling: den holder fordi rotationen kører og nogen rydder op, ikke fordi
noget håndhæver den. De to tal må ikke læses som det samme, og **koden afhænger ikke af nogen af
dem**: pagineringen i `JiraTaskSource` håndterer vilkårlig størrelse. Skulle rotationen stå stille,
vokser puljen, og appen viser den bare længere.

**Tre ting er uafgjorte med vilje. De er ikke huller nogen har overset.**

*Ingen minder dig om at slukke.* Der er ingen slutdato på vagten, kun kontakten. En slutdato ville
kræve noget der kører ved midnat, og skive 9 undgik netop det ved at gøre udskudtheden **beregnet**.
Vi valgte **synlighed** frem for automatik: import-skærmen siger med ord at vagten er slået til, så
tilstanden kan ses hver gang man er der. Glemmer man alligevel at slukke, kommer puljens sager med i
en uge de ikke skulle — synligt, ikke tavst.

*Importerede pulje-sager bliver liggende, når en kollega tager sagen.* `Status` er lokal efter
import, som `Title` og `Requester` — det er det rigtige design, og det er skrevet ned ovenfor. Men
puljen **churner** mere end dine egne sager, så konsekvensen rammer hårdere her: en sag du hentede i
din vagtuge, og som en anden løste, ligger stadig på din liste indtil du selv lukker den. En
afstemning ville kræve skive 14's sync.

*`alreadyImported` gælder på tværs af vagtuger.* Dedup er `SourceId` + `ExternalKey`, uden hensyn
til hvornår. Tages samme sag op igen i en senere rotation, er den stadig "importeret tidligere" og
tilbydes ikke — formentlig rigtigt, for opgaven ligger jo allerede på listen, men **uprøvet i brug**.

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

Tid bag `IClock` — "overskredet" og "i dag" er forretningslogik og må ikke afhænge af
`DateTime.Now`.

**Rettet 2026-08-19, efter skive 11: der er hverken en `DelegatingHandler` eller en
`ICredentialStore`, og begge blev valgt fra med vilje.** Afsnittet lovede dem, og koden har dem
ikke — så her står hvad der faktisk blev bygget, frem for at lade dokumentet love noget andet.

`ICredentialStore` er fravalgt, fordi tokenet ligger i klartekst i `Setting`-tabellen (afsnit 3), og
et interface med **én** implementation og ingen alternativ lagring er en abstraktion uden et andet
tilfælde. Skal det senere flyttes til DPAPI eller Windows Credential Manager, er det stedet
`JiraSettingsReader` læser fra, der skal skifte — ét sted. Interfacet kan indføres den dag der er to.

`DelegatingHandler` er fravalgt af en konkret grund frem for af smag: **basisURL'en er en
kørselstidsindstilling**, så `HttpClient.BaseAddress` sættes aldrig, og hver request bygges med en
absolut URI ud fra den indstilling der gælder *nu*. En handler ville skulle læse den samme
indstilling som kaldet selv gør, og `Authorization: Bearer` er så én linje på requesten
(`JiraTaskSource`, linje 239). En handler er stadig det rigtige den dag der er to kilder der skal
autentificere ens — det er skive 12's spørgsmål, ikke 11's.

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
9. **`DeferUntil` — en startdato** — en opgave kan have en dag hvor den *begynder*, og ligger
   indtil da i sin egen Udskudt-sektion i stedet for at optage plads i deadline-sektionerne.
   GTD's *tickler*, og det billigste ægte GTD-hul der var tilbage; se afsnit 11. **Færdig.**
   *Udskudtheden er beregnet, ikke gemt — ingen ny status, intet midnatsjob, og migreringen er
   én `AddColumn`. Rækkefølgen mellem Overskredet og Udskudt er load-bearing og genuint
   kontraintuitiv; se afsnit 10. Skiven fandt desuden en tabsfejl der slet ikke handlede om
   startdatoen: `TaskStore.update` sender en fuld request, og backenden læser et fraværende felt
   som "ryd", så en rettelse af noget helt andet slettede en gemt startdato lydløst. Fundet af en
   test, ikke af en gennemlæsning.*
10. **`long` som id** — `Guid` v4 erstattes af `long` på `TaskItem`, `SubTask` og `UserAlias`, og
    id'et tildeles af SQLite ved indsættelse. I SQLite bliver `INTEGER PRIMARY KEY` et alias for
    rowid, så **"opgave 42" kan siges højt**. **Placeret her 2026-08-18**, efter at have ligget
    uplaceret gennem tre leverancer; Jira-importen og hver skive efter den rykkede ét nummer op.
    Planen ligger i `docs/plans/2026-08-17-long-ids.md`. **Færdig.**
    *Migreringen er skrevet i hånden, og EF's egen blev kasseret — det er skivens vigtigste
    erfaring. `dotnet-ef migrations add` scaffoldede fire `AlterColumn<long>` på TEXT-primærnøgler
    og advarede selv: "An operation was scaffolded that may result in the loss of data." Advarslen
    var sand. En `CAST` af en Guid-streng læser et ledende talpræfiks og giver ellers `0`; målt på
    fem realistiske Guid'er blev fem distinkte værdier to, hvoraf tre var `0` — sammenfaldende
    primærnøgler. Begge migreringens kroppe blev derfor erstattet af SQL med mapningstabeller og
    `ROW_NUMBER()`, mens kun modelsnapshottet blev beholdt af det EF genererede. Resten af
    lektionerne — hvorfor forældre før børn er den eneste rækkefølge der kører, og hvorfor et
    `{id:guid}` i en ruteskabelon er usynligt for både `grep` og compileren — står i afsnit 10.*
11. **Jira-import** — `ITaskSource`, afstemning, lokale felter der overlever sync.
    Her bygges også "Test forbindelse" ind i indstillingssiden, nu hvor der er
    en server at teste mod. **Versionen er målt — Data Center 10.3.24, se afsnit 10.** **Færdig.**
    *Et krav blev besluttet 2026-08-18: en importeret sag i en ventende status skal kunne
    komme med, hvis brugeren har slået det til i en indstilling — default fra. Se afsnit 4a, hvor
    den sidste åbne beslutning nu også er afgjort og bygget.*
    *Leveret uden **afstemning** og uden `Ext*`-felter: de ligger i skive 14, fordi de beskytter
    mod en senere sync der ikke findes endnu, og en vagt på dem kunne ikke bringes til at fejle.
    Skiven har derfor **ingen migrering** — `ExternalUrl` er beregnet af basisURL og `ExternalKey`,
    ikke en kolonne. Det er ikke et hul, det er en afgrænsning; se afsnit 4a.*
    *Fire erfaringer er værd at bære, og de tre første kostede tid.* **(1) Wiki-markup er ikke
    markdown med andre tegn.** `*x*` er fed i wiki og kursiv i markdown, så en tegn-for-tegn-mapning
    vender betoningen om; og `----` oversat til `---` bliver en **setext-overskrift**, hvorved
    stregen forsvinder og afsnittet over den bliver en H2. Begge fund kom af at føre konverterens
    output gennem appens egen `marked` — ikke af at læse regexerne, som så rigtige ud.
    **(2) En falsk server der udsender .NET's format måler sig selv.** `System.Text.Json` binder kun
    offsets i *udvidet* form, og Jira sender den *basale*: `+0200` kaster, `+02:00` virker. Typer man
    samme felt som `DateTimeOffset` i sin `FakeJira`, udsender den den udvidede form, koden bliver
    grøn, og først den rigtige instans kaster — på hver enkelt sag.
    **(3) Et fremmedsystems feltnavn skal bindes eksplicit.** Jira staver `duedate` i ét ord, og
    camelCase-politikken ledte efter `dueDate`, læste feltet som fraværende og gav **null i hver
    deadline** — uden at nogen test faldt, fordi der fandtes en test for en sag *uden* deadline.
    **(4) Hemmeligheder og fulde erstatninger går ikke sammen.** `PUT /api/settings` læser et
    fraværende felt som "ryd", og tokenet kan aldrig sendes tilbage til klienten, så det fik sit eget
    `PUT`/`DELETE /api/jira/token`; `SettingsResponse` bærer kun `hasJiraToken`. Og en **tom**
    projektnøgle må ikke betyde alle projekter: PAT'en ser fire, kundeprojektet `KK` iberegnet.
    *Skiven efterlod desuden elleve umålte `@if`-grene og et manglende `FromJira` på builderen —
    lukket i skivens sidste opgave; se afsnit 10.*
    *Udvidet 2026-08-19 med **vagt-statusser**, uden et skivenummer; se nedenfor og afsnit 4a.*
12. **ADO-import** — samme mønster. Her viser det sig om abstraktionen fra 11 duer.
13. **Mentions-indbakke** — WIQL, dedup, "gør til opgave". Mest usikre del, derfor sent.
    *Krav tilføjet 2026-08-20:* **omtalens ophav afgør hvordan den præsenteres.** Er kommentaren
    på et **pull request**, skal det *indikeres* — en omtale i en kodegennemgang er en anden slags
    arbejde end en i et krav, og den skal kunne skelnes på listen uden at åbne den. Er den på en
    **user story, task eller feature**, præsenteres den som en **kravsafklaring**. Bemærk hvad det
    betyder for hentningen: work item-typen er allerede i WIQL-svaret, men et pull request er
    **ikke et work item** — så de to slags omtaler kommer fra hver sin kilde, og "indiker ophavet"
    er derfor ikke et felt på én række, men to veje der skal mødes i én indbakke. Det skal måles
    før skiven planlægges, på linje med målingen af `updates` mod `comments`.

    *Krav tilføjet 2026-08-20, to filtre:*

    **Er omtalen på et pull request der er `completed`, filtreres den fra.** Bemærk hvad det
    forudsætter, og at det er en **tredje** rundtur: omtalen kommer fra kommentaren, sagstypen fra
    pull requestet, og *tilstanden* af pull requestet er endnu et opslag. Filtret kan derfor ikke
    ligge i WIQL'en. Og bemærk faldgruben: et pull request der lukkes **efter** at omtalen blev
    hentet, forsvinder først ved næste hentning — filtret er en egenskab ved *nu*, ikke ved
    omtalen. Om `abandoned` skal filtreres på lige fod med `completed` er **ikke** afgjort;
    spørg frem for at gætte, for en forladt gennemgang kan stadig kræve et svar.

    **Omtaler ældre end 30 dage filtreres fra, og grænsen er en indstilling.** Samme form som
    `ado.defaultDeadlineDays` fra skive 12, og den præcedens skal genbruges frem for genopfindes:
    et **ikke-nullable** `int` med `default:` i kontrakten, standarden i **læselaget** så en
    fraværende række læses som 30 og et bevidst `0` bevares, validering af både negativ og en øvre
    grænse, og **ingen** række gemt for standardværdien — ellers vælter de tre tests der påstår om
    hele `Settings`-tabellen. Læs `AdoDefaults` og `AdoSettingsReader` før feltet skrives.
    **Uafgjort:** om `0` betyder *ingen grænse* eller *kun i dag*. For dagantallet i skive 12 blev
    `0` valgt til at betyde "slået fra", og den læsning peger her mod *ingen grænse* — men det er en
    beslutning, ikke en aflæsning, og den skal træffes eksplicit.

    **Fælles for de to filtre:** de fjerner rækker brugeren ellers ville have set, så de skal kunne
    **ses** virke. Et filter der tavst tømmer en indbakke ligner en tom indbakke, og skive 11's
    forhåndsvisning har præcedensen — den blokerede rækker *synligt* med hver sin grund frem for at
    udelade dem.
14. **Baggrundssync, tray og notifikationer.**
15. **Livscyklus og arkiv** — detached-håndtering, "vis afsluttede".
16. **Pakning** — self-contained exe, autostart.

### Ønsket, men ikke placeret endnu

To ting er besluttet uden en plads i rækkefølgen. De står her frem for at blive
glemt, og de skal placeres bevidst frem for at glide ind foran de nummererede skiver.
`long` som id stod her fra 2026-08-17 og fik en plads 2026-08-18 som skive 10; Swagger-linket og
vagt-statusserne blev leveret uden for skiverne og står nedenfor som lukkede.

- **Revisionslog med trends.** En hændelseslog ved siden af opgaverne — hvad
  ændrede sig hvornår — som kan bære spørgsmål som "hvor mange lukker jeg om ugen"
  og "hvor længe ligger noget i Venter på". Den er også fundamentet for GTD's
  ugentlige gennemgang, som appen slet ikke understøtter i dag. Største af de to.
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
- **Vagt-statusser fra Jira — leveret uden for skiverne 2026-08-19.** To indstillinger,
  `jiraDutyStatuses` og `jiraOnDuty`, udvider JQL'en med et `OR status IN (…)`-led når rotationen er
  din, så den generelle puljes sager kommer med selvom de ikke er tildelt dig — og de importeres som
  `Open`, fordi **vagt slår ventende**. **Den fik bevidst ikke et skivenummer**, af samme grund som
  Swagger-linket: det er en **udvidelse af skive 11**, ingen ny datamodel, **ingen migrering** og
  ingen ny skærm — de to indstillinger er rækker i `Setting`, og `isDuty` på forhåndsvisningsrækken
  er beregnet af to lister der begge ligger der. Et nummer ville desuden have skubbet ADO-importen
  fra 12 til 13 og hver skive efter den med (mentions, baggrundssync, livscyklus, pakning) — samme
  regning som dengang `long` som id fik nummer 10 og flyttede Jira-importen til 11. Der var her intet
  der retfærdiggjorde den. Planen ligger i `docs/plans/2026-08-19-jira-duty-statuses.md`, begrebet og
  de tre uafgjorte ting står i afsnit 4a.

## 10. Risici og åbne punkter

- **ADO-mentions er målt 2026-08-20, og den mest usikre antagelse i hele designet holder.** Den stod
  som "verificér i skive 11, ikke i 12"; skive 11 kunne ikke, fordi målingen kræver brugerens egen
  instans og eget token. Den er nu kørt, og **skive 12 er ikke længere blokeret.** Elleve ting er
  afgjort, og fire af dem ændrer designet.

  **Instansen.** `deploymentType: onPremises` — TFS/ADO Server, ikke Cloud. Samlingen er
  `Edora Software` **med et mellemrum**, som skal være `%20` i en URL, projektet er `Saas`, og der er
  **ingen `/tfs/`-mappe**: samlingen ligger i roden. Bemærk at projektet heder `Saas` her og `SAAS` i
  Jira — to systemer, to stavemåder, egen indstilling.

  **Versionen er 7.1, ikke 7.2.** Målt med `OPTIONS {collection}/_apis/wit`: `wiql`, `updates` og
  `workItems` har alle `maxVersion 7.2` men `releasedVersion 7.1`. Bruger vi 7.2, kalder vi et
  preview-API der må ændre sig under os.

  **`comments` er preview-only, og det ændrer designet.** `releasedVersion` er `0.0` på **begge**
  registrerede rækker, altså i hele spændet 3.0–7.2 — ressourcen er aldrig blevet GA på denne server.
  **`updates` er derimod GA på 7.1** og bærer `System.History` pr. revision, altså kommentarteksten
  *plus* hvornår den kom. `updates` er derfor ikke en fallback, den er **den primære vej**, og afsnit
  6's formulering om at "hente kommentarerne" skal læses som revisioner.

  **`CONTAINS WORDS` på `System.History` virker.** Det var hele antagelsen, og den er nu et 200 med
  resultater frem for en formodning.

  **Men serveren kan ikke filtrere på mentions, og det er målt to gange.** Mention-markupen er
  `<a href="#" data-vss-mention="version:2.0,{GUID}">@Visningsnavn</a>`, og GUID'et identificerer
  personen entydigt — men **`CONTAINS` uden `WORDS` giver 200 med nul resultater** for både
  `data-vss-mention` og et konkret GUID, selv på en sag vi *ved* indeholder strengen og som ligger
  inde i vinduet. Indekset dækker **prosaord, ikke markup**. Kontrollen er afgørende for at læse det
  rigtigt: den samme query med navnet gav 25 hits, så nul betyder "ikke indekseret", ikke "intet at
  finde". **GUID-matchet skal derfor ske i klienten.**

  **Det fulde visningsnavn er præcist; fornavnet er ikke.** `CONTAINS WORDS 'Thomas'` over 30 dage gav
  syv, hvoraf **én var falsk** — en sag hvor teksten omtaler en anden Thomas i prosa, og den eneste
  faktiske mention er af en tredje person. `CONTAINS WORDS 'Thomas Hjorth Hansen'` over 90 dage gav
  25, **ekskluderede netop den falske**, og **alle 25 bar GUID'et** ved verifikation. Flerords-
  `CONTAINS WORDS` kræver altså alle ordene, og en rigtig mention renderes med det fulde navn mens en
  prosa-omtale ikke gør. Klientverifikationen er dermed et **sikkerhedsnet**, ikke et filter der gør
  reelt arbejde.

  **Volumen er lav.** 25 kandidater på 90 dage, 10 mentions på 30 — ingen paginering i UI'et, ingen
  filtrering før visning.

  **Én sag kan bære flere mentions** (én havde fire, en anden to), så indbakkens enhed er
  **kommentaren**, ikke sagen — afsnit 4's `Mention` med et unikt indeks er rigtig. Men nøglen kan
  ikke være et kommentar-id: `updates` giver **revisioner**, så den er `workItemId` + `rev`.

  **`revisedDate` er `Z`, ikke et offset** — modsat Jiras `+0200`, som kostede en runde. Ingen
  omregning.

  **Kommentaren er HTML** (`<div>`, `<br>`, `&nbsp;`), så skive 13 skal konvertere **HTML til
  markdown**, hvor skive 11 konverterede wiki-markup. Anden retning, samme klasse af arbejde — og
  skive 11 målte, at en markup-konverter skal testes på det **renderede** resultat, ikke på
  mellemformen.

  **To ting til implementeringen.** WIQL-svaret bærer `asOf`, som er watermarket til inkrementel
  hentning — vi behøver ikke udlede tidspunktet. Og de URL'er ADO giver tilbage bruger projektets
  **GUID** frem for navnet, så en URL vi får udleveret er ikke menneskeligt navigerbar: skal en
  mention linke til sagen, skal vi bygge URL'en selv, som `JiraSettings.BrowseUrl`.

  **Og brugerens GUID hører i en indstilling.** `NoRealInstanceTests` vagter **værtsnavne, ikke
  GUID'er**, så et fixture med det rigtige GUID ville lægge brugerens identitet i repoet uden at nogen
  vagt sagde noget. Brug et opdigtet GUID i tests.
- **Jira-versionen er målt: Data Center 10.3.24** (2026-08-18, læst i Jiras egen om-dialog).
  Det afgør tre ting, som ellers skulle gættes. **Jira 10.x findes kun som Data Center** —
  Server udgik i februar 2024 — så selvhostet er ikke længere en antagelse. Selvhostet Jira
  bruger **REST v2 med wiki-markup** i beskrivelser, ikke Cloud'ens v3 med Atlassian Document
  Format; en importeret beskrivelse skal derfor konverteres **fra wiki-markup** til den
  CommonMark noterne bruger, og det er den største enkeltforskel i importarbejdet. Og **PAT
  som Bearer kræver 8.14+**, så afsnit 2's beslutning holder — under den tærskel havde den
  været forkert.
  **PAT'en er verificeret**, ikke kun udledt: `GET /rest/api/2/myself` med
  `Authorization: Bearer <PAT>` svarer 200 (målt 2026-08-18). Det bekræfter samtidig, at
  `/rest/api/2/` svarer, og at brugeren kan slås op, så `assignee = currentUser()` har noget
  at opløse.
  **Jira 10 fjernede en række forældede REST-endpoints, men ikke dem vi skal bruge** — målt
  2026-08-18 mod instansen frem for hukommelsen:
  `GET /rest/api/2/search?jql=assignee=currentUser()` svarer, og med den **klassiske
  paginering** (`startAt`, `maxResults`, `total`, `issues`, plus `warningMessages`, `names`,
  `schema`) — **ikke** Cloud'ens nyere token-baserede `nextPageToken`/`isLast`. Så `/search` er
  vejen, og `/search/jql` er ikke nødvendig. Projektnøglen er `SAAS` (`SaaS Support`), og PAT'en
  ser fire projekter: `EC`, `KK`, `SAAS`, `TTMBP`.
  **Én designfølge af det:** en import filtreret på `assignee = currentUser()` trækker fra **alle
  fire** projekter, kundeprojektet `KK` iberegnet. Om der skal et projektfilter på, er en
  beslutning til skive 11 — i Jira ses de allerede blandet på det samme dashboard.
- **ADO Server-versionen er stadig ukendt** og afgør endpoints og API-versioner der.
  Verificeres med "Test forbindelse" i skive 12.
- **Skallen har nu farver i begge temaer, og det var mere end kosmetik** (løst i skive 7).
  En `<body>` uden baggrund giver ikke blot forkerte farver — den gør hele `dark:`-systemet
  til **usynlig tekst**: `dark:text-gray-100` på hvid er 1,10:1. `<body>` har nu
  `bg-white text-gray-900 dark:bg-gray-900 dark:text-gray-100` og `scheme-light-dark`, og
  `task-list.html` og `task-row.html` har fået `dark:`-modparter så godt som overalt. Lektionen
  er, at kontrasten ikke længere holdes af øjemål: `ContrastTests` går appens **fire** skærme
  igennem i **begge** farvetemaer — `app.routes.ts` har præcis fire ruter: opgavelisten,
  retro-importen, Jira-importen og indstillingerne — og måler derudover det **udvidede
  detaljepanel** med noten, underopgaverne og statusvælgeren. Panelet er en tilstand på
  opgavelisten, ikke en femte skærm. *Tallet var tre indtil 2026-08-19; Jira-importen gjorde det
  fire, og vagten fulgte med i skive 11's sidste opgave.*
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
- **Overskredet slår Udskudt, og rækkefølgen er load-bearing** (afgjort i skive 9). En opgave kan
  have både en startdato i fremtiden og en deadline der er løbet ud — den var udskudt, og tiden
  løb alligevel. De to udsagn modsiger hinanden, så spørgsmålet er ikke hvad der er rigtigt, men
  hvilken fejl der er værst: **at skjule et tilsagn du allerede har brudt** er værre end at vise
  noget tidligere end planlagt. Derfor vinder `Overdue` over `Deferred` i `DeadlineBuckets.For`,
  og grenene må ikke byttes om. Det er værd at vide, at det er genuint kontraintuitivt: den der
  byggede kernen, uden at have fået reglen med, gættede på det modsatte — at en startdato i
  fremtiden skulle gemme opgaven uanset deadline. En test og en `<summary>` står vagt om
  rækkefølgen nu, men gættet kommer igen, hvis begrundelsen ikke står et sted. **Den modstridende
  tilstand er desuden synlig i panelet** (tilføjet 2026-08-18): står der en startdato efter
  deadline, skriver detaljepanelet at opgaven derfor vises som overskredet. Forrangen er dermed
  ikke længere kun rigtig — den kan forklares den bruger, hvis startdato blev sat til side.
- **Id'erne er `long`, og migreringen kunne ikke overlades til EF** (løst i skive 10). Premisset
  står stadig: branchen er ikke gået fra GUID til `long` — den er gået fra **tilfældig v4** til
  **tidsordnet UUIDv7**, som `Guid.CreateVersion7()` giver i .NET 9+. Argumentet her var et andet
  og holdt: i SQLite bliver `INTEGER PRIMARY KEY` et alias for rowid, og "opgave 42" kan siges
  højt. **Fragmentering var aldrig grunden** og skal ikke bruges som en. Fire ting blev lært, og de
  tre sidste er dem der koster, hvis de genopdages.
  **(1) `CAST` er en ommapning, ikke en typeændring.** SQLite læser et ledende talpræfiks af en
  Guid-streng og giver ellers `0`; målt blev fem distinkte Guid'er to distinkte heltal, tre af dem
  `0` — altså sammenfaldende primærnøgler. Målingen står nu som en påstand,
  `Casting_a_Guid_to_an_integer_collapses_distinct_ids`, frem for som en kommentar der kan blive
  gammel.
  **(2) EF's eget scaffold var netop den destruktive udgave, og sagde det selv.**
  `dotnet-ef migrations add` gav fire `AlterColumn<long>` på TEXT-primærnøgler plus advarslen
  "An operation was scaffolded that may result in the loss of data." Begge kroppe blev skrevet i
  hånden med mapningstabeller og `ROW_NUMBER()`; kun modelsnapshottet blev beholdt.
  **(3) Forældre før børn er ikke ordentlighed, det er den eneste rækkefølge der kører.**
  Fremmednøgler er slået **til** hele vejen — `PRAGMA foreign_keys` er en no-op inde i en
  transaktion, og EF pakker migreringen i én — så et barn ommappet til en forælder der ikke findes
  afvises på stedet med `FOREIGN KEY constraint failed`. Det er også derfor `PRAGMA
  foreign_key_check` næsten intet beviser alene; vagten har en påstand om at fremmednøglen
  **findes** ved siden af, og den blev set fejle. Se `CLAUDE.md`.
  **(4) En ruteskabelons `{id:guid}` er id-typens anden, separat usynlige halvdel.**
  `TaskEndpoints.cs` havde seks `Guid`-linjer **og syv `:guid`-bindinger**; `grep -c Guid` er
  versalfølsom og så ingen af bindingerne, og **compileren ser dem heller ikke**, fordi en
  minimal-APIs rutebinding afgøres i runtime. `long id` mod `{id:guid}` bygger altså grønt og
  svarer **404 fra routingen, før handleren** — hvilket ligner "opgaven findes ikke". Testene
  fanger det ikke, hvis *begge* halvdele glemmes: så virker ruten stadig. Kun et eksplicit
  før/efter-grep på **begge** stavemåder er et ærligt tjek.
  **(5) Vagten sammenligner nu databasen med modellen, ikke kun migreringen med sig selv**
  (tilføjet 2026-08-18). Migreringen navngiver hver kolonne eksplicit, og SQLite fjerner en kolonne
  uden at sige noget — den tabte data omtales i afsnit 10's advarsel ovenfor. Den første vagt såede
  fem af `Tasks`' tretten kolonner, så en kolonne droppet fra `SELECT`-listen ville have bestået.
  **Kolonnepåstandene findes præcis fordi det skete**: `DeferUntil` fandtes ikke da planen blev
  skrevet, skive 9 lagde den på, og den håndskrevne SQL kendte den ikke i **hverken `Up` eller
  `Down`** — det blev fanget ved at måle planen igen, ikke af en test. Der står nu tre påstande, som
  fejler forskelligt: `Every_column_of_every_table_survives_the_migration` sammenligner navnemængden
  for `Tasks`, `SubTasks` og `Aliases` før og efter,
  `Every_column_the_model_expects_exists_in_the_database` sammenligner databasens kolonner med dem
  EF's model forventer, og `Every_field_of_a_fully_populated_row_survives_the_migration` sår én
  fuldt udfyldt række pr. tabel med distinkte værdier og sammenligner felt for felt. En ombytning af
  `Note` og `Requester` i `SELECT`-listen fælder kun den sidste.
  **Før/efter-halvdelen kan stadig ikke se en symmetrisk udeladelse**, og det er ikke længere et
  forbehold men en måling: dens "før" produceres af migreringens egen `Down`, fordi vagten ruller
  tilbage for at nå Guid-verdenen, så glemmes en kolonne i *begge* kroppe er de to mængder enige om
  fejlen — fjernes `DeferUntil` fra både `Up` og `Down`, **består** den. Første udgave af det her
  afsnit affærdigede blindvinklen som praktisk uopnåelig, "det ville kræve, at en ny kolonne blev
  lagt på uden en ny migrering". Det var forkert: `DeferUntil` **fik** sin egen migrering i skive 9,
  og `LongIds` er den senere, så `Assert.EndsWith("LongIds", applied[^1])` holder uanfægtet. Det er
  præcis den virkelige fejls form. Model-påstanden er derfor den holdbare, fordi modellen er en
  **anden kilde** end migreringen; `PendingModelChangesWarning` gør ikke det arbejde, da den
  sammenligner model-snapshottet med modellen og snapshottet er genereret ud fra modellen.
  Før/efter-halvdelen beholdes ved siden af, fordi den lokaliserer en ændring til migreringen frem
  for til modellen.
  Planens aftrykstabel skal desuden læses med to rettelser: **11** id-relaterede forekomster i
  tests, ikke 12 — den tolvte var `SettingsJourneyTests`, hvor `Guid.NewGuid()` bygger et
  midlertidigt **mappenavn** — og **fire** ikke-id-brug af `Guid` tilbage, ikke tre: `RunningHost`s
  midlertidige databasenavn, `DatabaseBackupTests`' mappe og unikke titel, og
  `SettingsJourneyTests`' mappe. Alle bevidste. Builderne var som forudsagt på **nul** og krævede
  ingen ændring.
- **Skive 11 byggede ni opgaver funktion og efterlod vagterne til den tiende, og det er skivens
  vigtigste procesfund** (opgjort 2026-08-19). Da funktionen var færdig, var **elleve** `@if`-grene
  aldrig blevet renderet i nogen test, og de fordelte sig som tre på indstillingssiden
  (`jira-token-stored`, `jira-clear-token`, `jira-connection`) og otte på den nye skærm plus
  opgavelisten. Årsagen er ikke skødesløshed, men at hver af dem kræver et **svar fra Jira**:
  Playwright kan ikke starte en `FakeJira` inde i hostens proces, så uden opsnappede kald findes
  tilstanden simpelthen ikke. Rettelsen er `page.RouteAsync` på `**/api/jira/preview`,
  `**/api/jira/test` og `**/api/jira/statuses` — samme greb som afbrydelsen af
  `/api/system/open-link` — med **fire svar i rækkefølge**, fordi grenene udelukker hinanden: en
  afvisning, en tom liste, en liste hvor hver række er blokeret, og en liste med én række der kan
  importeres. Lektionen er generel: **en skærm bygget færdig uden en E2E-rejse ved siden af har ingen
  måde at vise, at dens betingede linjer aldrig blev renderet.**
- **`external-link` var uopnåelig, fordi builderen manglede en metode** (fundet og lukket 2026-08-19).
  `externalUrl` beregnes kun når `SourceId == JiraTaskSource.Id`, og `TaskItemBuilder` havde
  `FromRetro` men **ingen `FromJira`** — så ingen fixture i hele suiten kunne sætte grenen på skærmen,
  og både kontrastvagten og en link-påstand ville have målt et element der ikke fandtes. Målt frem for
  udledt: fjernes `FromJira` fra fixturet igen, siger Playwright
  `element(s) not found 'Åbn sagen'`. **Grenen kræver desuden to ting, ikke én** — kilden *og* en
  gemt `jira.baseUrl`, for URL'en beregnes af basisURL'en. Det er samme klasse af fund som de tidligere
  uopnåelige vagter i dette repo: dedup-vagten der ikke kunne nås fra UI'et, "ingen reload" bevist
  ved engelsk tekst, og "navnet er ryddet" tjekket på et felt der ikke rendereres i den tilstand.
- **En mislykket åbning af en Jira-sag er tavs på en sammenfoldet række** (fundet i skive 11,
  **bevidst ikke rettet**). `openIssue`s fejl går i `SystemStore.error()`, og `task-row.html`
  rendererer den linje **kun inde i det udvidede detaljepanel** — hvor den hører til notens links.
  Klikker brugeren "Åbn sagen" på en sammenfoldet række og styresystemets link-åbning fejler, sker
  der derfor ingenting synligt. Begrundelsen for at lade den ligge: fejlen kræver at
  shell-åbningen fejler, hvilket er sjældent, og rettelsen ville flytte afsnittet ud af panelet og
  dermed bryde `TaskListScreen.NoteLinkError`, som er scoped til `Detail` og dækker noget der virker
  i dag. **Skrevet ned frem for at være uopdaget** — det er forskellen på et kendt hul og en fejl.
- **Hvad skive 11 stadig ikke vagter** (opgjort 2026-08-19). `jira-error` på indstillingssiden — den
  røde linje når "Test forbindelse" eller "Hent statusser" fejler — er ikke kontrastmålt; dens farvepar
  `text-red-700 dark:text-red-300` er dækket gennem `alias-error` og `jira-import-error`, så det er
  farverne der er dækket, ikke linjen. Samme forbehold som skive 7's tre fejllinjer. De to
  `disabled`-tilstande der kun findes **mens** et kald er i luften (`jira-preview` og `jira-import`
  under `store.busy()`) er transiente og umålte; `jira-import`s disabled-farver er til gengæld målt i
  den tilstand hvor hver række er blokeret. Og **intet i suiten kalder den rigtige instans**: den
  eneste vagt på det er, at intet i repoet må navngive den. `WaitingSince` fra changeloggen er derfor
  målt mod `FakeJira`, som udsender Jiras *basale* offsetform netop for ikke at måle sig selv — men
  formen er afskrevet fra en måling 2026-08-18, ikke fra en løbende test.

## 11. Forholdet til GTD

Appen er **ikke** et GTD-system, og det er et bevidst valg. Vurderet 2026-08-14
mod *Getting Things Done*, opdateret 2026-08-17 efter skive 5 og igen efter skive 9:

Det, appen allerede gør efter bogen: indfangning er friktionsfri (ét felt, Enter),
den samler fra flere kanaler, og mentions-indbakken er en rigtig *clarify*-fase,
hvor du beslutter frem for at få noget påtvunget.

Det, skive 5 lukkede — de to billigste huller:

- **Venter på** er bygget. En opgave, der ligger hos en anden, har hvem den venter
  på og hvor mange dage den har ventet, og den står i sin egen sektion frem for at
  optage plads i dagens.
- **Someday/Maybe** er bygget som "Måske". En parkeret opgave er ude af syne,
  indtil "Vis måske" slås til, så listen kan holdes kort uden at noget slettes.

Det, skive 9 lukkede:

- **En startdato** er bygget. En opgave, der er et rigtigt tilsagn men ikke kan begyndes
  endnu, får dagen den begynder, og ligger indtil da i Udskudt. Det er GTD's *tickler*
  uden mappen — og uden noget der skal huskes, for udskudtheden er beregnet af dagens
  dato frem for gemt.
- **Måske har nu en nabo, der betyder noget andet.** "Måske" siger *ikke et tilsagn*;
  Udskudt siger *ikke endnu*. Det er to forskellige beslutninger, og før skive 9 måtte
  de dele én liste — hvilket gjorde Måske til et sted man lagde begge slags og
  derfor holdt op med at læse.

Det, den stadig ikke gør, i rækkefølge efter hvor meget det betyder:

- **Deadline er stadig den eneste organiserende akse for alt det, der *er* handlingsklart.**
  GTD reserverer kalenderen til det, der *skal* ske en bestemt dag, og organiserer resten
  efter kontekst. Presset på deadline-feltet er lettet af skive 9: noget, der ikke er
  handlingsklart endnu, skal ikke længere vælge mellem en falsk deadline for at blive
  synligt og Måske for at komme af vejen — det får en startdato og forsvinder af sig selv
  indtil den dag. Fristelsen til at lyve om en deadline var reel, og den er blevet mindre.
  **Der er dog én tilstand, hvor de to datofelter modsiger hinanden**: en startdato *efter*
  deadline. Den er tilladt med vilje — felterne gemmer hver for sig, så et forbud ville afvise
  den halve redigering, og en bruger, der flytter begge datoer længere frem, ville få en fejl
  afhængigt af hvilket felt hun rørte først. Men Overskredet slår Udskudt, så startdatoen gør
  i den tilstand ingenting, og panelet siger det nu (tilføjet 2026-08-18) frem for at lade den
  ligge lydløst uvirksom i sit felt.
  **Men det gælder kun udsættelsen.** For alt det, der er handlingsklart nu, er deadline
  fortsat den eneste akse, og "Uden deadline" er fortsat en bred bunke uden andet at
  sortere efter. En rigtig kontekstakse ville revidere afsnit 2 og er ikke sket.
- **Der er ingen projekter.** Underopgaver er en tjekliste under én opgave; de kan
  ikke have egen deadline eller stå selvstændigt på listen. Et udfald der kræver
  flere handlinger på forskellige tidspunkter, kan ikke repræsenteres.
- **Der er ingen kontekster** og ingen støtte til ugentlig gennemgang, som er GTD's
  nøglevane. "Måske"-listen er præcis den liste, en ugentlig gennemgang ville tage
  fat i, så hullet er blevet lettere at se — ikke mindre.

Det, der står tilbage, bliver stående her, så det er et valg og ikke en
forglemmelse — og så en senere beslutning om at gå hele vejen kan træffes med
åbne øjne.
