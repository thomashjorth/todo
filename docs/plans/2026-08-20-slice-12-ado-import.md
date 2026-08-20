# Skive 12 — ADO-import Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Hent dine tildelte work items fra ADO Server ind som opgaver, med forhåndsvisning og dedup —
samme mønster som Jira-importen i skive 11.

**Architecture:** `ITaskSource` får sin **anden** implementation. Det er skivens egentlige formål:
afsnit 9 siger, at det er her det viser sig, om abstraktionen fra skive 11 duer, eller om den var
Jira-formet. `AdoTaskSource` i `Todo.Host`, en `FakeAdo` på loopback i `Todo.TestSupport`, og
forhåndsvisning plus import efter samme kontrakt som Jiras.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core 10 / SQLite, Angular 22 signal-stores, xunit.v3,
Playwright, Vitest.

**Testtal før skiven:** Core **103**, Api **191**, E2E **35**, Vitest **198** — alle grønne på `main`.

---

## Hvad der er målt, og hvad der ikke er

**Målt 2026-08-20 mod instansen** (se designdokumentets afsnit 10 for det fulde referat):

- `deploymentType: onPremises` — TFS/ADO Server, ikke Cloud.
- Samling `Edora Software` **med mellemrum** (`%20` i en URL), projekt `Saas`, **ingen `/tfs/`-mappe**.
- **PAT som Basic auth med tomt brugernavn** virker: `base64(":" + PAT)`.
- **`api-version=7.1` er GA** for `wiql`, `updates` og `workItems`. `7.2` er preview.
- **WIQL virker** — `POST {collection}/{project}/_apis/wit/wiql?api-version=7.1`.
- WIQL-svaret bærer `asOf`, som er watermarket til inkrementel hentning.
- **De URL'er ADO giver tilbage bruger projektets GUID**, ikke navnet, så de er ikke menneskeligt
  navigerbare. En browse-URL skal bygges selv, som `JiraSettings.BrowseUrl`.

**Måling 0 er kørt 2026-08-20**, og fire fund ændrer skiven. To af dem var ikke forudset.

**`[System.AssignedTo] = @Me` virker.** 12 work items, ikke lukkede, og `asOf` i svaret — watermarket
er gratis også her. Det var den sidste blokerende antagelse.

**Batch-hentning virker med `?ids=`**, svaret er `count` + `value`. Bemærk at et kald med ugyldige
id'er svarer `400` med en **oplysende** besked ("The following Ids are not valid"), altså ikke en tavs
tom liste.

**Der findes intet deadline-felt.** Feltlisten på en Bug har ingen `Microsoft.VSTS.Scheduling.DueDate`.
ADO-opgaver har altså **ingen** deadline at importere. Se beslutning A.

**Notefeltet afhænger af sagstypen.** Sag 15664 er en **Bug** og har `Microsoft.VSTS.TCM.ReproSteps` —
**ikke** `System.Description`. En User Story ville have `System.Description`. Beskrivelsen skal derfor
mappes **pr. `System.WorkItemType`** med et fallback, ikke fra ét felt. Det er samme klasse som Jiras
`duedate`, men værre: der er ikke ét forkert navn, der er flere rigtige.

**`Microsoft.VSTS.Common.StateChangeDate` findes — og det gør ADO lettere end Jira.** Det er
modstykket til Jiras `statuscategorychangedate`, som **ikke** fandtes og tvang os til et
changelog-kald pr. sag. Her kommer `WaitingSince` med i samme svar. **Ingen ekstra kald.**

**Tilstandsnavnene afhænger også af typen.** Målt på de tolv: `New`, `Active`, `Blocked`,
`In Progress`, `PO Review` — og **Test Suite** bruger `In Progress` hvor Bug, User Story og Task bruger
`Active`. Samme betydning, to navne. Det er samme inkonsekvens som Jiras seks ventende statusser, og
det er argumentet for en eksplicit brugervalgt liste frem for en heuristik. Oplagte ventende-kandidater:
**`Blocked`** og **`PO Review`**.

**Felter der findes og er værd at kende:** `System.Title`, `System.CreatedBy` (opgavestiller),
`System.State`, `System.WorkItemType`, `System.CommentCount`, `System.ChangedDate`,
`System.IterationPath`, `System.AreaPath`, `System.Reason`, plus custom-felter (`Custom.Timelog` og et
med et GUID-navn). **Bind hvert felt eksplicit** — camelCase-politikken er ikke i spil her, men
per-type-forskellen er.

