# CLAUDE.md

Personlig todo-app. Én Photino-proces: ASP.NET Core + Angular i ét vindue, SQLite i
`%APPDATA%\TodoApp\`. Design og leveranceplan: `docs/plans/2026-08-13-todo-app-design.md`.
Aktuel tilstand og næste skridt: `docs/HANDOFF.md`.

## Kør appen

```
Todo.cmd
```

Bygger Angular hvis kilderne er nyere end `wwwroot`, og starter vinduet. Tag `--headless`
med for at køre uden vindue.

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1   # efter en kontraktændring
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1      # efter en Angular-ændring
dotnet test Todo.sln
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

## Maskinen — ting der koster en time hvis de genopdages

- **`pwsh` findes ikke.** Kun Windows PowerShell 5.1.
- **Kald `npm.cmd` og `ng.cmd`, aldrig `npm`/`ng`.** PowerShell-shimmen er i stykker: `& npm`
  bliver til den ukendte kommando `pm`.
- **`Get-Content` læser ANSI.** `Prøv` vises som `PrÃ¸v`. Det er en visningsfejl, ikke en
  ødelagt fil — ret aldrig "mojibake" du kun har set gennem `Get-Content`.
- **Antivirussen (AMSI) blokerer visse PowerShell-scripts**, især `Start-Process` kombineret
  med `Invoke-WebRequest`. Del kommandoen op, eller brug `curl` gennem Bash-værktøjet.
- **Ingen `"` i en `git commit -m`-heredoc.** PowerShell citerer om for native kommandoer, og
  et anførselstegn afslutter argumentet midt i beskeden.
- **EF-værktøjet er `dotnet tool run dotnet-ef`, aldrig `dotnet ef`.** En global `dotnet-ef`
  7.0.16 ligger på maskinen og kan ikke læse en EF Core 10-model.
- **Kør scripts fra repo-roden.** `dotnet tool restore` læser sit manifest fra den aktuelle
  mappe og henter ellers et andet repos værktøjer.
- **Kør ikke prettier på hele repoet.** Arbejdskopien er CRLF og prettier skriver LF, så en
  fuld kørsel omskriver 3810 linjer genereret klientkode og begraver den rigtige diff. Kør den
  kun på filer du selv har rørt, navngivet eksplicit.

## Sikkerhed omkring data

- **Kør aldrig mod `%APPDATA%\TodoApp\todo.db`.** Giv altid `--Data:Path <midlertidig fil>`.
  Det er brugerens rigtige opgaver.
- **Dræb aldrig en `Todo.Host`-proces du ikke selv har startet.** Brugeren har ofte appen åben.
- **`todo.db` alene er ikke databasen.** WAL-tilstand betyder, at de nyeste skrivninger ligger i
  `todo.db-wal`. Skal en database kopieres, så tag `.db`, `-wal` og `-shm` sammen — ellers
  kopierer du en tom header. Backup før migrering laver derfor et `wal_checkpoint(TRUNCATE)`
  først.
- Fra og med indstillingssiden indeholder databasen **tokens i klartekst** (bevidst valg, se
  designdokumentets afsnit 3). Databasefilen og `todo.db.bak-*` må aldrig vedhæftes en
  fejlrapport eller lægges i et repo.

## Konventioner

**Contract-first.** `contracts/openapi.yaml` ejer API'et. Endpoints skrives i hånden som minimal
APIs; en drift-test håndhæver, at de matcher. Genereret kode committes. Ændrer du kontrakten,
så kør `scripts\generate-api.ps1` — ellers fejler friskheds-testen.

**Commits.** Gitmoji foran, én linje, **ingen `Co-authored-by` og ingen Claude-attribution**.

**Styling.** Kun standard Tailwind utility-klasser. **Ingen CSS- eller SCSS-regler** — eneste
undtagelse er `@plugin`-linjen i `styles.css`, som er Tailwinds egen indlæsningsmekanisme.
Hver `bg-*`/`text-*`/`border-*` skal have en `dark:`-modpart. Ikke `text-gray-400` på lys
baggrund (~2,9:1).

**Bredde.** Appen bruges i en spalte på ~480 px, under Tailwinds `sm`-brydepunkt. De
uprefixede klasser **er** den smalle udgave; `sm:`/`md:` bruges kun til at udvide.

