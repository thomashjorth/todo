# CLAUDE.md

Personlig todo-app. Én Photino-proces: ASP.NET Core + Angular i ét vindue, SQLite i
`%APPDATA%\TodoApp\`.

**Hvor tingene står:** design og leveranceplan i `docs/plans/2026-08-13-todo-app-design.md`; aktuel
tilstand, næste skridt og de målinger kun brugeren kan lave i `docs/HANDOFF.md`; hvordan man bruger
appen i `README.md`. Denne fil er konventioner og målte fælder — den er auto-indlæst i hver session,
så alt der lægges her betales i hver samtale. **Skriv kun det ned der ikke kan udledes af koden**, og
især ikke tal der forældes; se "Testtal" nederst for hvad det kostede sidst.

## Tre kommandoer

```
Todo.cmd        starter appen
Check.cmd       kører alt, i den rækkefølge der er bærende
Publish.cmd     bygger exe'en til publish\ og prøver den bagefter
```

Alle tre findes for at slippe for at huske `-ExecutionPolicy Bypass`; scripterne bag dem ligger i
`scripts\`. `Todo.cmd` bygger Angular hvis kilderne er nyere end `wwwroot`, og tager `--headless`
med for at køre uden vindue.

`Publish.cmd` udgiver self-contained til `publish\` (gitignoreret) — **to filer**, exe'en og
`icon.ico`, som Photino skal have som sti — og **prøver derefter exe'en**: headless på en fri port
mod en midlertidig database, og 200 krævet på frontenden, health og dokumentationssiden. Den nægter
at overskrive en exe der kører, og navngiver processen frem for at dræbe den. `-OutputPath <mappe>`
når du vil installere et blivende sted.

### Hvorfor `Check.cmd`s rækkefølge er bærende

Angular-bygning, `dotnet test Todo.sln`, Vitest — og stop på det første der fejler, med navnet på
trinnet og kommandoen der kører det alene. **E2E-suiten bygger ikke Angular**, så uden bygningen
først tester Playwright den forrige udgave af frontenden, uden at noget ser forkert ud. Prettier- og
linjeskiftsvagten kører inde i `dotnet test` og behøver ikke et trin for sig — verificeret, ikke
antaget: `FrontendFormattingTests` kalder prettier, `LineEndingTests` kalder `git ls-files`, og begge
ligger i `Todo.Api.Tests`.

Trinnene hver for sig, når man kun skal have det ene:

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
- **`ConvertFrom-Json` i PowerShell 5.1 kaster på JSON med to nøgler der kun afviger i
  versalfølsomhed.** Beskeden er `Cannot process argument because the value of argument "name" is not
  valid`, og den ligner alt andet end årsagen. Målt mod ADO's `_apis/wit/workitemtypes`. Den nære fælde
  er, at et **401** på samme endpoint giver `Ugyldig JSON-primitiv` — fordi svaret så er en HTML-side —
  så de to fejl står side om side og har intet med hinanden at gøre. Læs data et andet sted, eller
  undgå at pipe gennem `ConvertFrom-Json`.
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
- **Prettier er nu en vagt, og `--check .` på hele repoet er den rigtige kommando.** Det var den
  ikke før: `.prettierrc` satte ikke `endOfLine`, så standarden `lf` gjorde **hver** fil i denne
  CRLF-arbejdskopi til en "style issue", en fuld kørsel omskrev 3810 linjer genereret klientkode,
  og rådet var derfor at navngive filer eksplicit. Målt før vagten blev skrevet:
  `--end-of-line crlf --check .` gav **28** filer, `--end-of-line auto` gav **10** — altså var
  **18 af de 28 ren linjeskiftsstøj**, og ti filer havde ægte afvigelser. Rettelsen er
  `"endOfLine": "auto"` i `.prettierrc`, som bevarer filens eksisterende linjeskift og gør tjekket
  linjeskifts-agnostisk. **`auto` frem for `crlf`, fordi vagten skal være portabel:** CI kører på
  `windows-latest`, men `crlf` ville fejle på enhver LF-checkout, og en vagt der afhænger af
  `core.autocrlf`, fejler af den forkerte grund. Kommandoen er
  `.\node_modules\.bin\prettier.cmd --check .` fra `src\Todo.Web`, og
  `FrontendFormattingTests` kører den inde i `dotnet test`. Uden vagten kostede det to gange:
  **uddelegeringsleverancen efterlod fire rigtige afvigelser i tre filer** — en `<p>` der blev for
  lang, da `settings.html` fik et indrykningsniveau mere, en tom linje før et `</section>`, og to
  ombrudte kald — og **accordion-leverancen tolv kommentarlinjer over 100 tegn**.
- **`.prettierignore` holder kun genereret kode ude, og listen er opgjort — ikke gættet.**
  `src/app/api/todo-client.ts` (skrevet af `scripts\generate-api.ps1`), `package-lock.json`
  (skrevet af npm) og `dist/` + `.angular/` (byggeoutput, som `.gitignore` også dækker; navngivet
  alligevel, så vagten ikke afhænger af at prettier bliver ved med at læse `.gitignore` som
  standard). **`src/app/api/api-error-message.ts` ligger i samme mappe og er håndskrevet** — den
  skal blive i tjekkets rækkevidde, og vagten påstår netop det. Og bemærk hvad der **ikke** fanger
  en formateret generator-fil: `GeneratedCodeFreshnessTests` hasher `contracts/openapi.yaml`, ikke
  generatorens output, så den er **grøn** mens `todo-client.ts` er omformateret. Målt.
- **Brug ikke `sed -i` på en fil i dette repo — brug `Edit`.** `sed` skriver **LF** i en
  CRLF-arbejdskopi, og `git diff` viser derefter **ingen ændring**, fordi autocrlf normaliserer på
  vejen ind. Filen på disken *er* ændret, men Git siger nej, så en midlertidig ændring man tror er
  rullet tilbage, ligger der stadig. Samme klasse af tavs fejl som prettier-fælden ovenfor, men
  gennem et værktøj man bruger til enlinjers-rettelser. Målt i skive 11.
- **`element.dataset.testid` compilerer ikke i en spec-fil.** `noPropertyAccessFromIndexSignature` er
  slået til, så Angulars bygning stopper med `TS4111: Property 'testid' comes from an index signature, so
  it must be accessed with ['testid']`. Fejlen kommer fra `ng test`s egen bygning, ikke fra
  typetjek-vagten, og den er hurtig at møde — men beskeden peger på en indeks-signatur og ikke på flaget.
  Brug `getAttribute('data-testid')` eller elementets `id`.
- **Verificér commit-emner med `od` gennem Bash-værktøjet, aldrig gennem en PowerShell-pipe.** En
  pipe fra PowerShell til `od.exe` tilføjer en **UTF-8 BOM**, så emnet ser ud til at begynde med
  `357 273 277` foran gitmojien. Tallet er pipens, ikke commit'ens — og havde nogen troet på det,
  ville de have "rettet" en fejl der ikke fandtes. Målt i skive 11.
- **Én enkelt Vitest-fil køres med `--include` og en *sti*, ikke med et navn:**
  `npm.cmd run test --prefix src\Todo.Web -- --watch=false --include src/app/<mappe>/<fil>.spec.ts`.
  Målt: `--run <navn>` giver `Unknown argument: run`, og dropper man `--run`, giver den
  `Unknown argument: watch` — fordi `ng test`s positionelle argument er *projektnavnet*, og
  `@angular/build:unit-test` har slet ikke noget `--run`. Den anden fejlbesked peger altså på det
  forkerte flag.

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

**`Write`-værktøjet skriver LF i denne CRLF-arbejdskopi.** Samme klasse som `sed`-fælden, men gennem
et andet værktøj: `git diff` siger *"LF will be replaced by CRLF"*, diffen ser ærlig ud, og
arbejdskopien er alligevel ude af trit med indekset. Målt i uddelegeringsleverancen. Foretræk `Edit`
på en eksisterende fil, og skal `Write` bruges, så konvertér tilbage til CRLF og verificér med **nul
LF-only-linjer** plus et `od` på en linje med danske tegn.

**Men verificér den med `tr`, ikke med `grep` — `grep -cv $'\r$'` giver et falsk bestået.** Målt på et
minimalt tilfælde: en fil med **nul** CR-bytes får `grep -c $'\r$'` til at svare "alle linjer matcher"
og `grep -cv $'\r$'` til at svare **0**. Git Bash' grep læser i teksttilstand og har allerede fjernet
CR'erne, når mønsteret prøves, så `\r$` er sandt uanset hvad der står i filen. Netop den påstand —
"nul LF-only-linjer" — kan derfor **ikke fejle**, og den er den vagt afsnittet ovenfor beder om. Den
holdbare måling er `tr -dc '\r' < fil | wc -c` op mod `tr -dc '\n' < fil | wc -c`: er de ens, er filen
CRLF; er CR nul, er den LF. **Sådan slap begge planfiler igennem** — `docs/plans/2026-08-19-*.md` var
ren LF gennem hele leverancen, og hver `grep`-verificering undervejs sagde at de var i orden.

**Og fra formateringsvagten behøver ingen hånd-verificere det mere:** `LineEndingTests` i
`Todo.Api.Tests` er den vagt. Den bygger på `git ls-files --eol`, som rapporterer både indeks
(`i/`) og arbejdstræ (`w/`) som **Git** har beregnet dem — den ene kilde en teksttilstand ikke kan
snyde. To påstande: ingen fil er `mixed`, og hver tekstfils linjeskift i arbejdstræet er hvad Git
selv ville skrive. Forventningen er **ikke** udledt af `core.autocrlf`, men spurgt Git:
`git cat-file --filters` kører indeks-blobben gennem samme smudge-filter som en checkout, én probe
pr. `attr/`-mængde — for attributterne afgør det, og `*.cmd text eol=crlf` pinner de to filer til
CRLF selv på en maskine hvor alt andet er LF.

**Og her er den måling der omskriver resten af afsnittet: skaden var arbejdstræ-lokal, og der var
ingenting at committe.** Alle 255 tekstfiler er `i/lf` og har altid været det — `* text=auto` gør,
at Git normaliserer på vejen ind, og CRLF kan **ikke** committes. De 40 `w/lf`-filer og den ene
`w/mixed` var altså udelukkende arbejdskopiens tilstand, og rettelsen er **`git checkout -- .`**,
ikke en commit. Konsekvensen for vagten er værd at sige højt: **en frisk checkout, CI iberegnet,
kan ikke fejle den** — det er arbejdstræet tools skriver i, og en beskidt lokal kopi er det eneste
sted fejlen nogensinde har vist sig.

**`git update-index --refresh` rydder *ikke* det falske `M`** — det stod her, og det er forkert.
Målt: efter en LF→CRLF-omlægning svarer den `<fil>: needs update` for hver fil og exitkode **1**,
og `git status` bliver ved at vise ` M`. Grunden er at indekset cacher filens **størrelse på
disken**, som ændrer sig med linjeskiftene, så kun en indholdssammenligning kan rydde den — og
`git diff` er derfor **tom** mens `git status` siger `M`. `git checkout -- .` rydder begge, fordi
den skriver filen igen og cacher den nye stat. **Bemærk fælden i den rækkefølge:** `git checkout --`
kaster også enhver ucommittet ændring i filen væk. Ligger der en formatering du ikke har committet,
er den tabt uden en advarsel — mødt i formateringsleverancen, hvor `main.ts` gik tilbage til den
uformaterede udgave midt i en vagt-mutation.

**Pladsholderfarven ligger på `::placeholder`**, ikke på elementet, så en DOM-gennemgang der kun
læser `style.color` er blind for den — `getComputedStyle(el, '::placeholder')` skal spørges
særskilt. Og et felt **uden** en `placeholder-*`-klasse arver `currentColor` med omkring 54 %
alfa og fejler i begge temaer; en optælling af de klasser der står der, kan ikke se en klasse
der mangler.

**Men reglen om en `placeholder-*`-klasse er *betinget*, ikke absolut.** Vagten spørger kun om
`::placeholder`, hvis attributten står der — `TodoApp.cs` gør det bag
`el instanceof HTMLInputElement && el.placeholder` — så et felt **uden** en `placeholder` slipper
udenom, og det er derfor `alias-input` har stået uden klassen gennem hele suitens levetid uden at
fejle. Kravet følger altså attributten: giver du et felt en pladsholder, skal klassen med i samme
ombæring. Målt i uddelegeringsleverancen, hvor `waitingOn`-feltet bevidst ingen pladsholder fik og
derfor ingen farveflade tilføjede. **Hullet om `<textarea>` er lukket, og det var et rigtigt hul.**
Betingelsen nævnte kun `HTMLInputElement`, så en `<textarea placeholder="…">` blev **aldrig** målt;
den er nu `(el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) && el.placeholder`.
Ingen `<textarea>` i appen har en pladsholder i dag, så udvidelsen finder ingenting — men den er
**bevist** frem for skrevet ned som ubevist: en midlertidig `placeholder` på noteeditoren gav
`textarea[note-editor] placeholder … 3,38:1` i lyst og `4,46:1` i mørkt tema, og med den gamle
betingelse og **samme** pladsholder var vagten **grøn**. Prisen er kun, at grenen står umålt igen,
så snart nogen fjerner den ene betingelse.

**Bredde.** Appen bruges i en spalte på ~480 px, under Tailwinds `sm`-brydepunkt. De
uprefixede klasser **er** den smalle udgave; `sm:`/`md:` bruges kun til at udvide.

**Og fra `xl` er opgavelisten to spalter, hvor de andre fire skærme ikke er.** `app.html`s
`max-w-2xl` er hele appens loft, så `xl:max-w-none` hæver det og de fire andre skærme sætter deres
eget igen med en host-klasse. Hele omlægningen står bag `xl:`, og det er derfor de eksisterende E2E
måler det samme som før — de kører alle sammen på 480.

**Detaljepanelet renderes præcis ét sted, valgt af et signal frem for af CSS.** `hidden xl:block`
ville lade begge kopier stå i DOM'en, så `data-testid="task-detail"` fandtes **to** gange på en smal
skærm, og Playwright vælger tavst den første. `WideScreen.wide` driver derfor en `@if` på begge
sider: højre spalte om sig selv, og rækken om sin `[expanded]`. Signalet frem for en klasse er hele
begrundelsen for at klassen `WideScreen` findes — brydepunktet er `(min-width: 80rem)`, altså
Tailwinds `xl` skrevet i rem, så tallet står ét sted. **jsdom 28.1.0 har ingen `matchMedia`** (målt),
så servicen defaulter til smal, og en spec der vil have den brede sætter signalet selv.

**Og `xl:h-screen` på `main` er opgavelistens behov, som de fire andre skærme betaler for.** Rammen de
to spalter ruller indeni er hele appens, så enhver skærm der er højere end vinduet bliver klampet af
den. Opgavelisten slap, fordi dens spalter ruller selv; de fire andre er kun `block xl:max-w-2xl`, og
med `overflow: visible` på wrapperen om `router-outlet` blev deres indhold **ikke klippet** — det flød
ned gennem health-linjen, som står fast på y=552 inde i en 600 px høj `main`. Målt på ADO-importen i
1400×600: wrapperen 424 px, skærmen 1310 px. **En sidescrolling hjalp ikke**, fordi `main` flytter sig
som helhed, så overlappet rejser med. Rettelsen (2026-08-24) er `xl:overflow-y-auto` på wrapperen —
**ét** sted frem for på de fire hosts, hvor rullebjælken ville stå midt i vinduet ved x≈704 inde i
deres `max-w-2xl` i stedet for i vinduets kant. Lægger du en femte skærm på, får den det gratis.

**`min-h-0` findes ikke længere i appen, og reglen bag er derfor værd at kende.** Et flex- eller
gitterbarn hvis overflow *ikke* er visible har allerede automatisk minimumstørrelse nul, så
`overflow-y-auto` gør arbejdet selv. Målt ved mutation to gange: `xl:min-h-0` på begge spalter i
`task-list.html` fældede **ingenting**, og da wrapperen fik `xl:overflow-y-auto`, blev også dens
`xl:min-h-0` overflødig — wrapperen krymper til 424 px uden den. Den bærende klasse på wrapperen er nu
`xl:overflow-y-auto`, og den bærer **to** ting: fjernes den, fælder det både
`The_columns_scroll_on_their_own` (*"The list column did not scroll inside itself: 0"*) og
`A_long_import_list_scrolls_inside_the_window_rather_than_through_the_footer`. Set fejle sammen.

**Valget af opgave er en `computed`, ikke en effekt — og de tre regler er én regel.** `TaskList.selected`
er `selectable.find(id) ?? (wide ? selectable[0] : undefined)`. Auto-valg ved indlæsning, at valget
følger med når den valgte søges væk eller slettes, og at auto-valget kun gælder side by side, er
**samme linje**. En effekt skulle kaldes fra `load`, `remove`, `searchFor`, `setShowCompleted`,
`setShowSomeday` og statusskiftet — seks steder der kan drive fra hinanden. **Fuldførte er ikke
valgbare**, fordi deres række er et almindeligt `<li>` uden panel, så den tomme tilstand
(`tasks.selectPrompt`) findes selv med auto-valg.

**Bundlens advarselsloft er 600 kB, og tallet er valgt frem for arvet.** Det var 500, og en leverance
på under en halv kilobyte krydsede det: målt 499,97 kB før og 500,37 efter, altså 368 bytes over fra
30 bytes under. Et loft en 400-bytes funktion kan krydse måler ingenting og træner folk i at ignorere
advarsler. Fejlgrænsen står stadig på 1 MB, så et rigtigt spring fanges.

**En sortering inde i en sektion er en rang plus en stabil `sort`, ikke en sammenligning der også ser
på datoerne.** `Array.prototype.sort` er stabil per spec siden ES2019, så lige rangeringer beholder
serverens rækkefølge — og serveren er den der sorterer. Skriver man datoerne ind i sammenligningen,
vedligeholdes reglen to steder, og de to driver fra hinanden. Klienten bruger den kun til at løfte
i-gang-opgaver øverst.

**Serverens rækkefølge er deadline, derefter startdato — og deadline slår startdatoen.** Startdatoen
skiller kun to opgaver der falder samme dag, og **ingen startdato sorterer først** blandt dem, fordi
intet nogensinde har holdt opgaven tilbage: fraværet læses som "kunne startes for evigt siden", ikke
som en manglende værdi. Brugerens valg 2026-08-21, og gættet uden begrundelsen falder den anden vej,
fordi "start og deadline" nævnes i den rækkefølge. Vagten er
`TaskEndpointsTests.The_deadline_outranks_the_start_date`, set fælde netop ombytningen.

**Angular.** Signal-baserede stores ejer al HTTP. Komponenter injicerer aldrig en genereret
klient og kalder aldrig `.subscribe()`. **Ikke NgRx** — bevidst fravalg.

**Og ingen Angular forms — hverken de gamle eller signal forms. Afgjort af brugeren 2026-08-21, så
det er en beslutning og ikke en forglemmelse.** Felterne er native inputs med `[value]`/`[checked]`
plus `(input)`/`(change)`/`(blur)`, og signalerne bor i storene. `@angular/forms` er en afhængighed
men **ubrugt**: der findes ikke et `FormsModule`, `ReactiveFormsModule`, `ngModel` eller
`FormControl` nogen steder — målt med en søgning over hele `src`, ikke antaget.
**Og signal forms er ikke eksperimentelle, hvad man ellers kunne tro:** i 22.1.1 eksporterer pakken
`./signals` og `./signals/compat`, og `form`, `required`, `validate`, `validateAsync`, `validateHttp`
og `validateTree` står **uden** `@experimental` — den ene markering i `signals.d.ts` sidder på en
AI-værktøjsdel. Så argumentet mod dem er ikke modenhed.
Argumentet er formen: appen gemmer **pr. felt** på blur/change og har ingen formular-submit, og
**valideringen bor på serveren** med fejlkoder som `apiErrorMessage` oversætter. Signal forms er
bygget om en formularmodel med submit, så et skifte ville røre 40+ felter på fem skærme og omkring 90
testkald der sætter `.value` og udsender et event — for at flytte en validering serveren ejer.

**Prisen for fravalget er målt og skal blive ved at stå her: det manuelle `[checked]`-mønster har
givet én rigtig fejl og efterlader to latente.** `[checked]` genanvendes **kun når signalet skifter**,
så da registret afviste autostart, gik signalet `false` → `false`, bindingen havde intet at gøre, og
fluebenet stod til mens intet var registreret. Fundet af en E2E-rejse, ikke af nogen Vitest.
`Settings.setAutostart` skriver derfor elementet tilbage fra signalet efter rundturen.
**`jira-on-duty` og `ado-include-waiting` har samme mønster og er *ikke* rettet** — de fejler kun hvis
serveren afviser, og ingen af dem har en kodet grund til det i dag, hvor et låst register er den
sandsynlige sag. Rører du en af dem, så skriv elementet tilbage som autostart gør.

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

**Men `ng test` er ikke *helt* blind, og forskellen betyder noget når en vagt skal ses fejle.**
Afsnittet ovenfor gælder typerne *inde i* spec-filen; `angular-compiler`-pluginnet compilerer til
gengæld komponenterne og deres skabeloner, så en spec der rører et input, en metode eller en
skabelontype der ikke findes, fejler som en **bygning** og ikke som en assertion. Vil du se en vagt
fejle på sin *påstand*, skal maskineriet lægges ind først og adfærden stå urettet.

**En genvej udfører elementets aktiveringshandling, ikke bare fokus.** Det er
Windows-konventionen — og HTML's eget `accesskey` — så et tekstfelt får fokus, fordi det ikke har
andet at gøre, et afkrydsningsfelt skifter, en knap klikkes, et link følges. Gav genvejen kun
fokus, skulle brugeren trykke Alt+O og **derefter** Enter. Og et programmatisk `click()` flytter
**ikke** fokus, så en aktiverende genvej skal kalde `focus()` også: Windows flytter fokusringen
lige så meget som den handler. Direktivet har derfor
`appShortcutAction="focus" | "activate"`, hvor `'activate'` kalder begge.

**Bogstaverne er `Alt+O/I/J/A/S/N/K/V/M`**, valgt udenom `Alt+D`, `Alt+E`, `Alt+F`, `Alt+Home` og
piletasterne, som Chrome stjæler under udvikling. De er frie i Photino-vinduet, men en genvej der
virker i appen og ikke i browseren bliver fejlsøgt i den forkerte ende. `J` kom til med
Jira-importen i skive 11 og kolliderer ikke: Chrome på Windows binder intet `Alt+J`, og den nære
nabo er DevTools-konsollen, som er `Ctrl+Shift+J` — et andet modifikatorsæt. (På macOS er den
`Cmd+Option+J`, men appen er Windows-only.) `A` kom til med ADO-importen i skive 12 og kolliderer
heller ikke: Chrome på Windows binder intet `Alt+A`, og de nære naboer er `Ctrl+A` og `Alt+D`,
**`K` kom til med søgefeltet** og kolliderer heller ikke: `Ctrl+K` er Chromes adresselinje, altså et
andet modifikatorsæt, og `Alt+K` er ubundet.
altså andre taste- og modifikatorsæt. **Registret er last-writer-wins**, så bogstaverne skal
blive ved at være globalt unikke; se designdokumentets afsnit 10.

**Og fra 2026-08-24 er der to lag, ikke ét.** Nøglen i registret er `lag+tast`, bygget af
`shortcutKey` i `src/Todo.Web/src/app/shortcuts/shortcut-key.ts`, og `shortcutLabel` bygger
`aria-keyshortcuts` af de **samme to felter** — så mærkaten kan ikke drive fra den kombination der
virker. `alt`-laget er de ni bogstaver ovenfor **plus `Alt+1`–`9`**, som vælger den n'te *valgbare*
række på listen; det lag er stadig last-writer-wins, så både bogstaver og cifre skal blive ved at
være globalt unikke. `alt-shift`-laget er detaljepanelets otte felter og hører **kun** til
opgavelisten — `D` deadline, `S` startdato, `O` opgavestiller, `N` noten, `T` status (`T` og ikke
`S`, fordi startdatoen har det stærkere krav på et felt man skriver i), `V` venter-på, `U` ny
underopgave, `L` slet. Cifrene kan **kun** prøves i Photino-vinduet: Chrome binder `Alt+1`–`8` til
faneskift og `Alt+9` til sidste fane.

**`Alt+Shift+L` er den eneste genvej der kun giver fokus, og mærkaterne vises på Alt alene.**
Sletningen har hverken bekræftelse eller fortryd i appen, så det **andet** tryk *er* bekræftelsen —
begrundelsen står ved kaldet i skabelonen, ellers rettes undtagelsen som en inkonsekvens. Og
mærkaterne — også feltlagets, som læses `⇧D` — vises når **Alt** alene er nede: skulle man holde
`Alt+Shift` bare for at *se* dem, udsatte hvert slip brugeren for Windows' layoutskift, en fejl der
dukker op uger senere som "appen skifter mit tastatur". Shift kommer først på i selve anslaget.

**Direktivet registrerer fra en `effect` med `onCleanup`, ikke fra `ngOnInit`/`ngOnDestroy`.** `@for`
sporer på `task.id`, så en søgning, et statusskifte eller en ny opgave omfordeler 1–9 **uden** at
destruere nogen række: samme komponentinstans får et nyt nummer, og med livscyklus-hooks ville den
svare på sit gamle ciffer for evigt — tavst, fordi badgen læser inputtet og pænt viser det nye tal.
En **tom** `appShortcut` registrerer ingenting og udsender slet ingen `aria-keyshortcuts`; sådan
virker række ti og frem. Og `unregister` tager callbacket med og sletter kun, hvis det gemte stadig
er det samme: to rækker der bytter numre kører hver sin oprydning, og rækkefølgen mellem to
effekters oprydninger er ikke garanteret, så uden vagten kan taberens oprydning slette den nøgle
vinderen lige skrev. **Vagten skal sammenligne på `key.toLowerCase()`**, fordi `register` gemmer den
små — ellers holder `unregister('N', fn)` sit callback op mod `undefined`, tager den tidlige udgang
og sletter aldrig. Planens egen kodestump gjorde netop det.

**Og fra skive 12 findes der en vagt på netop det**, hvad der ikke gjorde før:
`KeyboardJourneyTests.Every_shortcut_letter_on_screen_is_its_own` læser `aria-keyshortcuts` af hvert
element på opgavelisten og kræver bogstaverne distinkte. Den er nødvendig, fordi **intet andet kan se
en kollision**: `ShortcutStore.register` er et rent `Map.set`, mærkatens bogstav er skrevet i
skabelonen og ikke afledt af `appShortcut`, og målt i skive 12 fældede `nav-ado="j"` **nul af 239**
Vitest. Vagten er set fejle med netop den mutation og navngiver begge elementer:
`Two elements claim the same Alt letter … nav-ado=Alt+J, …, nav-jira=Alt+J, …`. **Bemærk hvad
mutationen ellers afslørede:** `Alt_J_follows_the_jira_link` faldt **også** — last-writer-wins gjorde
`Alt+J` til ADO-skærmens — så en kollision på et bogstav der har en rejse, fanges af rejsen. En
kollision på `m` eller `v` ville ingen have set. Vagtens grænse er, at den kun sammenligner de
genveje der er **renderet nu**; alle otte bor på opgavelisten i dag, og tællingen mod otte er det der
tvinger spørgsmålet frem, hvis en fremtidig genvej kun findes på en anden skærm.

**`Ctrl+Alt` er AltGr på et dansk tastatur.** En global `Alt+bogstav`-lytter skal tjekke
`!event.ctrlKey && !event.metaKey`, ellers kan brugeren ikke skrive `@`, `£` eller `$` — en fejl
der dukker op uger senere som "appen æder mine tegn". Og kald kun `preventDefault()` når tasten
faktisk blev håndteret, så uhåndterede kombinationer stadig når browseren og styresystemet.

**En kontrol inde i en omsluttende `<label>` arver labelens tekst ind i sit tilgængelige navn.** Målt på
Jira-forhåndsvisningen, hvor hver række *er* en `<label>` om sit afkrydsningsfelt: en knap placeret
derinde blev til et afkrydsningsfelt med navnet "Åbn sagen", og `GetByRole(Checkbox, Name = "Åbn sagen")`
fandt den. Knappen hører derfor **uden for** labelen. Bemærk at den nære fælde er en anden end den
nedenfor: her er det ikke en test der matcher præcist, men browserens navneberegning. Og bemærk hvad der
**ikke** virker som vagt: "fluebenet overlevede klikket" kan ikke fejle, fordi en `<button>` er
interaktivt indhold og browseren derfor springer labelens aktiveringsadfærd over. Påstå på **navnet**.

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

**Færdig slår begge, og rollen er en enum af netop den grund.** `JiraStatusRoles.For` svarer
`Done → Duty → Waiting → Actionable`, og `AdoStateRoles.For` svarer `Done → Waiting → Actionable` —
ADO's var en `bool` indtil færdighed kom til, og klassens egen dokumentation havde forudsagt skiftet
ordret. En status kan stå i to lister, og noget skal afgøre hvad der vinder: en løst sag venter ikke
på nogen, så byttes grenene om, står den som ventende, forslaget om at lukke skjules bag vagt-grenen,
og rækken er mærket som arbejde nogen skylder dig. **Begge udfald er lovlige roller**, så en ombytning
compilerer og læser rigtigt — kun `Done_outranks_waiting_when_a_state_is_in_both_lists` og dens tre
søskende kan se den. Set fejle.

**Færdig-listerne er tomme som gyldig tilstand, i modsætning til `adoWorkItemTypes`.** Tom betyder
ingen forslag; tom sagstypeliste betyder derimod "gendan standarden", og importen afviser den. Den
nære fælde er at kopiere den forkerte af de to præcedenser, når `ado.doneStates` eller
`jira.doneStatuses` læses.

**`PUT /api/tasks/{id}` overskriver `CompletedAt` med `clock.UtcNow`** på *enhver* overgang til
Færdig (`TaskEndpoints.cs`). Derfor kan en lukning fra importen ikke gå den vej: kildens tidsstempel
ville blive kastet væk tavst. Lukningen rider med på importens endpoint, hvor `waitingSince` allerede
har præcedens for at komme fra klienten som et faktum. Vagten er
`Closing_takes_the_completion_time_from_the_source`, og **fixturets fem dages afstand til testuret er
dens tænder** — et tidsstempel tæt på nu ville bestå med den forkerte implementering.

**Dedup'en bærer den lokale status, ikke bare nøglen.** `ImportedKeysAsync` svarer
`Dictionary<string, TodoStatus>` i begge endpoints. Uden statussen kan forslaget om at lukke ikke
holde op med at komme igen, når du har taget imod — samme række ville foreslå det samme for evigt.

**Vagt slår ventende.** `JiraStatusRoles.For` spørger om vagten **først**: står et statusnavn i
*begge* lister, og er kontakten slået til, er rollen `Duty` — ikke `Waiting`. Samme status betyder
*venter på puljen* når du ikke har vagten, og *venter på dig* når du har den, så det er kontakten
der afgør det, ikke statussen. Rækkefølgen er load-bearing på linje med `DeadlineBuckets.For`'s
grene, og **gættet uden begrundelsen falder den anden vej**: ventende er den ældste regel, så man
tror den vinder. Byttes de to om, importeres puljens sager som `WaitingFor` og forsvinder ud af
deadline-sektionerne — netop det arbejde du har vagten for, skjult. Reglen bor **ét** sted, fordi
den træffes to gange: i forhåndsvisningen og i importen.

**Der er fire Jira-indstillinger, og de to par gør hver sin ting.** `jiraWaitingStatuses` +
`jiraIncludeWaiting` er en **mapning**: navnene siger hvilke statusser der *betyder* ventende, og
kontakten siger om de sager alligevel må komme med. `jiraDutyStatuses` + `jiraOnDuty` **udvider
hvad der hentes**: navnene lægger et `OR status IN (…)`-led på JQL'en, så puljens sager — som ikke
er tildelt dig — overhovedet kommer med i svaret, og kontakten siger om det led skal med. Læs ikke
det andet par som endnu et filter over det første; det ene *oversætter* et svar, det andet *ændrer
spørgsmålet*.

**Parenteserne om disjunktionen i JQL'en er load-bearing.** `AND` binder tættere end `OR`, så
`project = X AND resolution = Unresolved AND assignee = Y OR status IN (…)` læses som
`(project = X AND resolution = Unresolved AND assignee = Y) OR status IN (…)` — og højresiden står
frit: enhver sag i en vagt-status fra **alle fire projekter** PAT'en ser, kundeprojektet `KK`
iberegnet, og løste sager med. Kun assignee'en må ind i disjunktionen; projektet og resolutionen
skal blive konjunktioner:
`project = X AND resolution = Unresolved AND (assignee = Y OR status IN (…))`. Bemærk at skive 11's
`An_empty_project_key_refuses…` er **blind** for det: projektnøglen *er* sat, den er blot havnet
inde i en parentes der forsvandt. Samme udfald som en tom projektnøgle, nået ad en vej ingen af de
gamle vagter kigger ned.

**`SettingList.Read` er delt, men `Write` er det *ikke* — og de to ser ens ud.** Læsningen af en
JSON-liste i `Setting` er samme regel overalt (korrupt værdi læses som tom, så en ulæselig
indstilling ikke stopper appen fra at åbne), og den bor derfor ét sted. **Skrivningen er to
forskellige regler:** `SettingList.Write` deduper **versalufølsomt** som aliaslisten, mens Jiras
`StatusList` deduper **ordinalt**, fordi `JiraStatusRoles.For` sammenligner statusnavne ordinalt —
en versalufølsom dedup på skrivevejen ville flette to statusser Jira holder adskilt. "Udtræk før du
tilføjer" peger derfor det forkerte sted hen her: fælles læsning, delte skrivninger.

**En `errors.*`-nøgle må ikke indeholde `{{value}}`.** `api-error-message.ts` kalder
`transloco.translate(key)` **uden params**, så en pladsholder når brugeren **urenderet** — hun ser
bogstaverne `{{value}}`. Naboen `errors.retro.duplicateAlias` er netop derfor formuleret uden
interpolation, og `errors.settings.duplicateDelegate` er det af samme grund. Ingen test kan se det:
paritetstesten sammenligner nøglemængder, og `ErrorCodeTranslationTests` kræver at nøglen *findes*.
Skriv sætningen så den er hel uden værdien.

**Et hemmeligt felt skal have sit eget endpoint.** `PUT /api/settings` er en fuld erstatning der
læser et fraværende felt som "ryd", og tokenet kan aldrig sendes *tilbage* til klienten — så en
klient der gemmer noget andet på siden, ville rydde tokenet hver gang. Tokenet har derfor
`PUT`/`DELETE /api/jira/token` for sig, og `SettingsResponse` bærer kun `hasJiraToken`. Det er
samme fælde som `TaskStore.update`, med den forskel at her kan `current`-tricket ikke bruges.

**Uddelegering er en genvej, ikke en tilstand.** En opgave med status `WaitingFor` og et navn i
`WaitingOn` **er** en uddelegeret opgave, så der er **ingen `Delegated`-status**, intet felt på
`TaskItem` og ingen migrering — samme valg som udskudtheden ovenfor, og det er et *valg*, ikke en
mangel. Leder du efter en status og ikke finder den: den findes ikke, og den skal ikke laves.
Genvejen er tre ting, alle uden ny datamodel: indstillingen `delegates` (JSON i `Setting`), en delt
`<datalist id="delegate-names">` i `task-list.html`, og at statusvælgeren giver hvem-feltet fokus.
**Listen er forslag, ikke et krav** — `waitingOn` bliver ved at være et tekstfelt, fordi "venter på
ingen" og "venter på en der ikke står på listen" begge er gyldige tilstande i dag; gør nogen navnet
obligatorisk, brækkes noget der virker. Og **kun bogføring**: ingen besked til den anden, og en
uddelegeret Jira-sag skifter **ikke** assignee i Jira.

**Intentionen om at spørge hvem bor i `TaskStore.askingWho`, ikke i rækken.** Rækken der spørger, er
ikke rækken der svarer: `PUT`'en flytter opgaven ud af sin deadline-sektion og ind i "Venter på" —
to forskellige `@for`-blokke — så `<li>`'en og komponentinstansen med den **destrueres**, og en frisk
renderer feltet. Et flag holdt i rækken var derfor altid falsk, når feltet endelig fandtes. Signalet
er `signal<number | null>` og læses af **ingen** skabelon, så en skrivning fra en effekt ikke kan
slås med change detection. Og feltet findes først **efter** en serverrundtur, fordi `@if` hænger på
den genindlæste opgaves status — en E2E-rejse skal derfor vente på det frem for at antage det, og
opløse sin locator igen bagefter.

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

**`UriBuilder` af-escaper *ikke* en sti på net10.0 — kun dobbelt escaping kan måles.** Skive 11 målte,
at `%20` blev et mellemrum, og den måling gjaldt en **query string** (JQL'en); den overføres ikke til
en sti, og en for bred læsning af den ville sende nogen efter en fejl der ikke findes. Målt tre gange i
skive 12 ved at mutere `AdoTaskSource.UriFor` og køre suiten: `UriBuilder` **bevarer** `%20` i både sti
og query og giver **36 grønne**; en interpolation af `Uri`-objektet af-escaper godt nok
(`Uri.ToString()` giver `/Fake Collection`), men `new Uri(...)` re-escaper mellemrummet på vejen ind
igen, så også den giver 36 grønne. En **sti** heler altså sig selv. Det der *kan* måles, er **dobbelt**
escaping — `Fake%2520Collection` — og det er præcis hvad
`The_space_in_the_collection_name_stays_escaped_on_the_wire` blev set fælde. Strengbygningen står
alligevel, fordi batch-kaldet **har** en query string, og der heler ingenting sig selv. Og
asymmetrien er det egentlige: samlingen er en **URL** brugeren har indsat og er escaped i forvejen,
projektet er et **navn** appen selv skal escape — `AdoSettings.BrowseUrl` gør netop det, og gør man det
omvendt, får man `%2520` i den ene ende og et brækket projektnavn i den anden.

**Indstillingssiden er en accordion, og foldningen er `@if`, ikke `hidden`.** De fem grupper er hver en
`section[appSettingsSection]` — attributvælger som `li[appTaskRow]`, så `<section>` bliver værten og
beholder sit `data-testid`, og skillelinjen falder mellem søskende. Højst **én** åben, og **nul åbne er en
gyldig tilstand**: siden ankommer sådan, og et klik på den åbne overskrift lukker den. Tilstanden bor i
`Settings.openSection` som `signal<SettingsSectionName | null>` og er **ikke** gemt — prisen er, at en tur
til importskærmen og tilbage folder siden op igen. Det er et *valg*, ikke en mangel: en gemt tilstand
koster et felt på kontrakten, en række i `Setting` og en rundtur for noget brugeren ikke har bedt om. Skal
det omgøres, flyttes signalet ind i `SettingsStore`.

**Konsekvensen for enhver locator på indstillingssiden er total.** Et felt i en lukket gruppe findes
**ikke** i DOM'en, så `SettingsScreen.WaitUntilShownAsync` — som ventede på `language-select` — blev en
påstand der ikke kunne holde: elementet er væk på netop den skærm den skulle bevise. Samme fælde ramte
`KeyboardJourneyTests.Alt_O_følger_opgavelinket`, hvor `ToHaveCountAsync(0)` på sprogvælgeren nu er sandt
**også** mens man står på indstillingssiden — en assertion der ikke kan fejle. Begge peger nu på
`language-section-toggle`, som findes uanset foldning. Leder du efter et felt her: åbn gruppen først.
`SettingsScreen.OpenAsync` er idempotent og kaldes af `ChooseLanguageAsync`, `SubmitAliasAsync`,
`SubmitDelegateAsync`, `StoreJiraTokenAsync`, `StoreAdoTokenAsync` og `RemoveWorkItemTypeAsync`, så en
kalder der kun bruger dem behøver intet at vide om foldningen.

**Chevronen skal være et tekstglyf, ikke en inline-SVG — og det er en testbeslutning.** `▾`/`▸` er
tekstknuder inde i overskriftsknappen, så `aria-hidden="true"` er **bærende**: målt ved at fjerne den,
hvorefter `GetByRole(Button, Name = "Sprog", Exact = true)` gav
`Locator expected to have count '1' But was: '0'`. En `<svg>` uden titel bidrager intet til det
tilgængelige navn, så samme mutation ville have fældet **ingenting** — vagten havde ikke kunnet skrives.
Prisen er, at `h3.textContent.trim()` nu indeholder chevronen; overskriftens ord læses af `<span>`'en.
Og glyfferne er tekst, så kontrastvagten måler dem: de bærer `text-gray-500 dark:text-gray-400`, det
dæmpede par, målt fældet ved `text-gray-400 dark:text-gray-600` (2,60:1 og 2,35:1).

**`PUT /api/settings` validerer præcis fire ting, og ingen af dem er et Jira-felt.** Ukendt sprog,
dubleret delegeret, tom ADO-sagstypeliste og ADO-dagantal uden for 0–365 — det er hele listen i
`SettingsEndpoints`. Konsekvensen: `saveJira`'s fejlvej kan i dag **kun** nås af en transportfejl, og
Jira-gruppens eneste *kodede* afvisning kommer fra tokenruten (`settings.emptyToken`). En test der staged
`jira.statusNameInvalid` på `PUT /api/settings` ville måle en form serveren aldrig sender — koden rejses af
`JiraTaskSource`, altså i forhåndsvisningen, ikke i gemningen.

**Og det var her foldningen afdækkede en rigtig fejl.** `settings.error` skrives af `save`, og
`saveBaseUrl`, `saveProjectKey`, `toggleWaiting`, `setIncludeWaiting`, `toggleDutyStatus`, `setOnDuty`,
`setToken` og `clearToken` gik alle den vej — men linjen `settings-error` renderes inde i
**sproggruppen**. Et afvist Jira-token stod altså over sprogvælgeren, og med foldning ser brugeren
**ingenting**, hvis sproggruppen er lukket. ADO-gruppen havde `adoError` af netop den grund; Jira havde
ikke. Rettelsen er `SettingsStore.jiraError` + `saveJira` + `jira-settings-error` i gruppens fod, symmetrisk
med ADO's to linjer: `jira-error` (Jira afviser et kald) ved siden af knappen, `jira-settings-error` (vores
server afviser en indstilling) i foden. `settings.error` har nu **én** kaller, `choose`, og det er
sproggruppens linje.

**Backenden læser et fraværende felt som "ryd".** `PUT /api/tasks/{id}` er en fuld erstatning, så
`TaskStore.update` skal bære **hvert** felt med i sit `current`-objekt. Mangler ét, sletter enhver
redigering af noget andet det lydløst — sådan tabte en gemt `DeferUntil` sig selv, når man rettede
deadlinen. Lægger du et felt på kontrakten, så læg det også i `current`.

## Testdisciplin

Det her er lært på den hårde måde i dette repo, hver gang ved at en test var grøn af den
forkerte grund.

- **En vagt-test skal ses fejle.** Bryd det den beskytter, bekræft at den fejler på det rigtige
  trin, ret tilbage. En test ingen har set fejle, beviser ingenting.
- **Pas på assertions der ikke *kan* fejle.** Fire gange her: en dedup-vagt der var uopnåelig fra
  UI'et; "ingen reload" bevist ved at kigge efter engelsk tekst, som en reload også ville give;
  "navnet er ryddet" tjekket på et felt der ikke renderes i den tilstand; og en sorteringstest hvor
  fixturet sådde den opgave der skulle løftes **først**, så en usorteret liste ville have bestået
  lige så godt. Den sidste er den nemmeste at lave og den sværeste at se: **rækkefølgen i fixturet er
  assertionens tænder.** Spørg altid: hvad ville få den her til at fejle?
- **En Playwright-påstand om at *ingenting* ændrede sig kan ikke laves race-fri ved at polle.** Den
  første poll der lykkes afslutter ventetiden, og lige efter et klik har Angular ikke re-renderet —
  så den uændrede værdi *står der* at læse. Målt: `Clicking_the_selected_row_again_keeps_it_showing`
  var **grøn** under den mutation der lægger den fravælgende toggle tilbage, og en probe viste
  hvorfor: feltet gik `2026-08-14` → `2026-08-16` et øjeblik **efter** at påstanden var bestået.
  Rettelsen er en rundtur imellem — her at slå "vis fuldførte" til og vente på rækken — så hver
  render klikket udløste er sket, før der læses. **Og fælden er dobbelt:** en *handling* lige efter
  klikket læser komponentens forældede tilstand, ikke DOM'ens. Et `FillAsync` på deadline-feltet
  umiddelbart efter klikket gemte på den **forrige** valgte opgave, fordi `save()` læste
  `this.task()` før inputtet var skiftet — så den omskrevne, "positive" påstand bestod af samme
  grund som den negative. En rundtur løser begge; en anden formulering af påstanden løser ingen af
  dem.
- **`getBoundingClientRect` klipper ikke.** Et element inde i en rullende beholder rapporterer sin
  **fulde** kasse, også den del beholderen skjuler — så en overlap-test bygget på rå rektangler er
  **grøn på både fejlen og rettelsen**, og den første udgave af
  `A_long_import_list_scrolls_inside_the_window_rather_than_through_the_footer` var netop det: rækkerne
  gik til y=1422 i begge tilfælde. Det man vil vide, er hvad et element **maler**, altså dets kasse
  skåret ned af hver forælder hvis overflow ikke er visible. Vagten regner den ud i browseren og
  navngiver de rækker der lander oven på health-linjen (`"Handling nummer 4"`).
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
- **Kun *fem* steder i *fire* filer er compiler-synlige, og det er opgjort — ikke gættet.** Et nyt
  `required` felt fælder `Spec_project_passes_the_type_checker` **kun** hvis fixturet føres ind i en
  **genereret type**, altså som argument til et `new X(...)`. Opgørelsen er
  `grep -rn "new \(Ado\|Jira\|Retro\)PreviewRow" --include=*.spec.ts`: `jira-store.spec.ts` (to gange),
  `ado-store.spec.ts` (to gange, kom til i skive 12) og `retro-store.spec.ts` (én gang). **Alt andet** —
  begge settings-fixtures, `jira-import.spec.ts`, `ado-import.spec.ts`, rutehandlerne i `ContrastTests`
  og E2E — bygger en **rå svarkrop** til `flush(...)` eller til `page.RouteAsync`, og er dermed usynlig
  for compileren. Jeg har forudsagt "typetjekkeren fælder den" tre gange og taget fejl tre gange;
  opgørelsen er den holdbare form. Konsekvensen: et manglende felt i en usynlig fixture er **grønt**,
  og bliver et `undefined` i et `string[]`- eller `number`-signal først når UI'et læser det.
- **Seks håndskrevne wire-fixtures har ingen compiler over sig — ikke tre.** Tallet stod som **tre**
  her indtil skive 12, og det var forkert af **to** grunde, hvoraf kun den ene er ADO's. Opgørelsen er
  `grep -rn "^interface " --include=*.spec.ts`, og den navngiver seks filer med en håndskreven
  wire-form: `settings-store.spec.ts` (`SettingsJson`), `settings.spec.ts` (`SettingsFixture`),
  `jira-store.spec.ts`, `jira-import.spec.ts`, `ado-store.spec.ts` og `ado-import.spec.ts` (alle fire
  `PreviewRowJson`). De to nye er skive 12's; **`jira-store.spec.ts` stod der i forvejen og var glemt.**
  Hver af dem beskriver serverens svar som et objekt skrevet i hånden — bevidst, fordi det er *wiren*
  de skal måle og ikke den genererede klasse — men ingen af dem afstemmes mod kontrakten. Lægger en
  skive et felt på et svar og glemmer det her, bliver de grønne på en form serveren ikke længere
  sender. Læg det nye felt i alle seks, og lad `ContrastTests`' og E2E-suitens rutehandlere være den
  syvende kopi du også skal huske. Bemærk at **`ado-store.spec.ts` står på *begge* lister** — den er
  den første fil der gør det: dens `row()` fodrer både et `flush(...)` (umålt) og et
  `new AdoPreviewRow(...)` (målt). `retro-store.spec.ts` og `retro-import.spec.ts` er **ikke** på
  listen: de bygger gennem `new RetroPreviewRow({...})` og har ingen egen wire-form.
- **Paritetstesten kan ikke se en nøgle der mangler i *begge* sprogfiler.** `translations.spec.ts`
  sammenligner `da.json` med `en.json` og med intet andet, så en symmetrisk mangel er de to filer
  *enige* om — ordret samme blindvinkel som en før/efter-sammenligning over en migrering, hvor
  før-billedet er lavet af migreringens egen `Down`. `jira.statusNameInvalid` stod uoversat i begge
  filer gennem tre commits, og brugeren ville have fået den rå streng at se. Vagten er
  `ErrorCodeTranslationTests` i `Todo.Api.Tests`: den enumererer hver `public const string` på
  `ErrorCodes` med refleksion og kræver en `errors.<kode>`-nøgle i **begge** filer. Den er
  **stærkere** end paritet og erstatter den ikke — paritet fanger den nøgle der kun findes i den ene
  fil. Og den påstår desuden, at refleksionen fandt noget: uden det består den på ingenting, hvis
  den en dag peger på den forkerte type. Samme grund som `--listFiles` på spec-typetjekkeren.
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
  testkørsel en rigtig browser. **Og afbrydelsen gælder kun den test der opsætter den** — den er
  ikke en egenskab ved suiten. `ContrastTests` havde den på opgavelisten, og en ny knap på
  Jira-skærmen ville derfor have bedt Windows om at åbne en rigtig browser fra en *anden* test i
  samme fil. Målt. Skriver du et klik på et udadgående link, så tjek at **netop den test** har
  ruten, frem for at antage at filen har.
- **En afbrudt request er ikke en 400, og de to tager forskellige veje gennem `apiErrorMessage`.**
  En `route.AbortAsync()` giver status 0, som den genererede klient kaster som `ApiException` —
  ikke `ApiError` — så beskeden falder tilbage på `errors.generic` ("Noget gik galt. Prøv igen.").
  Et rigtigt 400-svar giver derimod kodens egen tekst. Samme gren i UI'et viser altså **to
  forskellige sætninger** afhængigt af om den blev nået fra Vitest med et fejlsvar eller fra
  Playwright med en afbrydelse — så en E2E-påstand skrevet på kodens tekst fejler, uden at der er
  noget i vejen med koden.
- **Kontrast måles i browseren** med `getComputedStyle`, fordi kun browseren har afgjort hvilken
  baggrund et stykke tekst endte på. `ContrastTests` går appens **fem** skærme igennem i begge
  farvetemaer — `app.routes.ts` har præcis fem ruter: opgavelisten, retro-importen, Jira-importen,
  ADO-importen og indstillingerne — og måler derudover det **udvidede detaljepanel**, hvor noten,
  underopgaverne og statusvælgeren bor. Panelet er en tilstand på opgavelisten, ikke en sjette skærm;
  tæl det ikke som en. (Tallet var fire indtil 2026-08-20; ADO-importen gjorde det fem, og vagten
  fulgte med i skive 12's sidste opgave.) **En `@if`-gren er umålt, indtil fixturet har en opgave i den
  tilstand og rejsen åbner rækken** — vagten kan ikke se en farve, der aldrig blev renderet, så en ny
  betinget linje koster en fixture-opgave og en klik-og-vent i `ContrastTests`. Hintet om en startdato
  efter deadline kom ind i vagten netop derfor, og blev set fejle i begge temaer.
  **Indstillingssiden er fra foldningen fem skærmtilstande i stedet for én**, og den foldede side er en af
  dem: fem overskrifter og intet andet, en tilstand rejsen aldrig kan nå tilbage til, når først en gruppe
  er åbnet — så den snapshottes **først**. De fire andre er de fire grupper der ikke åbnes af noget andet;
  Jiras og ADO's åbnes af `StoreJiraTokenAsync`/`StoreAdoTokenAsync`. Målt at det virker frem for antaget:
  overskriftens `<span>` sat til `text-gray-400 dark:text-gray-600` gav **fem** fejl pr. tema i **tre**
  teorier (`Every_screen`, `The_Jira_screens`, `The_Ado_screens`), én pr. gruppenavn —
  `span text "Sprog" 2,60:1 needs 4,5`. Tælles skærme, er svaret stadig fem ruter; tælles **tilstande**,
  koster indstillingssiden nu fem snapshots.
  **Og hele suiten kører på 480 px undtagen to teorier**, så alt bag `xl:` var farve for farve umålt,
  indtil `The_side_by_side_layout_meets_WCAG_AA` og `The_side_by_side_prompt_meets_WCAG_AA` kom til.
  Det nye der måles, er ikke en palette men et sæt **forældre**: panelet har forladt sin række, og en
  uigennemsigtig flade stopper vagtens gang op gennem træet, så hver tekst i panelet måles mod en
  anden forælder end før. Den valgte rækkes accent er med vilje **ikke** blandt fundene — den er en
  kant, og vagten måler tekst; derfor er markeringen en kant og ikke en baggrund, for `text-gray-500`
  er 4,63:1 på `gray-50` og har ikke plads til et trin mere.
- **En gren bag et fremmedsystem er umålt, indtil kaldet opsnappes.** Skive 11 efterlod **elleve**
  `@if`-grene som ingen farve nogensinde blev renderet i, fordi hver af dem kræver et svar fra Jira;
  skive 12 efterlod **toogtyve** af samme slags bag ADO. `ContrastTests` svarer derfor selv på
  `**/api/jira/preview|test|statuses` og `**/api/ado/preview|test|states` med `page.RouteAsync` — samme
  greb som afbrydelsen af `/api/system/open-link`. Fire forskellige svar i rækkefølge er nødvendige,
  fordi grenene udelukker hinanden: en afvisning, en tom liste, en liste hvor hver række er
  blokeret, og en liste med én række der kan importeres. **Én rute-handler der læser en variabel**,
  ikke fire handlere, hvis præcedens ellers ville afgøre udfaldet.
- **Men "Playwright kan ikke bruge en falsk server" er forkert, og den stod her som en begrundelse.**
  Sætningen var *"Playwright kan ikke starte en `FakeJira` inde i hostens proces"* — og den blander to
  ting sammen. `FakeJira` og `FakeAdo` er **deres egne Kestrel-instanser på 127.0.0.1**, og
  `RunningHost` starter appen **i testprocessen**, så hostens egen `HttpClient` kan nå dem. Målt i
  skive 12: gemmer man `fake.BaseUrl` som `ado.baseUrl`, kører hele kæden ægte — WIQL'en, typefiltret,
  note-mapningen, dedup'en og den udledte deadline — uden at ét kald opsnappes.
  `AdoImportJourneyTests.Importing_derives_the_deadline_on_the_server_and_the_next_preview_says_so`
  gør netop det, og tre ting kan **kun** måles sådan: at typefiltret holder en Test Suite ude *hele
  vejen til opgavelisten*, at deadlinen er serverens regnestykke, og at "importeret tidligere" er
  appens egen dedup. Det gyldige stykke af den gamle sætning er, at en rutehandler stadig er den
  eneste vej til en **bestemt feltkombination** — et svar hvor én række har deadline og en anden ikke
  har, kan den rigtige server aldrig give, fordi dagantallet er én indstilling for alle rækker. Brug
  derfor det ægte til rejsen og opsnapningen til grenene.
  **Og at opsnappe kaldet er ikke nok — kroppen skal bære feltet.** Vagt-mærkaten hænger på
  `row.isDuty` alene, og handleren ovenfor sendte rækker **uden** feltet, så klienten læste
  `undefined`, grenen var falsk, og ingen farve blev nogensinde malet — mens rutehandleren så ud til
  at dække sagen. Samme klasse ét niveau dybere: et opsnappet kald er en tilstand, ikke alle
  tilstande. Og en indstilling er den anden halvdel: `jira-on-duty-notice` står bag
  `settings.jiraOnDuty()`, som kommer fra den **rigtige** backend og er `false`, så rejsen må klikke
  kontakten til — den kan ikke opsnappes væk.
- **En link-gren der hænger på et felt ingen builder kan sætte, er usynlig for enhver vagt.**
  `externalUrl` beregnes kun når `SourceId == JiraTaskSource.Id`, og `TaskItemBuilder` havde
  `FromRetro` men **ingen `FromJira`** — så `external-link` blev aldrig renderet i en eneste test,
  og både kontrastvagten og en link-påstand ville have målt et element der ikke fandtes. Målt:
  fjernes `FromJira` fra fixturet igen, siger Playwright `element(s) not found 'Åbn sagen'`.
  **Og linket kræver to ting, ikke én:** kilden *og* en gemt `jira.baseUrl`, for URL'en er beregnet
  af basisURL'en. Mangler den, er grenen stadig tom, uanset hvordan kilden er stavet.
  **ADO's samme gren har derimod *ingen* builder, og det er et valg.** `TaskItemBuilder` har `FromRetro`
  og `FromJira` men ikke `FromAdo`, og et `FromAdo` blev ikke lavet i skive 12: den ægte
  `FakeAdo`-rejse importerer gennem endpointet, så opgaven får sin kilde og sin nøgle af koden frem for
  af et fixture, og linket måles derfor **stærkere** end en builder kunne. Målt ved at bytte
  `AdoTaskSource.Id => ado.BrowseUrl(...)` i `TaskEndpoints.ToContract` til `=> null`:
  `element(s) not found 'Åbn sagen'`. Skal en fremtidig skive have en ADO-opgave i listen *uden* at
  importere, kræver den `FromAdo` **plus** både `ado.baseUrl` og `ado.project` — to ting mod Jiras én,
  fordi `AdoSettings.BrowseUrl` kræver begge.
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
- **En påstand lige efter en store-opdatering er allerede serverens svar.** `TaskStore.update`
  slutter med `load()`, så rækkens tekst efter et gem er læst tilbage fra API'et og ikke fra lokal
  tilstand. Målt i uddelegeringsleverancen: udelades `waitingOn` af gemningen, falder **den første**
  rækkepåstand — ikke den efter genindlæsningen, som man ellers ville tro var den eneste ærlige. En
  rejse behøver derfor ikke en genindlæsning for at bevise *at* der blev gemt; genindlæsningen måler
  noget andet og svagere, nemlig at det også overlevede en frisk `GET` uden tilstand i hukommelsen.
  Skriv aldrig "kun det sidste led kan fange det" uden at have set de foregående bestå.
- **Kontrastvagtens fejllinje navngiver det element der *bærer teksten*, ikke rækken omkring det.**
  `data-testid` sidder på `<li data-testid="delegate-row">`, men teksten står i dens `<span>`, så
  fejlen læses `span text "Flemming Overgaard" 2,60:1` — uden et testid at søge på. Læs hele listen
  frem for kun de linjer der har et navn i firkantet parentes; en mutation der fælder to grene kan
  ellers se ud som om den kun fældede én.
- **Et fallback gør en prioritet umålelig.** `AdoTaskSource` vælger notefeltet pr. sagstype med et
  fallback til det andet felt, og instansens målte Bug havde **kun** `ReproSteps` — så et fixture
  bygget af målingen kan ikke skelne *"en Bug foretrækker ReproSteps"* fra *"tag det felt der er
  udfyldt"*, fordi fallbacket redder begge. Kun en sag der bærer **begge** felter med forskellig tekst
  kan måle reglen, og den er derfor konstrueret frem for målt (`FakeAdo`s 17165). Har en regel et
  fallback, skal fixturet ramme det sted hvor de to grene giver forskellige svar — ellers måler du
  fallbacket.
- **Typer man feltet i sin falske server, kan den ulæselige fixture ikke længere udtrykkes.** Det er en
  stærkere vagt end en fejlende test: `FakeAdo` udsender tidsstempler som **`string`** netop for at et
  work item kan bære `"ikke en dato"`, og var feltet typet `DateTimeOffset`, ville compileren nægte
  fixturet — og hullet ville se ud som om det ikke fandtes. Samme familie som "en falsk server der
  udsender .NET's format måler sig selv", men den anden vej: typen i fixturen afgør hvilke fejl der
  kan *skrives ned*.
- **Et fallback der læser samme felt gennem samme parse er ikke et fallback.** `ITaskSource` siger:
  læs `ExternalTask.StatusChangedAt` og kald `FetchStatusChangedAtAsync` når den er `null`. For ADO er
  det **forkert** — metoden læser `Microsoft.VSTS.Common.StateChangeDate` gennem samme parse som `Map`,
  så den kan kun svare `null` en gang mere, mod én spildt rundtur pr. række med et ulæseligt
  tidsstempel. Målt ved at lægge `?? await FetchStatusChangedAtAsync(...)` ind:
  `An_unreadable_state_change_date_is_not_chased_with_a_second_call` faldt med
  `Assert.Empty() Failure: Collection was not empty / Collection: [17162]`, og `WaitingSince` var
  stadig `null`. Fallbacket hører til en kilde **uden** feltet, og Jira er den.
- **En udvidet vagt kan finde ingenting og stadig være værd at udvide.** At føre gennemgangen ud
  over noter, underopgaver, de analyserede importrækker og aliasrækkerne gav **nul** nye
  farvefejl — forudsigelsen om at `@tailwindcss/typography` ville fejle holdt ikke. Hvad den til
  gengæld afdækkede, var blindvinklen ovenfor. Læs ikke "ingen nye fejl" som "udvidelsen var
  spildt".
- **`BadgeCount = 9` i `KeyboardJourneyTests` gælder den *tomme* liste, og det er ikke en svaghed
  men en grænse.** `Holding_Alt_reveals_the_badges_and_releasing_it_hides_them_again` og
  `Every_shortcut_letter_on_screen_is_its_own` sår **ingen** opgaver, så ingen række har et nummer og
  der er intet panel — de ni statiske `Alt+bogstav` *er* hele siden, og tallet skal derfor ikke
  hæves, når de to nye lag kommer til. Konsekvensen er den vigtige: de to er **blinde for begge nye
  lag**, og derfor står `Every_shortcut_letter_on_a_seeded_list_with_the_panel_open_is_its_own` ved
  siden af dem og tæller **fra fixturet** frem for fra et nedskrevet tal. Målt: giver man
  `waiting-on-input` bogstavet `d` — en kollision inde i feltlaget — fælder det søsteren med begge
  elementnavne, mens den tomme liste **består i samme kørsel**.
- **Mærkaterne var aldrig målt af `ContrastTests`, heller ikke de ni gamle**, fordi ingen rejse holdt
  Alt nede. `Every_screen_meets_WCAG_AA` holder nu Alt over **ét** snapshot, taget med den *ventende*
  opgaves panel åbent — den eneste tilstand der renderer `⇧V` — påstår at mærkaterne *er* der (tallet
  bor i vagten), og slipper Alt igen inden resten af rejsen: med Alt nede ville klikkene og
  udfyldningerne længere nede i rejsen udløse
  genveje i stedet. Målt i begge retninger: én mærkat malet `text-gray-400 dark:text-gray-600` gør
  suiten **rød i begge temaer** (`span[shortcut-badge] text "⇧D" 2,49:1 needs 4,5` lyst, `1,94:1`
  mørkt) og **grøn** igen, hvis kun Alt-holdet fjernes. Bemærk tallene: de er mod panelets egen
  `bg-gray-50`/`dark:bg-gray-800`, ikke mod hvid — en forudsigelse på 2,60/2,35 var forkert af netop
  den grund, jf. paret-afsnittet i "Konventioner".
- **`aria-hidden="true"` på feltlagets mærkater kan ikke fældes af E2E.** Ingen rejse holder Alt med
  et panel åbent, så påstanden bor i `task-detail.spec.ts` og læser alle otte mærkater som
  `glyf:aria-hidden`-par. Set fejle på mutationen (`⇧L:null`).

## Testtal

**174** Todo.Core.Tests, **316** Todo.Api.Tests, **67** Todo.E2E, **293** Vitest — alle grønne,
målt med `Check.cmd` 2026-08-24.

Tallene står her af én grund: **et ændret tal efter en refaktorering betyder, at en test er tabt
eller duplikeret.** Det er hele reglen. Kør `Check.cmd` og sammenlign.

**Og E2E-tallet var forældet *inden* genvejslagene — fjerde gang regnskabet driver, og det skal stå
her frem for at blive overskrevet i stilhed.** Grenen stod på **61** grønne E2E før den leverance,
ikke 59: seks af de otte nye er dens, og to var lagt til uden at nogen rettede tallet. `HANDOFF.md`
stod samtidig på **58**, altså et tredje tal. Det er præcis den fejl afsnittet nedenfor advarer mod,
og den ene ting reglen kræver: et ændret tal kan kun betyde en tabt eller duplikeret test, hvis
tallet var sandt i forvejen. Flytter du et tal her, så skriv **hvorfor** det flyttede sig — og
retter du et tal du ikke selv har fået til at flytte sig, så sig at det var forældet.

**Og de her fire tal er de eneste der står i prosa nogen steder.** Afsnittet var 265 linjer med et
regnskab pr. leverance — hvor mange tests hver skive lagde til, og hvorfor fordelingen var som den
var. Det blev fjernet 2026-08-21, og begrundelsen er værd at kende, fordi den gælder næste gang
nogen får lyst til at føre regnskabet igen: **tallene drev fra virkeligheden tre gange** (`e6be619`,
`50f0e61` og senest her, hvor `HANDOFF.md` stod på 290/44/250 mens sandheden var 300/47/267), de
kostede omkring 7.000 tokens af hver session, og hver lektion der var værd at beholde er flyttet op
i "Konventioner" eller "Testdisciplin", hvor den hører. Et regnskab ingen kan stole på er værre end
intet regnskab.

Skal du vide hvad en bestemt leverance lagde til, så spørg Git — `git log --stat` og planfilerne i
`docs/plans/` har det, og de forældes ikke.
