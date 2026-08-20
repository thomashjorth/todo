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
ligger `wwwroot\` med **24** filer, `web.config`, `Todo.Host.staticwebassets.endpoints.json` (55 kB)
og **tre** `.pdb`. Samlet 111 MiB.

**3. Fire af de syv løse filer er fjernelige — målt, ikke antaget.** `web.config`, de tre `.pdb` og
`staticwebassets.endpoints.json` blev slettet fra outputtet, hvorefter `/`, `/api/health` og
`/scalar/` alle svarede **200**. `staticwebassets`-filen er ikke i spil, fordi appen bruger
`UseStaticFiles()` og ikke `MapStaticAssets()`. Tilbage står **`wwwroot`**, som er det egentlige
arbejde i beslutning A.

**4. `icon.ico` bliver ikke udgivet.** `Todo.Host.csproj` har
`<Content Include="..\Todo.Web\public\favicon.ico" Link="icon.ico" CopyToOutputDirectory="PreserveNewest" />`
— og `CopyToOutputDirectory` gælder **build**-output. Publish kræver `CopyToPublishDirectory`. Den
udgivne app kalder derfor `SetIconFile(Path.Combine(AppContext.BaseDirectory, "icon.ico"))` på en sti
der ikke findes. Den crashede ikke headless, men **vinduesvejen er utestet** — headless springer
`PhotinoWindow` over, så målingen siger intet om hvad Photino gør med en manglende ikonfil. Bemærk at
`wwwroot\favicon.ico` **er** udgivet, så der findes en anden sti til samme fil.

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

**7. `wwwroot` indeholder tre udgaver af hver fil.** Angular-bygningen lægger `.br` og `.gz` ved
siden af hver `.js`, `.css`, `.json`, `.html` og `.ico` — plus `.gitkeep` og
`prerendered-routes.json`. Det er derfor 24 filer og ikke 8, og det er en beslutning i Task 2:
embedder man alle tre, tredobler man nyttelasten for en app der servérer på loopback.

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
gratis, hvis det virker. **Mål (c) først**; den er den eneste der giver én fil uden at skrive til
disk.

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

**Vagten er ikke en unittest.** En påstand om at appen finder sit `wwwroot` kan kun måles på en
udgivet exe, og en publish tager omkring 40 sekunder. Den hører derfor i `scripts\publish.ps1`
(Task 3), ikke i `dotnet test`.

**Versionen.** Sæt `<Version>` i csproj'en, og lad `/api/health` bære den. Der findes allerede en test
på health-svaret — find den og udvid den frem for at lægge en ny ved siden af.

**De fire suppressioner.** `<IsTransformWebConfigDisabled>`, `<DebugType>none</DebugType>` eller
tilsvarende, og den egenskab der slår static-web-assets-manifestet fra. **Verificér hver af dem ved
at udgive igen og tælle filerne** — en egenskab der ikke gør noget, ser ud som en der gør.

## Task 2: `wwwroot` ind i exe'en

`GenerateEmbeddedFilesManifest` + `ManifestEmbeddedFileProvider` som webroot.

**Afgør hvad der skal med.** De tre udgaver af hver fil er målt: embedder man `.br` og `.gz`, bærer
exe'en tre kopier af alt, og appen servérer på loopback hvor komprimering ikke køber noget. Forslag:
kun de ukomprimerede, plus en note om hvorfor. `.gitkeep` og `prerendered-routes.json` skal ikke med.

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

## Hvad skiven ikke gør

**Ingen opdateringsmekanisme.** Ingen signering, ingen SmartScreen-håndtering, ingen
delta-opdatering. En usigneret exe på egen maskine er den valgte pris; vælger man senere en
installer, er det en skive for sig.

**Ingen trimming.** Se beslutning A.

**Ingen tray og ingen notifikationer.** Det er skive 14, og autostart uden tray betyder at appen åbner
et vindue ved login. Det er værd at sige højt: **brugeren har valgt autostart før tray**, så den
første udgave starter synligt. Er det forkert, er rækkefølgen forkert — ikke koden.
