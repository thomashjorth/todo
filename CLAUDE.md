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
baggrund (2,60:1).

**Tailwind 4's palette er oklch, ikke Tailwind 3's hex.** `gray-400` er **2,60:1** på hvid,
ikke 2,85:1. Tallet 2,85 hører til `#9ca3af`, altså Tailwind **3**'s hex-palette; Tailwind 4
definerer sine farver i oklch, og `gray-400` er dermed en anden farve. Regn kontrasttal ud fra
`node_modules/tailwindcss/theme.css` — slå dem aldrig op i en kilde der viser `#9ca3af`.

**Parret er ikke samme trin på begge sider.** `text-gray-500` holder AA i lyst tema (4,84:1 på
hvid, 4,63:1 på `bg-gray-50`), men `dark:text-gray-500` fejler (3,67:1 på `gray-900`). Dæmpet
tekst er derfor `text-gray-500 dark:text-gray-400`. Bytter man mekanisk 400 til 500 på begge
sider, ødelægger man den mørke.

**Pladsholderfarven ligger på `::placeholder`**, ikke på elementet, så en DOM-gennemgang der kun
læser `style.color` er blind for den — `getComputedStyle(el, '::placeholder')` skal spørges
særskilt. Og et felt **uden** en `placeholder-*`-klasse arver `currentColor` med omkring 54 %
alfa og fejler i begge temaer; en optælling af de klasser der står der, kan ikke se en klasse
der mangler.

**Bredde.** Appen bruges i en spalte på ~480 px, under Tailwinds `sm`-brydepunkt. De
uprefixede klasser **er** den smalle udgave; `sm:`/`md:` bruges kun til at udvide.

**Angular.** Signal-baserede stores ejer al HTTP. Komponenter injicerer aldrig en genereret
klient og kalder aldrig `.subscribe()`. **Ikke NgRx** — bevidst fravalg.

**En store-metode der sætter et signal og derefter genindlæser, skal værne mod svar i forkert
rækkefølge.** To genindlæsninger kan være i luften på én gang — `setShowCompleted` og
`setShowSomeday` gjorde netop det, og ankom det ældste svar sidst, overskrev det den nyeste
liste. `load()` har derfor en sekvenstæller: kun det nyeste load må skrive listen.

**En delt `<ng-template>` med `let-`-variabler har konteksttype `any`.** `strictTemplates`
tjekker den ikke, og `[ngTemplateOutletContext]` afstemmes ikke. Skal en række være
typetjekket, skal den være en komponent med `input()`. Bruger den `<li>`, så giv den en
attributvælger (`li[appTaskRow]`) — et eget element ville bryde `divide-y` og listens struktur.

**`@if` indsnævrer ikke et signal-kald.** `@if (task().x != null)` efterlader `task().x` som
`T | undefined` inde i blokken. Bind med `@let` først. Brug **ikke** `as`, som binder på
sandhed og taber `0`.

**`strict` kan slås fra i en `tsconfig.app.json` uden at nogen bygning klager.** `extends`
lader barnet skygge for basen, og `ng build` blev grønt med `"strict": false` derinde — målt.
`FrontendStrictnessTests` er derfor vagten på både basen og de to børnekonfigurationer.

**En genvej udfører elementets aktiveringshandling, ikke bare fokus.** Det er
Windows-konventionen — og HTML's eget `accesskey` — så et tekstfelt får fokus, fordi det ikke har
andet at gøre, et afkrydsningsfelt skifter, en knap klikkes, et link følges. Gav genvejen kun
fokus, skulle brugeren trykke Alt+O og **derefter** Enter. Og et programmatisk `click()` flytter
**ikke** fokus, så en aktiverende genvej skal kalde `focus()` også: Windows flytter fokusringen
lige så meget som den handler. Direktivet har derfor
`appShortcutAction="focus" | "activate"`, hvor `'activate'` kalder begge.

**Bogstaverne er `Alt+O/I/S/N/V/M`**, valgt udenom `Alt+D`, `Alt+E`, `Alt+F`, `Alt+Home` og
piletasterne, som Chrome stjæler under udvikling. De er frie i Photino-vinduet, men en genvej der
virker i appen og ikke i browseren bliver fejlsøgt i den forkerte ende.

**`Ctrl+Alt` er AltGr på et dansk tastatur.** En global `Alt+bogstav`-lytter skal tjekke
`!event.ctrlKey && !event.metaKey`, ellers kan brugeren ikke skrive `@`, `£` eller `$` — en fejl
der dukker op uger senere som "appen æder mine tegn". Og kald kun `preventDefault()` når tasten
faktisk blev håndteret, så uhåndterede kombinationer stadig når browseren og styresystemet.

