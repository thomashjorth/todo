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
- **`curl` i PowerShell 5.1 er et alias for `Invoke-WebRequest`** og tager helt andre flag —
  `-s` bliver læst som `-SessionVariable` og fejler med "Missing an argument". Kald `curl.exe`
  eksplicit, og husk at escapes også skifter: `NUL` frem for `/dev/null`, backtick-n frem for
  `\n`. Samme klasse af fælde som `npm` mod `npm.cmd`. Og PS 5.1 **kaster** på en HTTP-fejlkode,
  så en `Invoke-WebRequest` der skal vise en 401 skal pakkes i `try`/`catch`.
- **Læg aldrig et token direkte i en kommandolinje.** Sæt det i `$env:NAVN` først og referér det
  — ellers følger det med i fejlbeskeder, historik og enhver kopiering af kommandoen. PowerShell
  klippede et PAT efter to tegn i en fejlbesked her, og det var held, ikke design.
- **Antivirussen (AMSI) blokerer visse PowerShell-scripts**, især `Start-Process` kombineret
  med `Invoke-WebRequest`. Del kommandoen op, eller brug `curl` gennem Bash-værktøjet.
- **Ingen `"` i en `git commit -m`-heredoc.** PowerShell citerer om for native kommandoer, og
  et anførselstegn afslutter argumentet midt i beskeden.
- **`@'…'@` er PowerShells here-string, ikke Bash'.** Bruges den gennem Bash-værktøjet, sender Git
  Bash `@`-afgrænserne videre som **almindelig tekst**, så commit-emnet bliver `@ ✨ … @`. Fejlen er
  tavs — `git commit` lykkes — og opdages først når nogen læser historikken. Vælg syntaks efter det
  værktøj du kalder, ikke efter hvad du skrev sidst, og verificér et emne med gitmoji **på
  byte-niveau** (`git log --format=%s -1 | od -c`) frem for på skærmen, hvor en forkert kodning
  ligner det rigtige.
- **EF-værktøjet er `dotnet tool run dotnet-ef`, aldrig `dotnet ef`.** En global `dotnet-ef`
  7.0.16 ligger på maskinen og kan ikke læse en EF Core 10-model.
- **Kør scripts fra repo-roden.** `dotnet tool restore` læser sit manifest fra den aktuelle
  mappe og henter ellers et andet repos værktøjer.
- **Find din egen `Todo.Host` på porten, ikke på navnet.**
  `Get-NetTCPConnection -LocalPort <port> -State Listen` → `Stop-Process -Id` på det ene PID.
  Brugeren har ofte appen åben, og under Swagger-linket kørte der to processer på én gang —
  brugerens vindue og en probe. Et `Stop-Process -Name Todo.Host` ville have lukket begge.
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

**Appen har to OpenAPI-dokumenter, og det er med vilje.** `/openapi/v1.json` er runtime-afledningen,
som drift-testen måler imod; `/openapi/contract.yaml` er kontrakten selv, indlejret i `Todo.Host`,
og det er den dokumentationssiden på `/scalar/` viser. Ryd ikke den ene op som en dublet — se
designdokumentets afsnit 10.

**En umappet rute under `/api/` fejler tre forskellige måder, og ingen af dem er 404.** Målt i skive
11, mens fire endpoints endnu ikke fandtes: en **POST** giver `405 Method Not Allowed`, fordi
`MapFallbackToFile("index.html")` gør krav på stien for GET og dermed gør metoden — ikke stien —
til det der mangler. En **GET** giver **`200` med `index.html` i kroppen**, så en test der venter
JSON fejler med `'<' is an invalid start of a value`. Og et **PUT** giver 405 af samme grund som
POST'en. Forvent derfor ikke 404 når du skriver en rute-test der skal fejle først; mål hvad der
faktisk kommer, ellers jagter du en halvt eksisterende rute der ikke findes.

**En minimal API uden for `/api/` skal have `.ExcludeFromDescription()`.** ASP.NET Core beskriver
hver rute i `/openapi/v1.json` uanset præfiks, så uden kaldet dukker den op som en operation for
meget, og `ContractDriftTests` fejler på et mismatch der **ligner** kontraktdrift og er et
manglende kald. Målt, ikke gættet — se designdokumentets afsnit 10.