## Beslutning A: en standard-deadline på tre dage, som en indstilling

Besluttet af brugeren 2026-08-20. ADO har ingen deadline, så appen sætter en: **`ado.defaultDeadlineDays`,
default `3`.** En importeret ADO-opgave får `Deadline = i dag + N`.

**Serveren udleder den, ikke klienten.** Ellers afhænger deadlinen af hvilken maskine der klikker, og
appen har allerede `IClock` som ejer af "i dag" — det er samme grund som at `WaitingSince` sættes
serverside. Klienten sender **ingenting** om deadline; det er en beslutning, og skive 11 målte at
beslutninger ikke kan sendes over wiren. Forhåndsvisningen **viser** den foreslåede dato, men importen
**genudleder** den.

**Konsekvensen at kende:** forhåndsviser du i dag og importerer i morgen, får du morgendagens
udregning. Vinduet er et døgn, og det er det rigtige — datoen er relativ til importen.

**Ændringen gælder kun fremtidige imports.** Brugerens ord, og det passer med afsnit 4: deadline ejes
altid lokalt, og Jiras due date er kun et forslag ved import som sync aldrig overskriver. ADO foreslår
blot noget appen selv har regnet ud.

**`0` betyder ingen deadline.** Feltet er en **ikke-nullable** `int` frem for nullable: et nullable felt
ville give en `@if`-gren i frontenden, og de tre seneste leverancer har handlet om netop dem. `0` er en
læselig "slået fra" for et dagantal. Afvis negative værdier og noget over 365 — en negativ standard
betyder "overskredet ved import", hvilket er meningsløst, og 300 mod 3 er en sandsynlig slåfejl.

## Beslutning B: filtrér på sagstype

Besluttet af brugeren 2026-08-20. **`ado.workItemTypes`, default `["Bug", "User Story", "Task"]`.**

To af de tolv målte sager er **Test Plan** og **Test Suite** — testartefakter, ikke arbejde man løser.
17 % støj i dag.

**En tom liste betyder alle typer, og det modsiger *ikke* skive 11's lektion om den tomme
projektnøgle.** Forskellen er hvordan tomheden nås: projektnøglen var tom **som udgangspunkt**, så et
tilbagefald til "alle projekter" blev ramt ved at gøre ingenting. Her er defaulten udfyldt, så en tom
liste kræver en **bevidst** rydning. Skriv forskellen ned — den ser ellers ud som en inkonsekvens.

---

## Måling 0 — kørt 2026-08-20, bevaret som opskrift

Kommandoerne står her, fordi de skal køres igen efter en serveropgradering. **Læg ikke pladsholdere som
`DIT-ID` i en kørbar blok** — det blev gjort, og de blev kørt ordret. Brug en variabel.

### Kun brugeren kan køre den

**Ingen agent kan gøre dette** — det kræver instansen og et token. Kør det, og skriv svarene ind i
denne plan, før Task 1 begynder.

Variablerne, i én session:

```powershell
$env:ADO_PAT = 'INDSÆT'
$col = 'https://tfs.edora.dk/Edora%20Software'; $proj = 'Saas'
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$env:ADO_PAT")); $h = @("Authorization: Basic $auth")
```

**0a — virker `@Me`?**

```powershell
@'
{ "query": "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me AND [System.State] <> 'Closed' ORDER BY [System.ChangedDate] DESC" }
'@ | Set-Content -Encoding utf8 "$env:TEMP\ado-mine.json"
curl.exe -s -w "`nHTTP %{http_code}`n" -H $h[0] -H "Content-Type: application/json" -d "@$env:TEMP\ado-mine.json" "$col/$proj/_apis/wit/wiql?api-version=7.1"
```

**0b — felterne på én sag.** Sæt id'et i en variabel, så kommandoen kan køres som den står:

```powershell
$id = 15664
curl.exe -s -H $h[0] "$col/_apis/wit/workItems/$id`?api-version=7.1" | ConvertFrom-Json | Select-Object -ExpandProperty fields | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
```

**Det er den vigtigste af de fire**, og den viser **kun feltnavnene** — ikke værdierne, så der ikke
sendes kundetekst. Den afgjorde at der ingen deadline findes, og at en Bug bruger `ReproSteps` frem for
`System.Description`.

**0c — batch-hentning og tilstandene i én.** Hentes fra sagerne selv frem for fra `workitemtypes`, af
en grund der er værd at kende (se fælden nedenfor):

```powershell
$ids = '15664,16901,17170,17169,17165,17162,16977,17057,17142,17141,16524,17119'
curl.exe -s -w "`nHTTP %{http_code}`n" -H $h[0] "$col/_apis/wit/workitems?ids=$ids&fields=System.State,System.WorkItemType&api-version=7.1"
```

**Fælde: `ConvertFrom-Json` i PowerShell 5.1 kaster på ADO's `workitemtypes`.** Beskeden er
`Cannot process argument because the value of argument "name" is not valid` — den kommer af to JSON-nøgler
der kun afviger i versalfølsomhed, hvilket PS 5.1's parser ikke tåler. Det er **ikke** en
autentificeringsfejl, selvom den optræder samme sted som en. Læs tilstandene af sagerne i stedet, eller
undgå at pipe gennem `ConvertFrom-Json` på det endpoint.

Bemærk `&` i URL'en — **den skal i anførselstegn**, ellers giver PowerShell `AmpersandNotAllowed`.

**0d — tilstandene i projektet:**

```powershell
curl.exe -s -H $h[0] "$col/$proj/_apis/wit/workitemtypes?api-version=7.1" | ConvertFrom-Json | Select-Object -ExpandProperty value | Select-Object name, referenceName
```

**Hvad svarene afgør.** Findes der intet deadline-felt, har ADO-opgaver **ingen** deadline, og de
lander alle i "Uden deadline" — det er en produktbeslutning, ikke en teknikalitet, og den skal siges
til brugeren frem for at opdages. Virker `?ids=` ikke, skal `workitemsbatch` bruges, som er en POST og
ændrer kildens form.

---

## Task 1: Kontrakten

**Files:** `contracts/openapi.yaml`, plus de tre genererede filer.

Fire ruter i `/api/ado/*`, spejlet efter Jiras: `test`, `states`, `preview`, `import`. Samme
begrundelse for metoderne som i skive 11: `test` og `preview` er **POST**, fordi de er handlinger man
beder om og forventer at gentage; `states` er en **GET**, fordi en liste af navne er en cachebar
ressource.

`AdoPreviewRow` spejler `JiraPreviewRow`: `key`, `title`, `url` (**required**, se nedenfor), `note`,
`deadline`, `requester`, `state`, `isWaiting`, `alreadyImported`, `excluded`.

**`url` er required og ikke nullable**, af samme grund som i "Åbn sagen"-leverancen: en
forhåndsvisning kan ikke ske uden en konfigureret samling, så URL'en er aldrig fraværende — og et
nullable felt ville give en `@if`-gren, som er umålt indtil et fixture renderer den.

**`AdoImportRow` bærer `state`, ikke `isWaiting`.** Skive 11 målte hvorfor: NSwag udsender ikke
`[Required]` på en ikke-nullable værditype, og `[Required]` er DataAnnotations som System.Text.Json
ikke håndhæver — en fraværende bool bliver `false`, en **gyldig** værdi, så handleren kan ikke afvise
den. En fraværende string bliver `null`, og *det* kan afvises. **Kendsgerningen kan sendes;
beslutningen kan ikke.**

**Regn med at et nyt required felt fælder `Spec_project_passes_the_type_checker`** hvis en håndskrevet
spec-fixture føres ind i en genereret type. Målt tre gange nu: det er kun de fixtures der bruges som
argument til en `new …()`, ikke dem der bruges som rå svarkrop til `flush(...)`.

## Task 2: Indstillingerne

`ado.baseUrl` (samlingen), `ado.project`, `ado.token`, `ado.waitingStates`, `ado.includeWaiting`.

**Tokenet får sit eget endpoint**, `PUT`/`DELETE /api/settings/ado-token`, af samme grund som Jiras:
`PUT /api/settings` er en fuld erstatning der læser et fraværende felt som *ryd*, så et token på den
rute ville blive slettet af enhver anden ændring.

**Genbrug `SettingList.Read`** — den blev udtrukket i uddelegeringsleverancen netop for dette. Men
**genbrug ikke `SettingList.Write`** til tilstandsnavne: den deduper versalufølsomt, og skive 11 målte,
at ordinal sammenligning er bevidst for statusnavne, fordi en versalufølsom fletning ville slå to
tilstande sammen som systemet holder adskilt. Jiras `StatusList` er præcedensen — læs dens
doc-kommentar.

**En femte indstillingsgruppe.** Siden har fire; ADO bliver den femte, mellem Uddelegering og
Jira-import eller efter den. Rækkefølgen er "dine egne først, kilderne sidst", så ADO hører hos
kilderne.

**`AdoSettings` bliver sin egen record, ikke en generalisering af `JiraSettings`.** Det er skivens
egentlige spørgsmål, og svaret skal komme af at bygge den — ikke af at antage en fælles form på
forhånd. Konvergerer de to, er en fælles abstraktion en oprydning bagefter med to eksempler at
retfærdiggøre den.

## Task 3: `AdoTaskSource` og `FakeAdo`

`ITaskSource`'s anden implementation. **Det er her abstraktionen prøves**, og rapporten skal sige
hvad der **ikke** passede — det er skivens vigtigste output, ikke koden.

`FakeAdo` på **`127.0.0.1`**, som `FakeJira`. `NoRealInstanceTests` forbyder `edora.dk` i enhver
kildefil, og den vagt er grøn i dag — hold den grøn.

**Basic auth med tomt brugernavn**, `base64(":" + PAT)`. Ikke Bearer; det er Jiras form.

**Byg URL'en som streng, ikke med `UriBuilder`.** Målt i skive 11: den af-escaper `%20` tilbage til et
mellemrum mens `%3D` bliver stående — og her er `%20` **i samlingsnavnet**, så fælden er ikke teoretisk.

Vagter der skal ses fejle: at samlingen og projektet er i URL'en, at tomt projekt afvises **før**
kaldet, at Basic og ikke Bearer sendes, og at paginering læses helt igennem.

## Task 4: Endpointsene

Spejlet efter `JiraEndpoints`. **Udtræk rollebeslutningen som `JiraStatusRoles` blev udtrukket** — ét
sted, kaldt fra forhåndsvisning og import, frem for den samme regel to gange. Skive 11 målte, at to
kaldesteder er to steder reglen kan glemmes, og at kun det ene havde en test.

`excluded` bærer en fejlkode frontenden oversætter. **Hver ny kode skal have en `errors.<kode>`-nøgle i
begge sprogfiler**, ellers fejler `ErrorCodeTranslationTests` — og den fanger det paritetstesten
strukturelt ikke kan: en nøgle der mangler i **begge** filer.

## Task 5: Frontenden

En importskærm som Jiras, og en femte indstillingsgruppe. **`app.routes.ts` har fire ruter i dag; ADO
bliver den femte**, og `ContrastTests` går dem alle igennem i begge temaer — tallet står skrevet i
`CLAUDE.md` og designdokumentets afsnit 10 og skal rettes.

`SettingsStore.save` bærer i dag **otte** felter i `current`; ADO's fem gør det tretten. **Udvid den
eksisterende regressionstest** frem for at lægge en ny ved siden af.

## Task 6: E2E, kontrast og dokumentation

Én rejse hele vejen: konfigurér, forhåndsvis, importér, og find opgaven i listen. Opsnap ADO-kaldene
med `page.RouteAsync`, og **`/api/system/open-link` skal fortsat opsnappes og afbrydes** — afbrydelsen
gælder den enkelte test, ikke filen.

**Byg før E2E.** `Todo.E2E.csproj` har intet build-trin, og hosten servérer bare `wwwroot`.

Dokumentér **hvad abstraktionen ikke tålte**. Det er skivens formål, og det er den ene ting der ikke
kan læses ud af koden bagefter.

---

## Hvad der kan gå galt

**Feltnavnene.** Måling 0b er den eneste vej. Jiras `duedate` gav null i hver deadline uden at en test
faldt, fordi testen for en sag *uden* deadline bestod.

**Samlingsnavnets mellemrum.** `%20` gennem en strengbygget URL, en `HttpClient` og en falsk server på
loopback — tre steder det kan af-escapes. Læg et mellemrum i `FakeAdo`s samlingsnavn, så fælden er
dækket frem for undgået.

**Fem håndskrevne wire-fixtures uden compiler over sig**, og ADO gør dem til syv. Det er stedet en
fremtidig skive taber et felt; hullet er dokumenteret i `CLAUDE.md`, ikke lukket.

**Ingen test kalder den rigtige instans.** Målingerne i afsnit 10 er afskrevet fra 2026-08-20, ikke fra
en løbende kørsel. En serveropgradering ville vise sig i brug, ikke i `dotnet test`.