**En mærkat inde i et link eller en label indgår i elementets tilgængelige navn**, medmindre den
bærer `aria-hidden="true"`. Det betyder noget her, fordi E2E-suiten matcher tilgængelige navne i
deres helhed — `TaskListScreen.RowTitled` matcher rækkeknappens navn præcist. Tastaturhintet når
hjælpemidler gennem `aria-keyshortcuts` i stedet; den synlige mærkat er kun for øjet.

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
- **Kontrast måles i browseren** med `getComputedStyle`, fordi kun browseren har afgjort hvilken
  baggrund et stykke tekst endte på. `ContrastTests` går appens **tre** skærme igennem i begge
  farvetemaer — `app.routes.ts` har præcis tre ruter: opgavelisten, importen og indstillingerne
  — og måler derudover det **udvidede detaljepanel**, hvor noten, underopgaverne og
  statusvælgeren bor. Panelet er en tilstand på opgavelisten, ikke en fjerde skærm; tæl det
  ikke som en.
- **`getComputedStyle` giver `oklch(...)`, ikke `rgb(...)`** for farver skrevet i oklch. En regex
  over cifrene læser `oklch(0.967 0.003 264.542)` som en blå kanal på 264 — og vagtens første
  udgave gav derfor **usynlig tekst omkring 8:1 og bestod**. Mal farven på et 1×1-canvas og læs
  pixlen tilbage.
- **En rettet baggrund kan gøre en vagt strengere, ikke mildere.** En uigennemsigtig flade
  stopper søgningen op gennem forældrene, så tekst inde i panelet havde været målt mod den
  forkerte farve. `dark:bg-gray-800` på detaljepanelet lukkede syv fejl og afdækkede syv nye;
  tallet blev stående på 15, mens der reelt blev gjort fremskridt. **Læs hvilke poster der
  ændrede sig, ikke antallet.**
- **Et felts værdi er ikke en tekstknude.** Kontrastgennemgangen samlede børnenes tekstknuder, og
  derfor havde `<textarea>` og `<input>` **ingen målbar tekst overhovedet** — at nå frem til
  noteeditoren var ikke det samme som at måle den. Bevist frem for antaget: editoren blev malet
  `text-gray-400 dark:text-gray-600` (2,49:1 og 1,94:1), og vagten bestod stadig. Den måler nu
  `el.value` for de felttyper der faktisk maler deres værdi, med en hvidliste — et
  afkrydsningsfelts værdi er strengen `"on"`, og at give feltet skylden for den ville være en
  fejl ingen kan se eller rette.
- **En locator der er sand ved et tilfælde, er ikke ærlig.** `span[aria-hidden="true"]` var
  tilfældigvis unik for genvejsmærkaterne, så optællingen ville have virket — indtil det første
  dekorative ikon pustede tallet op. Mærkaterne fik `data-testid="shortcut-badge"` i stedet, som
  navngiver tingen selv.
- **En udvidet vagt kan finde ingenting og stadig være værd at udvide.** At føre gennemgangen ud
  over noter, underopgaver, de analyserede importrækker og aliasrækkerne gav **nul** nye
  farvefejl — forudsigelsen om at `@tailwindcss/typography` ville fejle holdt ikke. Hvad den til
  gengæld afdækkede, var blindvinklen ovenfor. Læs ikke "ingen nye fejl" som "udvidelsen var
  spildt".

## Testtal

Efter skive 8: **33** Todo.Core.Tests, **109** Todo.Api.Tests, **22** Todo.E2E, **139** Vitest.
Et ændret tal efter en refaktorering betyder, at en test er tabt eller duplikeret.
Skive 8 lagde **otte** E2E-tests til (14 → 22) — mærkaterne, de seks genveje og AltGr — og **fem**
Vitest-tests (134 → 139) på `ShortcutStore`.
Vitest gik fra 133 til 134 i skive 7 — ikke af tilgængelighedsarbejdet, men af regressionstesten
for `TaskStore`-fejlen, hvor to loads i luften på én gang kunne lade det ældste svar overskrive
den nyeste liste. Og E2E gik fra 12 til 14 i samme skive, fordi kontrastvagtens dækning blev
udvidet efter første gennemløb: den tomme liste er en skærmtilstand rejsen ikke kan nå, og den
blev lagt til som en `[Theory]` over begge farvetemaer. Se 12 og 133 i ældre rapporter som
forældede, ikke som tabte tests.