**Angular.** Signal-baserede stores ejer al HTTP. Komponenter injicerer aldrig en genereret
klient og kalder aldrig `.subscribe()`. **Ikke NgRx** — bevidst fravalg.

**En delt `<ng-template>` med `let-`-variabler har konteksttype `any`.** `strictTemplates`
tjekker den ikke, og `[ngTemplateOutletContext]` afstemmes ikke. Skal en række være
typetjekket, skal den være en komponent med `input()`. Bruger den `<li>`, så giv den en
attributvælger (`li[appTaskRow]`) — et eget element ville bryde `divide-y` og listens struktur.

**`@if` indsnævrer ikke et signal-kald.** `@if (task().x != null)` efterlader `task().x` som
`T | undefined` inde i blokken. Bind med `@let` først. Brug **ikke** `as`, som binder på
sandhed og taber `0`.

**C#.** Feature-mapper, én type pr. fil (også enums), namespaces følger mapper.
**Kald aldrig noget `Task` eller `TaskStatus`** — `System.Threading.Tasks` er i scope overalt
via implicit usings, og kollisionen giver fejl der peger et andet sted hen.

**Sprog.** Dansk er kilden; `en.json` er oversættelsen. Hver brugervendt streng er en nøgle,
også `aria-label` og `title`. En nøgle skal i **begge** filer, ellers fejler paritetstesten.

**Datoer.** `Deadline` er `DateOnly`. Tidsstempler er `DateTime` i UTC — **aldrig
`DateTimeOffset`**, som SQLite ikke kan sortere korrekt. En deadline må aldrig gennem
`new Date(string)`; det parses som UTC-midnat og kan vise dagen før.

## Testdisciplin

Det her er lært på den hårde måde i dette repo, hver gang ved at en test var grøn af den
forkerte grund.

- **En vagt-test skal ses fejle.** Bryd det den beskytter, bekræft at den fejler på det rigtige
  trin, ret tilbage. En test ingen har set fejle, beviser ingenting.
- **Pas på assertions der ikke *kan* fejle.** Tre gange her: en dedup-vagt der var uopnåelig fra
  UI'et; "ingen reload" bevist ved at kigge efter engelsk tekst, som en reload også ville give;
  og "navnet er ryddet" tjekket på et felt der ikke renderes i den tilstand. Spørg altid: hvad
  ville få den her til at fejle?
- **`GetByRole(..., Name)` matcher på delstreng** medmindre `Exact = true`. En overskrift
  "Todoo" matchede "Todo" og gjorde en E2E-test meningsløs.
- **`TaskListScreen.RowTitled` matcher rækkeknappens *fulde* tilgængelige navn.** Deadline,
  opgavestiller og fremdrift er `<span>`s inde i knappen. Lægger du mere tekst derind, holder
  den op med at matche, og fejlen ligner en manglende række.
- **Sammenlign `scrollWidth` med `clientWidth`**, aldrig med 480. En lodret scrollbar gør
  klientbredden 465, og en fast forventning fejler af den forkerte grund.
- **Drift-testen sammenligner kun stier og metoder.** Skemaændringer fanger den ikke — dem
  dækker wire-format-tests, der ser på det rå JSON. Enum-værdier blev serialiseret forkert i
  en hel skive, før en sådan test blev skrevet.
- **Builders er til *arrange*.** De skriver direkte i databasen og springer API'ets validering
  over; brug dem aldrig til selve handlingen en test skal verificere.
- **Tests må ikke røre `%APPDATA%`.** `RunningHost` giver hver test sin egen midlertidige
  database. Arv fra `ApiTest` eller `BrowserTest` frem for at starte en host i testen.
- **Playwright-tests må ikke have bivirkninger uden for appen.** Kald til
  `/api/system/open-link` opsnappes med `page.RouteAsync` og afbrydes; ellers åbner hver
  testkørsel en rigtig browser.

## Testtal

Efter skive 6: **33** Todo.Core.Tests, **106** Todo.Api.Tests, **7** Todo.E2E, **133** Vitest.
Et ændret tal efter en refaktorering betyder, at en test er tabt eller duplikeret.
