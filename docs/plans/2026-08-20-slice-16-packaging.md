# Skive 16: pakning — én exe, og autostart som en indstilling

Skrevet 2026-08-20, efter en måling der blev kørt **før** planen. Designdokumentets afsnit 9 gav
skiven én linje — *"self-contained exe, autostart"* — så resten er nyt her.

Brugerens to valg, truffet 2026-08-20: **én enkelt exe** frem for en mappe eller en installer, og
**autostart som en indstilling** frem for en fast genvej i Startup-mappen.

## Måling 0 — kørt 2026-08-20, og den ændrede planen tre gange

Kommandoen var:

```
dotnet publish src\Todo.Host\Todo.Host.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <midlertidig mappe>
```

**1. Native filer embedder fint.** Der er **ingen** `runtimes\`-mappe og ingen løse DLL'er i
outputtet. `Photino.Native.dll`, `WebView2Loader.dll` og `e_sqlite3.dll` ligger inde i exe'en. Det
var den antagelse der kunne have væltet hele formen, og den holdt.

**2. Men det er 31 filer, ikke én.** Exe'en er **114.608.853 bytes** (109 MiB), og ved siden af den
ligger `wwwroot\` med **25** filer, `web.config`, `Todo.Host.staticwebassets.endpoints.json`
(55.078 bytes) og **tre** `.pdb`. Samlet 115.622.764 bytes, altså 110 MiB.
*(Task 1 målte tallene igen: 31 og 55.078 holdt, men `wwwroot` har **25** filer og ikke 24 —
8 filer plus `.gitkeep` plus 16 komprimerede — så der ligger **seks** løse filer i roden, exe'en
iberegnet, ikke syv.)*

**3. Fem af de seks løse filer er fjernelige — målt, ikke antaget.** `web.config`, de tre `.pdb` og
`staticwebassets.endpoints.json` blev slettet fra outputtet, hvorefter `/`, `/api/health` og
`/scalar/` alle svarede **200**. `staticwebassets`-filen er ikke i spil, fordi appen bruger
`UseStaticFiles()` og ikke `MapStaticAssets()`. Tilbage står **`wwwroot`**, som er det egentlige
arbejde i beslutning A. *(Task 1 slog dem fra med csproj-egenskaber og udgav igen: **11 filer**,
exe 114.600.629 bytes. Egenskaberne og hvad hver af dem fjernede står i `Todo.Host.csproj`.)*

**4. `icon.ico` bliver ikke udgivet.** `Todo.Host.csproj` har
`<Content Include="..\Todo.Web\public\favicon.ico" Link="icon.ico" CopyToOutputDirectory="PreserveNewest" />`
— og `CopyToOutputDirectory` gælder **build**-output. Publish kræver `CopyToPublishDirectory`. Den
udgivne app kalder derfor `SetIconFile(Path.Combine(AppContext.BaseDirectory, "icon.ico"))` på en sti
der ikke findes. Den crashede ikke headless, men **vinduesvejen er utestet** — headless springer
`PhotinoWindow` over, så målingen siger intet om hvad Photino gør med en manglende ikonfil. Bemærk at
`wwwroot\favicon.ico` **er** udgivet, så der findes en anden sti til samme fil.

*(Task 1 målte, at `CopyToPublishDirectory` **ikke er nok**, og det er en fælde et niveau dybere.
Med den alene er filen stadig ikke på disken: `-getItem:ResolvedFileToPublish` viser `icon.ico` i
listen med `PublishSingleFile=false` og **væk** med `true`, fordi `_ComputeFilesToBundle` tager hver
opløst fil der ikke bærer `ExcludeFromSingleFile` og giver den til bundleren — publish-output er
altså ikke det samme som en fil på disken. Beviset er i exe'ens størrelse: med metadataen 114.600.629
bytes, uden 114.608.855, en forskel på 8.226 mod ikonets 8.088. `wwwroot`-globben er markeret af
Web-SDK'en, hvilket er grunden til at de filer aldrig blev slugt. Item-gruppen bærer nu alle tre
stykker metadata, og `icon.ico` ligger i outputtet.)*

**5. Den udgivne exe afhænger af sin arbejdsmappe, og det er skivens vigtigste fund.** Kørt fra
repo-roden svarede `/` **404**, og vinduet ville have været blankt. Appens egen log sagde hvorfor:

```
The WebRootPath was not found: C:\privat-git\todo\wwwroot. Static files may be unavailable.
Content root path: C:\privat-git\todo
```

Indholdsroden er `Directory.GetCurrentDirectory()`, ikke `AppContext.BaseDirectory`. Kørt fra sin
**egen** mappe svarede `/` og `main-*.js` begge **200**. Fejlen er altså ikke i pakningen, den er i
hosten — og den rammer netop autostart, hvor arbejdsmappen sættes af den der starter processen.
**Den skal rettes uanset hvilken pakningsform man vælger.**

**6. Versionen er `1.0.0.0`.** `/api/health` svarede `{"status":"ok","version":"1.0.0.0"}`, altså
assembly-standarden. En udsendt exe bør bære et rigtigt nummer, og health-linjen viser det allerede
for brugeren.

**7. `wwwroot` indeholder tre udgaver af hver fil.** ~~Angular-bygningen lægger~~ **Publish** lægger
`.br` og `.gz` ved siden af hver `.js`, `.css`, `.json`, `.html` og `.ico` — plus `.gitkeep` og
`prerendered-routes.json`. Det er derfor 25 filer og ikke 9, og det er en beslutning i Task 2:
embedder man alle tre, tredobler man nyttelasten for en app der servérer på loopback.

*Task 1 målte kilden, og fundet var forkert om **hvem** der laver de komprimerede filer:
`src\Todo.Host\wwwroot` indeholder **ingen** `.br` eller `.gz` — 8 filer plus `i18n\`, og det er alt
Angular skriver. De 16 komprimerede kopier dannes af **static-web-assets-pipelinen under publish**.
Konsekvensen for Task 2 er, at der ikke er noget at beslutte: `StaticWebAssetsEnabled=false`, som
Task 1 satte for at fjerne endpoint-manifestet, fjerner dem i samme greb — 25 filer blev 9 — og
`UseStaticFiles()` servérede dem alligevel aldrig. Verificeret bagefter: `/`, `main-*.js`,
`styles-*.css` og `i18n/da.json` svarer alle 200. **Bemærk at egenskaben altså gør to ting**, og det
var kun den ene planen bad om.*

### En fælde målingen selv faldt i, værd at kende

`ls -la` printer **lokaltid**, mens `find -printf %T` printer **UTC**. To timers forskel gjorde at
den rigtige `%APPDATA%\TodoApp\`-mappe *lignede* noget proben havde rørt. Den var urørt — `todo.db`
var skrevet 17:45 lokalt, altså før den første probe kl. 18:01, af brugerens egen kørende app.
`CLAUDE.md` har allerede lektionen om at sammenligne tidsstempler på **epoch**; det her er samme
lektion gennem to værktøjer der ikke er enige om tidszonen.

## Beslutning A: én fil, og hvad den koster

Målingen siger at der er tre stykker arbejde mellem "31 filer" og "én fil", og de er ikke lige store.

**Fire filer slås fra med csproj-egenskaber** — `web.config`, `.pdb`-filerne og
`staticwebassets`-manifestet. Målt fjernelige.

**`wwwroot` skal ind i assemblyen.** Vejen er `Microsoft.Extensions.FileProviders.Embedded` med
`<GenerateEmbeddedFilesManifest>` og en `ManifestEmbeddedFileProvider` sat som webroot-provider.
Kontrakten er allerede embeddet på præcis den måde —
`<EmbeddedResource Include="..\..\contracts\openapi.yaml" LogicalName="Todo.Host.openapi.yaml" />` —
så mønstret findes i huset, men en enkelt fil med et fast `LogicalName` er ikke det samme som et helt
mappetræ med et manifest.

**`icon.ico` skal løses anderledes.** Tre muligheder, og valget skal træffes af en måling frem for en
smag: (a) `CopyToPublishDirectory` og lev med **én** løs fil ved siden af exe'en; (b) embed ikonet og
skriv det til en midlertidig fil ved opstart, så `SetIconFile` har en sti; (c) drop `SetIconFile`
helt og se om Photino-vinduet arver exe'ens egen `ApplicationIcon`-ressource — hvilket ville være
gratis, hvis det virker. ~~**Mål (c) først**~~; den er den eneste der giver én fil uden at skrive til
disk.

*Task 1 tog **(a)** — plus `ExcludeFromSingleFile`, se fund 4 — og lod (c) stå **åben med vilje**:
et vinduesikon kan ikke verificeres uden at et menneske ser på vinduet, og en agent der "målte" det
ville gætte. Spørgsmålet hører til Task 2, når brugeren kan se vinduet. Samtidig er `SetIconFile`
gjort betinget af at filen findes, så svaret ikke længere kan koste et blankt vindue — men bemærk hvad
den betingelse **ikke** er: ingen test rører `PhotinoWindow`, så den er en sikring og ikke en vagt.*

**Størrelsen bliver ikke mindre af det her.** 109 MiB er self-contained .NET plus ASP.NET Core.
Trimming er den oplagte tanke og **skal ikke tages i denne skive**: EF Core-migreringer,
System.Text.Json og NSwags genererede klient er alle refleksionstunge, og en trimmet build der
starter men fejler på den tolvte migrering er en dyrere fejl end 109 MiB. Vil man ned i størrelse, er
`--self-contained false` det ærlige alternativ — men det kræver .NET installeret på maskinen, og hele
pointen med skiven er en fil man kan flytte.

**WebView2-runtimen kan ikke embeddes.** `WebView2Loader.dll` er inde i exe'en, men den *loader* en
runtime der skal være installeret. På Windows 11 følger den med Edge, så den er der i praksis — men
det er en forudsætning, ikke en detalje, og den hører i README frem for i en overraskelse.

## Beslutning B: autostart som en indstilling

En kontakt på indstillingssiden skriver eller fjerner en værdi under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. **Per bruger, ikke maskinen** — ingen
administrator, ingen `HKLM`.

**Stien er `Environment.ProcessPath`**, ikke en hardkodet sti. Bemærk hvad det betyder i udvikling:
kører man gennem `dotnet run`, peger den ind i `bin\Debug`, så en kontakt slået til på
udviklingsmaskinen skriver en autostart der peger på et byggeoutput. Det er ikke farligt, men det er
værd at vide før nogen fejlsøger det.

**Registret skal bag en grænseflade.** `CLAUDE.md` er utvetydig: *"Playwright-tests må ikke have
bivirkninger uden for appen"* — og en test der skriver i brugerens `Run`-nøgle er præcis det. Samme
greb som `/api/system/open-link`, der opsnappes og afbrydes, fordi den ellers åbner en rigtig
browser. Så: en `IAutostart` med en Windows-implementation og en fake i testene, og E2E opsnapper
endpointet.

**Tilstanden læses fra registret, ikke fra databasen.** Det er den ene indstilling hvor sandheden bor
udenfor: fjerner brugeren nøglen med et andet værktøj, skal kontakten sige fra. En gemt bool i
`Setting` ville kunne stå og lyve. Det gør den til den første indstilling **uden** en `Setting`-række,
og det er et bevidst brud på mønstret frem for en forglemmelse.

## Task 1: de fejl der kun viser sig i en publish

Ingen ny funktion, kun de tre ting målingen fandt.

**Indholdsroden.** `TodoHost.Build` skal sætte den til `AppContext.BaseDirectory`. Bemærk at det ikke
må brække de fire testprojekter, som starter hosten i testprocessen — `RunningHost` er indgangen, og
dens `AppContext.BaseDirectory` er testbinærens mappe, hvor der **ikke** ligger et `wwwroot`. Det er
præcis derfor E2E-suiten i dag finder `src\Todo.Host\wwwroot`: arbejdsmappen. Så ændringen skal kunne
bære **begge** tilfælde, og vagten skal se dem hver for sig.

*Task 1: mekanismen fandtes i forvejen — `RunningHost` sender `--contentRoot RepoPaths.HostContentRoot`
med i sine args, så testene navngiver roden selv. Men **kommandolinjen slår ikke automatisk en
standard her**, og det er den ene ting der skal måles frem for gættes:
`WebApplicationOptions.ContentRootPath` lægges **oven på** konfigurationen, så en ubetinget tildeling
vinder over `--contentRoot`. Målt ved netop den mutation:
`An_explicit_content_root_still_wins` faldt med `Expected: "C:\privat-git\todo\src\Todo.Host"`,
`Actual: "…\tests\Todo.Api.Tests\bin\Debug\…"`, og hele E2E-suiten ville være fulgt efter. Derfor
spørger `TodoHost.DefaultContentRoot` **først**, om nogen har navngivet en rod — kommandolinje og de
to env-præfikser, altså de samme tre kilder hosten selv læser — og svarer `null`, hvis nogen har.*

*Og fundet planen ikke havde: **`dotnet run` er også en af de kaldere.** `scripts\run-app.ps1` starter
appen med `dotnet run -c Release`, hvis arbejdsmappe er **projektmappen** — det er derfor `Todo.cmd`
virker i dag. Med den nye standard bliver roden `bin\Release\net10.0`, som **ikke** har noget
`wwwroot` (målt: ingen af de tre byggeoutputmapper har et), og vinduet ville have været blankt for
brugeren. Målt på et Debug-run fra repo-roden: uden argumentet svarede `/` **404** med
`The WebRootPath was not found: …\bin\Debug\net10.0\wwwroot`; med `--contentRoot src\Todo.Host`
svarede `/`, `main-*.js` og `i18n/da.json` alle **200**. `run-app.ps1` navngiver derfor roden nu, på
samme måde som testene. En udgivet exe har `wwwroot` ved siden af sig og behøver ingenting.*

**Vagten er ikke en unittest.** En påstand om at appen finder sit `wwwroot` kan kun måles på en
udgivet exe, og en publish tager omkring 40 sekunder. Den hører derfor i `scripts\publish.ps1`
(Task 3), ikke i `dotnet test`.

*Det gælder **den** påstand, men ikke de to andre, og Task 1 skrev dem som `HostContentRootTests`:
at `Build` uden argumenter får `AppContext.BaseDirectory`, og at `--contentRoot` stadig vinder. Den
første har en fælde værd at kende: under `dotnet test` **er** processens arbejdsmappe testbinærens
mappe, så de to kandidatsvar peger på samme mappe, og en normaliserende sammenligning kan ikke fejle.
Det der skiller dem, er stavemåden — `AppContext.BaseDirectory` ender på en separator,
`Directory.GetCurrentDirectory()` ikke — så påstanden sammenligner strengen som den står. Målt begge
veje med rettelsen fjernet: den eksakte fejlede på `…\net10.0\` mod `…\net10.0`, den normaliserende
bestod.*

**Versionen.** Sæt `<Version>` i csproj'en, og lad `/api/health` bære den. Der findes allerede en test
på health-svaret — find den og udvid den frem for at lægge en ny ved siden af.

*Task 1 valgte **1.16.0**: major 1, fordi appen har været i brug siden skive 1, og minor er skiven der
udsendte bygningen. `/api/health` svarer nu `{"status":"ok","version":"1.16.0.0"}` — fire led, fordi
den læser `Assembly.GetName().Version`. `HealthEndpointTests` påstår **ikke** tallet, som ellers skal
rettes hver skive, men at det ikke er `1.0.0.0` (assembly-standarden, altså "ingen valgte et") og at
det er assemblyens eget. Set fejle ved at kommentere `<Version>` ud:
`Assert.NotEqual() Failure: Strings are equal / Expected: Not "1.0.0.0"`. Kontrakten er **ikke**
rørt: `version` er allerede et krævet felt på `HealthResponse`, og `example: 1.0.0.0` er et eksempel
på formen — en ændring der ville koste en hel `generate-api.ps1`-kørsel for en dokumentationsstreng.*

**De fire suppressioner.** `<IsTransformWebConfigDisabled>`, `<DebugType>none</DebugType>` eller
tilsvarende, og den egenskab der slår static-web-assets-manifestet fra. **Verificér hver af dem ved
at udgive igen og tælle filerne** — en egenskab der ikke gør noget, ser ud som en der gør.

*Task 1: **fire egenskaber, og de tre `.pdb` krævede to af dem.** `<DebugType>none</DebugType>` gælder
kun projektets eget symbolfil; `Todo.Core.pdb` og `Todo.Contracts.pdb` følger med som *related files*
til en projektreference og krævede `<AllowedReferenceRelatedFileExtensions>none</…>`. Begge står under
`Condition="'$(Configuration)' == 'Release'"`, så en Debug-bygning stadig kan fejlsøges — og
`dotnet test` bygger Debug. **Prisen er større end den ser ud**, fordi `run-app.ps1` kører
`dotnet run -c Release`: brugerens daglige app har derfor heller ingen symboler, og et stacktrace
mister sine linjenumre. Det er et valg, ikke oprydning. `<StaticWebAssetsEnabled>false</…>` fjernede
manifestet **og** de 16 komprimerede kopier, se fund 7. 31 filer blev 11 — de 5 løse plus 16 `.br`/`.gz`
væk, `icon.ico` til.*

## Task 2: `wwwroot` ind i exe'en

`GenerateEmbeddedFilesManifest` + `ManifestEmbeddedFileProvider` som webroot.

**Afgør hvad der skal med.** ~~De tre udgaver af hver fil er målt~~ — *og beslutningen er væk efter
Task 1: kilden har aldrig haft `.br`/`.gz`, publish lavede dem, og `StaticWebAssetsEnabled=false`
fjernede dem. Der er **9** filer at forholde sig til, ikke 25.* `.gitkeep` og
`prerendered-routes.json` skal ikke med.

**Fælden at måle:** `MapFallbackToFile("index.html")` slår op i webrootens provider, så den skal virke
gennem manifestet og ikke kun gennem disken. Og `UseStaticFiles()` skal have samme provider. Et opslag
der falder tilbage på disken ville bestå i udvikling og fejle i den udgivne exe — samme klasse som
fund 5.

**Og verificér at Angular-bygningen stadig er inputtet.** `scripts\build-web.ps1` skriver til
`src\Todo.Host\wwwroot`, og bliver de filer nu embeddet, skal en glemt bygning kunne mærkes. I dag er
den fælde kendt og skrevet i `CLAUDE.md`: E2E-suiten bygger ikke Angular. Med embedding bliver det
værre, fordi filerne så er inde i en assembly der skal genbygges.

## Task 3: `scripts\publish.ps1`, som beviser sit eget output

Scriptet udgiver og **prøver derefter exe'en**: starter den headless på en fri port med
`--Data:Path <midlertidig fil>`, kalder `/`, `/api/health` og `/scalar/`, kræver 200 på alle tre, og
stopper processen igen. Det er vagten på hele skiven, og den bor her fordi en publish er for dyr til
`dotnet test`.

**Tre regler fra `CLAUDE.md` gælder scriptet selv.** Kør fra repo-roden. Giv **altid** `--Data:Path`,
aldrig `%APPDATA%\TodoApp\todo.db`. Og find processen på **porten**, ikke på navnet — brugeren har
ofte appen åben, og et `Stop-Process -Name Todo.Host` ville lukke begge. Målingen her gjorde netop
det rigtige og fandt tre gange sit eget PID via `Get-NetTCPConnection`.

**Og scriptet skal skrives ASCII-only i brugervendte strenge.** PS 5.1 læser en BOM-løs `.ps1` som
ANSI-kodesiden, hvorfor `Todo.cmd` skriver `foraeldet` og `check.ps1` gør det samme. Der er ingen vagt
på det.

**Kør prøven fra en anden mappe end exe'ens.** Ellers måler den ikke fund 5, som er hele grunden til
at Task 1 findes.

*Task 1's prøve, kommando for kommando, så scriptet kan skrives frem for genopfundet. Udgivelsen (fra
repo-roden, én linje):*

```
dotnet publish src\Todo.Host\Todo.Host.csproj -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o <midlertidig mappe>
```

*Prøven, kørt med **repo-roden** som arbejdsmappe — altså ikke exe'ens egen mappe:*

```
<mappe>\Todo.Host.exe --headless --urls http://127.0.0.1:<port> --Data:Path <midlertidig .db>
curl.exe -s -o NUL -w "%{http_code}" http://127.0.0.1:<port>/            # ventes 200
```

*`/api/health` svarer, før `/` gør, så pollingen skal hænge på health og ikke på roden. Målt syv ruter
og alle **200**: `/`, `/main-*.js`, `/styles-*.css`, `/i18n/da.json`, `/api/health`, `/scalar/` og
`/openapi/contract.yaml`. Kroppen var `{"status":"ok","version":"1.16.0.0"}`, og loggen sagde
`Content root path: <mappe>` **uden** nogen `WebRootPath was not found`. Før rettelsen svarede samme
prøve fra samme mappe `/` = **404** med `/api/health` og `/scalar/` = 200 — så en prøve der kun kalder
health og scalar kan **ikke** fange fund 5; roden skal med.*

*Og oprydningen, med det ene PID fundet på porten:*

```
Get-NetTCPConnection -LocalPort <port> -State Listen   →   Stop-Process -Id <det ene PID>
```

*Målt tre gange her, og hver gang stod brugerens egen app ved siden af — PID 57664 fra
`src\Todo.Host\bin\Release\net10.0`. Bemærk at netop den proces **låser** `bin\Release\net10.0`, så et
`dotnet run -c Release` fejler med `MSB3021 … locked by: "Todo.Host (57664)"` mens appen er åben. Et
publish-script rører ikke den mappe (RID-mappen er `bin\Release\net10.0\win-x64`), men et script der
gerne ville bygge Release "rent" først, ville støde på det.*

## Task 4: autostart-indstillingen

Kontrakt, lagring, endpoint, frontend — i den rækkefølge, som de øvrige skiver.

`autostart` som en bool på `SettingsResponse`, læst af registret. **Ikke** på `SettingsRequest`s fulde
erstatning: `PUT /api/settings` læser et fraværende felt som *ryd*, og en autostart der slås fra af en
sproggemning er samme fælde som tokenet. Den får sit eget endpoint, som tokenet gjorde.

`IAutostart` i `Todo.Core` med `Read()`, `Enable(path)`, `Disable()`; Windows-implementationen i
`Todo.Host`. Fejlkoder skal have en `errors.*`-nøgle i **begge** sprogfiler, ellers er
`ErrorCodeTranslationTests` rød — den enumererer hver `public const string` på `ErrorCodes` med
refleksion.

Kontakten hører i **sproggruppen**, som nu heder noget andet end sproget alene — accordion'en har fem
grupper, og "dine egne først". Afgør om gruppen skal skifte navn til noget der dækker både sprog og
autostart, og husk at overskrifterne er `settings.groups.*`-nøgler i begge filer.

## Task 5: vagter og dokumentation

En E2E-rejse der slår kontakten til og fra, med `IAutostart` faket, så testen ikke skriver i
registret. `ContrastTests` skal måle den nye kontakt i begge temaer — og `CLAUDE.md`s lektion gælder:
en `@if`-gren er umålt indtil rejsen renderer den, og en indstilling der kommer fra den **rigtige**
backend kan ikke opsnappes væk.

`README` skal sige at WebView2-runtime er en forudsætning, og hvordan man udgiver. `CLAUDE.md` skal
have fund 5 (indholdsroden), fund 4 (`CopyToPublishDirectory`) og tidszonefælden fra målingen.
Designdokumentets afsnit 9 skal markere skiven færdig.

*Task 1 fandt tre lektioner mere, som hører samme sted, og de står indtil videre kun her:
(1) `CopyToPublishDirectory` er ikke nok under `PublishSingleFile` — uden `ExcludeFromSingleFile`
havner filen **inde** i exe'en, og `File.Exists` siger nej på en fil der er "udgivet".
(2) `dotnet run`s arbejdsmappe er **projektmappen**, ikke byggeoutputtet, så en standard baseret på
`AppContext.BaseDirectory` brækker udviklingsvejen og ikke den udgivne — modsat af hvad man venter;
`run-app.ps1` navngiver derfor roden.
(3) Brugerens app kører fra `bin\Release\net10.0` og **låser** mappen, så `dotnet run -c Release`
fejler med `MSB3021` mens vinduet er åbent. Ingen af de tre kan gættes af den næste der møder dem.*

## Hvad skiven ikke gør

**Ingen opdateringsmekanisme.** Ingen signering, ingen SmartScreen-håndtering, ingen
delta-opdatering. En usigneret exe på egen maskine er den valgte pris; vælger man senere en
installer, er det en skive for sig.

**Ingen trimming.** Se beslutning A.

**Ingen tray og ingen notifikationer.** Det er skive 14, og autostart uden tray betyder at appen åbner
et vindue ved login. Det er værd at sige højt: **brugeren har valgt autostart før tray**, så den
første udgave starter synligt. Er det forkert, er rækkefølgen forkert — ikke koden.