**En rutebinding er id-typens anden halvdel, og den er usynlig for både `grep` og compileren.**
`TaskEndpoints.cs` havde seks `Guid`-linjer **og syv `:guid`-bindinger** i skabeloner som
`"/api/tasks/{id:guid}"`. `grep -c Guid` er versalfølsom og så ingen af bindingerne — og
**compileren ser dem heller ikke**, fordi en minimal-APIs rutebinding afgøres i runtime. `long id`
mod `{id:guid}` bygger derfor grønt og svarer **404 fra routingen, før handleren**, hvilket ligner
"opgaven findes ikke". Glemmes *begge* halvdele på én rute, virker den stadig, så testene siger
heller ingenting. Skifter du id-typen, så ret begge stavemåder og grep efter **begge**, før og
efter.

**En lokal side må ikke kalde ud, og statisk gennemsyn beviser ikke at den ikke gør.** Appen kører
på maskinen og kan være uden netværk. Dokumentationssidens HTML henviste ikke til nogen fremmed
vært, og bundlen var gennemsøgt for CDN-navne — begge tjek bestod alligevel, mens siden hentede
`api.scalar.com/vector/registry/*` fra JavaScript **efter mount** (Scalars "Ask AI"-knap, lukket med
`.DisableAgent()`), og bundlens `@font-face` pegede på `fonts.scalar.com` (lukket med
`.DisableDefaultFonts()`). Kun en vagt på rute-niveau, der blokerer alt uden for appens egen origin
og fastslår at intet blev **forsøgt**, ser den slags. Vagten er `ApiDocsJourneyTests`.

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

**Ingen af de to Angular-bygninger typetjekker spec-filerne.** `ng build` compilerer
`tsconfig.app.json`, som **ekskluderer `src/**/*.spec.ts`**, og `ng test` kører gennem esbuild, der
fjerner typerne uden at tjekke dem. En `string` lagt i et `number`-felt inde i en spec-fil var
derfor grøn for evigt — sådan blev et fixture-id ved med at være `` `${bucket}-1` ``, efter feltet
blev et tal. Hullet er lukket af `FrontendStrictnessTests.Spec_project_passes_the_type_checker`,
som kører `tsc -p tsconfig.spec.json --noEmit` gennem den lokale `node_modules\.bin\tsc.cmd` med
`src\Todo.Web` som arbejdsmappe (relative stier i tsconfig'en opløses ellers ikke) og kræver
exitkode 0, med compilerens diagnostik i fejlbeskeden. **En compilering af ingenting giver også
exitkode 0**, så vagten kører med `--listFiles` og påstår desuden, at der var mindst én `*.spec.ts`
i filsættet. Klassen vagter altså både **flagene** i de tre tsconfigs og selve **kørslen**. Prisen
er, at `dotnet test` nu forudsætter installerede `node_modules`; mangler `tsc.cmd`, siger vagten
det med navn og sti frem for at kaste en `Win32Exception`.

**En genvej udfører elementets aktiveringshandling, ikke bare fokus.** Det er
Windows-konventionen — og HTML's eget `accesskey` — så et tekstfelt får fokus, fordi det ikke har
andet at gøre, et afkrydsningsfelt skifter, en knap klikkes, et link følges. Gav genvejen kun
fokus, skulle brugeren trykke Alt+O og **derefter** Enter. Og et programmatisk `click()` flytter
**ikke** fokus, så en aktiverende genvej skal kalde `focus()` også: Windows flytter fokusringen
lige så meget som den handler. Direktivet har derfor
`appShortcutAction="focus" | "activate"`, hvor `'activate'` kalder begge.

**Bogstaverne er `Alt+O/I/J/S/N/V/M`**, valgt udenom `Alt+D`, `Alt+E`, `Alt+F`, `Alt+Home` og
piletasterne, som Chrome stjæler under udvikling. De er frie i Photino-vinduet, men en genvej der
virker i appen og ikke i browseren bliver fejlsøgt i den forkerte ende. `J` kom til med
Jira-importen i skive 11 og kolliderer ikke: Chrome på Windows binder intet `Alt+J`, og den nære
nabo er DevTools-konsollen, som er `Ctrl+Shift+J` — et andet modifikatorsæt. (På macOS er den
`Cmd+Option+J`, men appen er Windows-only.) **Registret er last-writer-wins**, så bogstaverne skal
blive ved at være globalt unikke; se designdokumentets afsnit 10.

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

**Datoer.** `Deadline` er `DateOnly`, og `DeferUntil` — startdatoen — er `DateOnly` af samme
grund. Tidsstempler er `DateTime` i UTC — **aldrig `DateTimeOffset`**, som SQLite ikke kan
sortere korrekt. En deadline må aldrig gennem `new Date(string)`; det parses som UTC-midnat og
kan vise dagen før.

**System.Text.Json binder kun offsets i *udvidet* form, og Jira sender den *basale*.** Et
`DateTimeOffset`-felt på en DTO kaster på `+0200` og kræver `+02:00` — målt mod Jiras changelog,
hvor beskeden er `The JSON value is not in a supported DateTimeOffset format`. Løsningen er at binde
feltet som `string` og køre `DateTimeOffset.TryParse` med `InvariantCulture`, som tager **begge**
former, og derefter `.UtcDateTime`. **Og fælden er dobbelt:** typer man samme felt som
`DateTimeOffset` i sin *falske* server, udsender den den udvidede form, koden bliver grøn, og først
den rigtige instans kaster — på hver enkelt sag. En falsk server skal udsende fremmedsystemets
faktiske format, ikke .NET's; ellers måler den sig selv.

**Et fremmedsystems feltnavn skal have et eksplicit `[JsonPropertyName]`, ikke en navnepolitik.**
Jira staver `duedate` i ét ord, og den camelCase-politik der ellers gælder ledte efter `dueDate`,
læste feltet som fraværende og gav **null i hver deadline** — uden at nogen test faldt, fordi der
findes en test for en sag *uden* deadline. Målt i skive 11. Bind hvert felt fra en ekstern kilde
eksplicit, og lad en test have en værdi i feltet frem for kun at dække fraværet.

**Wiki-markups `*x*` er fed, markdowns er kursiv** — de to sprog bruger samme tegn til hver sin
betydning, så en tegn-for-tegn-oversættelse vender betoningen på hovedet. Og `----` må **ikke**
blive `---`: markdown læser en linje bindestreger *under* et afsnit som en **setext-overskrift**, så
stregen forsvinder og afsnittet over den bliver en H2. Skriv `***` i stedet. Begge fund kom af at
føre konverterens output gennem appens egen `marked` og læse HTML'en — ikke af at læse regexerne,
som så rigtige ud i begge tilfælde. **Test en markup-konverter på det renderede resultat**, ikke på
mellemformen.

**En tom projektnøgle må ikke betyde alle projekter.** PAT'en ser fire projekter — `EC`, `KK`,
`SAAS`, `TTMBP` — så en JQL uden `project = …` trækker kundeprojektet `KK` ind i den personlige
liste. Projektnøglen er derfor et **krav** med sin egen fejlkode (`jira.projectKeyRequired`), ikke
et valgfrit filter der falder tilbage på alt. Samme klasse af fælde som et tomt filter i en
databaseforespørgsel: fraværet af en afgrænsning er ikke en neutral standard.

**Et hemmeligt felt skal have sit eget endpoint.** `PUT /api/settings` er en fuld erstatning der
læser et fraværende felt som "ryd", og tokenet kan aldrig sendes *tilbage* til klienten — så en
klient der gemmer noget andet på siden, ville rydde tokenet hver gang. Tokenet har derfor
`PUT`/`DELETE /api/jira/token` for sig, og `SettingsResponse` bærer kun `hasJiraToken`. Det er
samme fælde som `TaskStore.update`, med den forskel at her kan `current`-tricket ikke bruges.

**Udskudtheden er beregnet, ikke gemt.** `DeadlineBuckets.For` svarer `Deferred`, når `DeferUntil`
ligger *strengt efter* i dag — der er ingen `Deferred`-status på `TodoStatus`, og **intet skal
køre ved midnat**: opgaven kommer tilbage, fordi uret siger noget andet i morgen. Grenenes
rækkefølge i metoden er load-bearing: `Overdue` slår `Deferred`, fordi det er værre at skjule et
tilsagn du allerede har brudt end at vise noget tidligere end planlagt. Byt dem ikke om — og
bemærk, at gættet uden begrundelsen falder den anden vej.

**Id'er er `long` og tildeles af SQLite.** `Id` på `TaskItem`, `SubTask` og `UserAlias` er
`INTEGER PRIMARY KEY AUTOINCREMENT`, altså et alias for rowid, så "opgave 42" kan siges højt.
Entiteten sætter **ikke** et id, og klienten sender ikke et.

**En TEXT-til-INTEGER-konvertering i SQLite er en ommapning, ikke en typeændring.** En `CAST` af en
Guid-streng læser et ledende talpræfiks og giver ellers `0`: målt blev fem distinkte Guid'er **to**
distinkte heltal, hvoraf **tre** var `0` — sammenfaldende primærnøgler. Skriv sådan en migrering i
hånden med en mapningstabel pr. tabel og `ROW_NUMBER()`. **EF's eget scaffold er den destruktive
vej og siger det selv**: `dotnet-ef migrations add` gav fire `AlterColumn<long>` på
TEXT-primærnøgler plus advarslen "An operation was scaffolded that may result in the loss of data."
Behold modelsnapshottet, skriv `Up` og `Down` selv.

**`PRAGMA foreign_key_check` beviser næsten ingenting alene.** Fremmednøgler er slået **til** under
en migrering — `PRAGMA foreign_keys` er en no-op inde i en transaktion, og EF pakker migreringen i
én, så håndhævelsen kan ikke slås fra — og en ommapning til en forælder der ikke findes afvises
derfor på stedet med `FOREIGN KEY constraint failed`. SQLite kommer først og siger det tydeligere
end nogen gennemgang. Og bygges den nye tabel **uden** fremmednøglen, har `foreign_key_check` ingen
regel at tjekke og melder også nul. Gennemgangen er kun noget værd ved siden af en påstand om at
reglen **findes** — `pragma_foreign_key_list('SubTasks')` skal give
`Tasks: TaskItemId -> Id ON DELETE CASCADE`. Konsekvensen for migreringen: **forældre før børn er
ikke ordentlighed, det er den eneste rækkefølge der kører** — og børn ud før forældre den anden vej.

**Et indeks følger sin tabel gennem `ALTER TABLE … RENAME TO` og beholder sit navn.** Omdøber du
`Tasks` til `Tasks_old`, hedder indekset stadig `IX_Tasks_Deadline` og ligger nu på `Tasks_old`, så
et `CREATE INDEX` med samme navn fejler. Opret derfor indeksene **til sidst**, efter at de gamle
tabeller er droppet.

**Backenden læser et fraværende felt som "ryd".** `PUT /api/tasks/{id}` er en fuld erstatning, så
`TaskStore.update` skal bære **hvert** felt med i sit `current`-objekt. Mangler ét, sletter enhver
redigering af noget andet det lydløst — sådan tabte en gemt `DeferUntil` sig selv, når man rettede
deadlinen. Lægger du et felt på kontrakten, så læg det også i `current`.

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
  en hel skive, før en sådan test blev skrevet. **En ny enum-værdi er samme sag**: `deferred` på
  `DeadlineBucket` kan ikke få drift-testen til at fejle, uanset hvordan den serialiseres, så den
  har sin egen påstand — `"bucket":"deferred"` — i
  `Wire_format_uses_the_names_the_contract_declares`. Læg en ny værdi der, ikke kun i kontrakten.
- **En håndskrevet migrering skal have en vagt på *kolonnemængden*, ikke kun på rækkerne.**
  `LongIds` navngiver hver kolonne eksplicit i sin `CREATE TABLE` og sit `INSERT … SELECT`, og
  SQLite fjerner en kolonne uden at sige noget. Den første vagt såede fem af `Tasks`' tretten
  kolonner, så en kolonne fjernet fra `SELECT`-listen bestod. Præcis det skete: `DeferUntil`
  fandtes ikke da planen blev skrevet, skive 9 lagde den på, og den håndskrevne SQL kendte den
  ikke — fanget ved at måle planen igen, ikke af en test. Derfor **tre** påstande, som fanger
  forskellige fejl: `Every_column_of_every_table_survives_the_migration` sammenligner
  navnemængden før og efter, `Every_column_the_model_expects_exists_in_the_database` sammenligner
  databasen med EF's model, og `Every_field_of_a_fully_populated_row_survives_the_migration` sår
  én fuldt udfyldt række pr. tabel og sammenligner felt for felt. Set fejle hver for sig: en
  ombytning af `Note` og `Requester` i `SELECT`-listen fælder **kun** den værdibaserede, fordi
  alle kolonner stadig findes. Værdiernes distinkthed er bærende — stod der `"x"` i to kolonner,
  ville ombytningen bestå. Sammenlign **navnemængden**, ikke rækkefølgen: kolonneordenen efter en
  ombygning er den `CREATE TABLE` nævner dem i, og at pinne den ville gøre en harmløs omrokering
  rød.
- **En før/efter-sammenligning over en migrering er blind for en kolonne der mangler i *begge*
  retninger.** Før-billedet er lavet af migreringens egen `Down`, så den sammenligner migreringen
  med sig selv, og de to mængder bliver enige om det forkerte. Målt: fjernes `DeferUntil` fra
  både `Up` og `Down`, **består** `Every_column_of_every_table_survives_the_migration` — og det
  var netop den symmetriske form den rigtige fejl havde. Den holdbare påstand er derfor
  **databasen mod EF-modellen**: `pragma_table_info` op mod
  `entityType.GetProperties().Select(p => p.GetColumnName())`, drevet af entitetstyperne, så
  parringen type → tabel ikke skal skrives ned nogen steder. `PendingModelChangesWarning` dækker
  det ikke, og `Assert.Empty(pending)` heller ikke: advarslen sammenligner model-*snapshottet* med
  *modellen*, og snapshottet er genereret **ud fra** modellen, så de er enige uanset hvad den
  håndskrevne SQL siger. Databasen er den ingen ellers sammenligner. Den skal køre på en **frisk**
  database frem for på seedings-fixturet: mangler kolonnen i skemaet, vælter `INSERT`en først, og
  fejlen peger så på testens SQL i stedet for på migreringens.
- **En mængdesammenligning kan bestå på ingenting.** Et stavefejlet tabelnavn giver
  `pragma_table_info` nul rækker i *begge* ender, og to tomme mængder er ens. Vagten kræver
  derfor også, at `Id` står i mængden fra før migreringen. Model-mod-database-vagten er udsat fra
  to sider mere — et `FindEntityType` der svarer `null`, og en halv `MigratedEntities` der gør
  hele løkken til en no-op — så den kræver `Id` i **begge** mængder og at de tabelnavne løkken
  faktisk nåede er alle tre.
- **Builders er til *arrange*.** De skriver direkte i databasen og springer API'ets validering
  over; brug dem aldrig til selve handlingen en test skal verificere.
- **Et databasegenereret id findes først efter `SaveChanges`.** Læser en test `task.Id` før den
  gemmer, får den `0` — og `0` er ikke et rigtigt id, for et rowid starter på 1.
  `AddAndSaveChangesAsync` gemmer, så id'et er gyldigt bagefter, men et builder-objekt der aldrig
  blev gemt har ingenting at give.
- **E2E-suiten bygger ikke Angular, så den kan være grøn på en frontend der ikke findes længere.**
  Målt i skive 11: `Todo.E2E.csproj` har **intet** build-trin, og hosten servérer bare `wwwroot`
  gennem `UseStaticFiles` og `MapFallbackToFile`. Kun `scripts/run-app.ps1` bygger, og kun når appen
  startes — den sammenligner `index.html`s `LastWriteTimeUtc` med den nyeste kilde. Ændrer du en
  Angular-fil og kører `dotnet test`, tester Playwright altså **den forrige udgave**, uden at noget
  ser forkert ud: suiten gør nøjagtigt hvad den skal, mod det forkerte input. **Kør
  `scripts\build-web.ps1` før E2E**, hver gang frontenden er rørt.
- **Sammenlign tidsstempler på epoch, aldrig på klokkeslæt.** `ls --time-style=+%H:%M:%S` sorteret
  som tekst gør `21:15` fra i går nyere end `13:14` fra i dag, og datoen er væk. Det fik en
  gennemgang her til at kalde `wwwroot` forældet, mens den var 36 sekunder frisk. Brug
  `stat -c %Y` eller `find -printf %T@`.
- **Tests må ikke røre `%APPDATA%`.** `RunningHost` giver hver test sin egen midlertidige
  database. Arv fra `ApiTest` eller `BrowserTest` frem for at starte en host i testen.
- **Playwright-tests må ikke have bivirkninger uden for appen.** Kald til
  `/api/system/open-link` opsnappes med `page.RouteAsync` og afbrydes; ellers åbner hver
  testkørsel en rigtig browser.
- **Kontrast måles i browseren** med `getComputedStyle`, fordi kun browseren har afgjort hvilken
  baggrund et stykke tekst endte på. `ContrastTests` går appens **fire** skærme igennem i begge
  farvetemaer — `app.routes.ts` har præcis fire ruter: opgavelisten, retro-importen, Jira-importen
  og indstillingerne — og måler derudover det **udvidede detaljepanel**, hvor noten, underopgaverne
  og statusvælgeren bor. Panelet er en tilstand på opgavelisten, ikke en femte skærm; tæl det
  ikke som en. **En `@if`-gren er umålt, indtil fixturet har en opgave i den tilstand og rejsen
  åbner rækken** — vagten kan ikke se en farve, der aldrig blev renderet, så en ny betinget linje
  koster en fixture-opgave og en klik-og-vent i `ContrastTests`. Hintet om en startdato efter
  deadline kom ind i vagten netop derfor, og blev set fejle i begge temaer.
- **En gren bag et fremmedsystem er umålt, indtil kaldet opsnappes.** Skive 11 efterlod **elleve**
  `@if`-grene som ingen farve nogensinde blev renderet i, fordi hver af dem kræver et svar fra Jira.
  Playwright kan ikke starte en `FakeJira` inde i hostens proces, så `ContrastTests` svarer selv på
  `**/api/jira/preview`, `**/api/jira/test` og `**/api/jira/statuses` med `page.RouteAsync` — samme
  greb som afbrydelsen af `/api/system/open-link`. Fire forskellige svar i rækkefølge er nødvendige,
  fordi grenene udelukker hinanden: en afvisning, en tom liste, en liste hvor hver række er
  blokeret, og en liste med én række der kan importeres. **Én rute-handler der læser en variabel**,
  ikke fire handlere, hvis præcedens ellers ville afgøre udfaldet.
- **En link-gren der hænger på et felt ingen builder kan sætte, er usynlig for enhver vagt.**
  `externalUrl` beregnes kun når `SourceId == JiraTaskSource.Id`, og `TaskItemBuilder` havde
  `FromRetro` men **ingen `FromJira`** — så `external-link` blev aldrig renderet i en eneste test,
  og både kontrastvagten og en link-påstand ville have målt et element der ikke fandtes. Målt:
  fjernes `FromJira` fra fixturet igen, siger Playwright `element(s) not found 'Åbn sagen'`.
  **Og linket kræver to ting, ikke én:** kilden *og* en gemt `jira.baseUrl`, for URL'en er beregnet
  af basisURL'en. Mangler den, er grenen stadig tom, uanset hvordan kilden er stavet.
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

Efter skive 11 (Jira-import): **83** Todo.Core.Tests, **168** Todo.Api.Tests, **32** Todo.E2E,
**178** Vitest.
Et ændret tal efter en refaktorering betyder, at en test er tabt eller duplikeret.
Skiven lagde **45** Core-tests til (38 → 83), **47** Api-tests (121 → 168), **7** E2E (25 → 32) og
**35** Vitest (143 → 178). Fordelingen er værd at læse, for den siger hvor arbejdet lå:
wiki-markup-konverteringen, `DeadlineBuckets` uberørt og `JiraSettings` er alle rene funktioner og
hører i Core, mens `JiraTaskSource` måles mod en **falsk Jira på loopback** og derfor tæller som
Api-tests sammen med de fire nye endpoints og kontraktens wire-format.
**E2E stod stille gennem skivens første ni opgaver og flyttede sig først i den tiende**, som var
vagterne alene: fire i `JiraImportJourneyTests` (den ukonfigurerede skærm, de to blokerede rækker med
hver sin grund, at importen kun sender de valgte, og at linket beder `/api/system/open-link` om
`…/browse/SAAS-1` fra en `BUTTON`), én `Alt_J_follows_the_jira_link` i `KeyboardJourneyTests`, og
**to** af `The_Jira_screens_meet_WCAG_AA`, som er en `[Theory]` over de to farvetemaer. Den
rækkefølge — funktionen i nio opgaver, vagterne i den tiende — er også grunden til at skiven
efterlod elleve umålte `@if`-grene: en skærm bygget færdig uden en E2E-rejse ved siden af har ingen
måde at vise, at dens betingede linjer aldrig blev renderet.
Før skiven, efter typetjek-vagten på spec-projektet: 38 Core, 121 Api, 25 E2E, 143 Vitest.
Den lagde **én** Api-test til (120 → 121), `Spec_project_passes_the_type_checker` i
`FrontendStrictnessTests`. Den koster et par sekunder af `dotnet test`, fordi den starter en rigtig
compiler. De øvrige tre tal står stille: vagten er en påstand om værktøjskæden og rørte ingen kode
— den blev set fejle med det ægte brud, `` id: `${bucket}-1` `` i `task-store.spec.ts`, og
diagnostikken `TS2322` stod i fejlbeskeden.
Før den, efter model-mod-database-vagten: 38 Core, 120 Api, 25 E2E, 143 Vitest.
Den lagde **én** Api-test til (119 → 120), `Every_column_the_model_expects_exists_in_the_database`
i `LongIdMigrationTests`. Begge de to ældre kolonnepåstande blev beholdt: de fejler forskelligt, og
før/efter-udgaven lokaliserer en ændring til migreringen frem for til modellen. De øvrige tre tal
står stille: vagten er en påstand om skemaet og rørte hverken kontrakten, kernen eller frontenden.
Før den, efter kolonnevagten på migreringen: 38 Core, 119 Api, 25 E2E, 143 Vitest.
Kolonnevagten lagde **to** Api-tests til (117 → 119), begge i `LongIdMigrationTests`:
`Every_column_of_every_table_survives_the_migration` og
`Every_field_of_a_fully_populated_row_survives_the_migration`. Delt i to med vilje, fordi de
fanger forskellige fejl og skal kunne ses fejle hver for sig — en ombytning i `SELECT`-listen
fælder kun den anden. De øvrige tre tal står stille: vagten rørte hverken kontrakten, kernen
eller frontenden.
Før den, efter skive 10: 38 Core, 117 Api, 25 E2E, 143 Vitest.
Skive 10 lagde **to** Api-tests til (115 → 117), begge i `LongIdMigrationTests`:
`Guid_era_rows_survive_the_migration_to_long_ids`, som sår rigtige Guid-rækker gennem den forrige
migrering og kræver dem intakte bagefter, og
`Casting_a_Guid_to_an_integer_collapses_distinct_ids`, der fastholder selve `CAST`-sammenfaldet:
holder begrundelsen for den håndskrevne migrering op med at gælde, får nogen det at vide. De øvrige
tre tal står stille: skiven skiftede en type frem for at tilføje adfærd, og de 143 Vitest er de
samme specs med tal frem for strenge i deres fixtures.
Før den, efter hintet om en startdato efter deadline: 38 Core, 115 Api, 25 E2E, 143 Vitest.
Hintet lagde **to** Vitest til (141 → 143): at panelet siger det, og at grænsen — en startdato
*på* deadlinen — ikke er en konflikt. De øvrige tre tal står stille: hintet fik ingen ny logik
bag kontrakten, og `ContrastTests` voksede med en fixture-opgave og en klik-og-vent frem for
med en test.
Efter skive 9: 38 Core, 115 Api, 25 E2E, 141 Vitest.
Skive 9 lagde **fem** Core-tests til (33 → 38) — hele grænsefladen om startdatoen i
`DeadlineBucketsTests`, inklusive at dagen en opgave begynder ikke er udskudt, og at Overskredet
slår Udskudt — **fire** Api-tests (111 → 115), **én** E2E (24 → 25, `DeferUntilJourneyTests`) og
**to** Vitest (139 → 141): sektionens plads sidst i `bucketOrder`, og regressionen på at
`TaskStore.update` bærer `deferUntil` med.
Før den, efter Swagger-linket: 33 Core, 111 Api, 24 E2E, 139 Vitest.
Api gik fra 109 til 111 med de to `ContractDocumentTests`, og E2E fra 22 til 24 med de to
`ApiDocsJourneyTests`. Vitest stod stille: linket fik ingen ny frontend-logik.
Skive 8 lagde **otte** E2E-tests til (14 → 22) — mærkaterne, de seks genveje og AltGr — og **fem**
Vitest-tests (134 → 139) på `ShortcutStore`.
Vitest gik fra 133 til 134 i skive 7 — ikke af tilgængelighedsarbejdet, men af regressionstesten
for `TaskStore`-fejlen, hvor to loads i luften på én gang kunne lade det ældste svar overskrive
den nyeste liste. Og E2E gik fra 12 til 14 i samme skive, fordi kontrastvagtens dækning blev
udvidet efter første gennemløb: den tomme liste er en skærmtilstand rejsen ikke kan nå, og den
blev lagt til som en `[Theory]` over begge farvetemaer. Se 12 og 133 i ældre rapporter som
forældede, ikke som tabte tests.
