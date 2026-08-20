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

~~**En tom liste betyder alle typer**~~ — **forkert, omgjort i Task 2.** Argumentet var, at tomheden
nås på en anden måde end den tomme projektnøgle: defaulten er udfyldt, så en rydning er bevidst. Det
holder ikke, af to grunde. Den ene er skive 11's egen lektion ordret — fraværet af en afgrænsning er
ikke en neutral standard, og "alle typer" trækker netop de testartefakter ind som filtret findes for at
holde ude. Den anden er, at **lagringsformen ikke kan bære påstanden**: en tom liste gemmes som *ingen
række*, og læseren kan derfor ikke skelne *aldrig konfigureret* fra *bevidst tømt* — de er den samme
byte. Så: en tom liste **afvises** på PUT med `ado.workItemTypesRequired`, og en fraværende række læses
som de tre standardtyper. Kontrakten er rettet til at sige det.

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

`AdoPreviewRow` spejler `JiraPreviewRow`, med `required: [key, title, url, state, workItemType,
isWaiting, alreadyImported]` plus `note`, `deadline`, `requester`, `waitingSince` og `excluded` som
nullable.

**`workItemType` er med på *begge* rækker** — også `AdoImportRow` — fordi serveren skal kunne genudlede
typefiltret fra beslutning B ved import. Planens første udgave udelod det; rettet efter Task 1.

Og **`AdoImportRequest` er `{ rows: AdoImportRow[] }`**. Planens første udgave nævnte den kun i
rute-tabellen, ikke i skemalisten, så kontrakten ville ikke have kunnet opløses.

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

## Task 2: Indstillingerne — kørt

**Syv, ikke fem.** Planens første udgave nævnte kun `ado.baseUrl` (samlingen), `ado.project`,
`ado.token`, `ado.waitingStates` og `ado.includeWaiting` — men beslutning A og B er også
indstillinger, og kontrakten fra Task 1 erklærer dem: `ado.workItemTypes` og
`ado.defaultDeadlineDays`. Rettet efter Task 2.

**Standarden på tre dage kom til at ligge i kontrakten, og det er det ene sted den kan ligge.**
`adoDefaultDeadlineDays` er en ikke-nullable `int` hvor `0` betyder *ingen deadline*, så
System.Text.Json giver `0` for både et fraværende felt og et bevidst nul — og de to skal ende
forskellige steder. Løsningen er `default: 3` på **`SettingsRequest`** i kontrakten: NSwag laver den om
til en property-initializer, som deserialiseringen lader stå for et fraværende felt, så fravær binder
til 3 og et bevidst 0 bliver 0. `SettingsResponse` har med vilje **ingen** default — en initializer der
ville lade en handler der aldrig satte feltet svare 3, og gøre testen for det uophævelig.
Målt: fjernes initializeren, falder **tre** eksisterende tests i `SettingsEndpointsTests` der påstår om
hele `Settings`-tabellen, plus de to nye.

**Standarderne kommer af fraværet af en række**, ikke af en seeding — hverken de tre typer eller de tre
dage lægger en række i en tom database. Derfor gemmes dagantallet kun når det **afviger** fra
`AdoDefaults.DeadlineDays`, på samme måde som de to bool'er kun gemmes når de er slået til.

**Testtal efter Task 2:** Core **122** (+19), Api **219** (+28, med `ContractDriftTests` fortsat rød på
de fire `/api/ado/*`), E2E **35** (uændret), Vitest **198** (uændret).

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

## Task 3: `AdoTaskSource` og `FakeAdo` — kørt

`ITaskSource`'s anden implementation. **Det er her abstraktionen prøves**, og rapporten skal sige
hvad der **ikke** passede — det er skivens vigtigste output, ikke koden.

`FakeAdo` på **`127.0.0.1`**, som `FakeJira`. `NoRealInstanceTests` forbyder `edora.dk` i enhver
kildefil, og den vagt er grøn i dag — hold den grøn. **Verificeret ved at bryde den**: et
`// tfs.edora.dk` i både `AdoTaskSource.cs` og `FakeAdo.cs` fælder den og navngiver begge filer, så
den scanner faktisk det nye.

**Basic auth med tomt brugernavn**, `base64(":" + PAT)`. Ikke Bearer; det er Jiras form.

~~**Byg URL'en som streng, ikke med `UriBuilder`.** Målt i skive 11: den af-escaper `%20` tilbage til
et mellemrum mens `%3D` bliver stående — og her er `%20` **i samlingsnavnet**, så fælden er ikke
teoretisk.~~ — **forkert begrundelse, målt tre gange i Task 3.** URL'en *skal* bygges som streng, men
ikke af den grund. Målt på net10.0 ved at mutere `AdoTaskSource.UriFor` og køre suiten:
`UriBuilder` **bevarer** `%20` i både sti og query, og en ombygning med den giver **36 grønne**;
en interpolation af `Uri`-objektet af-escaper godt nok (`Uri.ToString()` giver `/Fake Collection`),
men `new Uri(...)` re-escaper mellemrummet på vejen ind igen, så også den giver **36 grønne**. En
**sti** heler altså sig selv. Det der *kan* måles, er **dobbelt** escaping —
`Fake%2520Collection` — og det er præcis hvad `The_space_in_the_collection_name_stays_escaped_on_the_wire`
blev set fælde. Skive 11's måling gjaldt en **query string** (JQL'en), og den overføres ikke til en
sti. Læs ikke fælden som større end den er, og lad alligevel strengbygningen stå: batch-kaldet
*har* en query string, og der heler ingenting sig selv.

Vagter der skal ses fejle: at samlingen og projektet er i URL'en, at tomt projekt afvises **før**
kaldet, at Basic og ikke Bearer sendes, og at paginering læses helt igennem. **Alle fire er set
fejle**, plus elleve mutationer mere — se leverancerapporten. To fund er værd at have her:

- **Kun ét kald pagineres, og det er ikke det man tror.** WIQL pagineres ikke; **hydreringen** gør,
  fordi `?ids=` er kappet ved 200. At måle at hver chunk læses kræver derfor **mere end 200 sager**
  i `FakeAdo` — at gøre kildens chunk-størrelse til en testkrog ville have målt krogen. `FakeAdo`
  har derfor en `filler`-parameter, og vagten kører med 250.
- **Rækkefølgen skal genskabes.** WIQL bærer `ORDER BY`; batch-svaret lover ikke at komme i den
  rækkefølge man spurgte. Jira havde ikke problemet, fordi dets `/search` returnerede sagerne selv.

**Tre ting Task 3 skal måles på igen, som kun brugeren kan gøre** (afsnittet "Måling 0" er
opskriften; disse tre er nye og blev *ikke* dækket 2026-08-20):

- **0e — én rigtig beskrivelse.** 0b printede med vilje kun feltnavne, så ingen har set hvordan
  instansens rigtige HTML ser ud. Derfor er HTML → markdown **udskudt** (se nedenfor), og derfor
  mangler konverteren et rigtigt eksempel at bygges mod.
- **0f — hvilken form har `System.CreatedBy`?** Et objekt med `displayName`, eller den ældre streng
  `Navn <adresse>`? Ukendt. `AdoTaskSource` læser **begge** frem for at gætte, og begge er dækket af
  en test — men den ene af dem er forkert, og målingen siger hvilken.
- **0g — bærer `_apis/wit/workitemtypes` en `states`-liste?** 0d spurgte kun om `name` og
  `referenceName`. Indtil det er målt, hentes tilstandsnavnene **af brugerens egne sager** (0c's
  målte form), hvilket koster, at en tilstand ingen af dine sager står i lige nu ikke kan vælges.

**HTML → markdown er udskudt, med begrundelse.** Kontrakten siger at `note` er "converted to
CommonMark"; det er **endnu ikke sandt** — `AdoTaskSource` giver ADO's HTML videre uændret. Tre
grunde: der findes **ingen målt prøve** af instansens rigtige rich text (0b printede kun feltnavne,
med vilje), skive 13 skal have **samme** konverter til kommentar-HTML, så én konverter bygget mod to
målte prøver slår to bygget mod nul, og `CLAUDE.md` kræver at en markup-konverter måles på det
**renderede** resultat gennem appens egen `marked` — altså frontend-arbejde, som er Task 5/6. Indtil
da renderer `marked` inline-HTML igennem, så noten er læselig frem for maltrakteret. **Den skal
lukkes før skiven vises brugeren**, og kontraktens sætning skal enten indfries eller rettes.

**`AdoDeadline` er beslutning A's regel, og den bor i Core.** `AdoDeadline.For(today, days)` svarer
`null` ved `0` og ellers `today.AddDays(days)`. To kaldesteder kan ikke dele kodevej — kilden udleder
den mens den læser sagerne, importen udleder den igen fra rækker klienten sendte uden deadline — så
den er sit eget sted af præcis den grund `JiraStatusRoles` blev udtrukket.

**`ExternalTask` fik to felter, og det er skivens svar på om abstraktionen var Jira-formet.**
`ItemType` (Jira svarer `null`) og `StatusChangedAt` (Jira svarer `null`, fordi DC 10.3.24 ikke har
feltet). `FetchStatusChangedAtAsync` blev **beholdt** som fallback frem for at blive en no-op: ADO
implementerer den som et rigtigt enkeltopslag, så et kald med kun en nøgle får et rigtigt svar, men
ingen behøver at bruge den, fordi feltet ligger på rækken. **Task 4 skal læse feltet først og kun
kalde metoden når det er `null`.**

**Testtal efter Task 3:** Core **143** (+21), Api **256** (+37, med `ContractDriftTests` fortsat rød
på de fire `/api/ado/*` og intet andet — verificeret ved at sammenligne kontraktens 27 operationer
med de 23 mappede `/api`-ruter), E2E **35** (uændret), Vitest **198** (uændret).
De 21 Core-tests er **9** `AdoDeadlineTests` og **12** nye i `AdoSettingsTests` (om `BrowseUrl`).
De 37 Api-tests er **36** `AdoTaskSourceTests` og **1** `AdoTaskSourceRegistrationTests`.

## Task 4: Endpointsene — kørt

Spejlet efter `JiraEndpoints`. **Udtræk rollebeslutningen som `JiraStatusRoles` blev udtrukket** — ét
sted, kaldt fra forhåndsvisning og import, frem for den samme regel to gange. Skive 11 målte, at to
kaldesteder er to steder reglen kan glemmes, og at kun det ene havde en test.

**Reglen blev `AdoStateRoles.IsWaiting(state, settings)`, og den har ingen vagt-gren.** Verificeret
frem for antaget: `AdoSettings` har `WaitingStates` + `IncludeWaiting` og ingen `DutyStates`/`OnDuty`,
kontrakten erklærer syv ADO-indstillinger og ingen af dem er en vagt, og hverken Måling 0 eller
`AdoTaskSource` nævner en. En vagt-gren ville derfor være en indstilling brugeren aldrig fik tilbudt —
og uopnåelig, hvilket er værre end fraværende. Reglen svarer en `bool` frem for en to-værdi-enum:
Jiras enum tjener sig hjem på tre roller, to ville være en bool med ekstra trin.

**Der var to regler at udtrække, ikke én.** Planen nævnte kun rollebeslutningen, men typefiltret fra
beslutning B har præcis samme form: `AdoTaskSource` gør listen til et `IN (...)`-led *før* kaldet,
mens importen skal spørge om **én** rækkes type *efter* at klienten sendte den tilbage — to former
der ikke kan dele kodevej. Den bor derfor i `AdoWorkItemTypes` (`Effective` + `Allows`), og
`AdoTaskSource.AssignedQuery` blev lagt om til at bruge `Effective`, så query-teksten og
rækkefiltret ikke kan skride fra hinanden.

**Læs `ExternalTask.StatusChangedAt` frem for at kalde `FetchStatusChangedAtAsync`.** ADO bærer
`Microsoft.VSTS.Common.StateChangeDate` med i samme svar, så `WaitingSince` er gratis; metoden er kun
fallback for en kilde uden feltet, og Jira er den. Et kald pr. række ville koste en rundtur ADO ikke
behøver — og `The_state_change_date_arrives_with_the_page_and_costs_no_extra_call` påstår at siden
ikke kostede nogen.

~~`ITaskSource` siger: læs feltet først og kald metoden når det er `null`.~~ — **fallbacket tages
ikke, og det er målt.** `AdoTaskSource.FetchStatusChangedAtAsync` læser **samme felt gennem samme
parse** som `Map` gør, så for denne kilde kan den kun svare `null` en gang mere — mod én spildt
rundtur pr. række med et ulæseligt tidsstempel. Målt ved at lægge `?? await
FetchStatusChangedAtAsync(...)` ind: `An_unreadable_state_change_date_is_not_chased_with_a_second_call`
faldt med `Assert.Empty() Failure: Collection was not empty / Collection: [17162]`, og `WaitingSince`
var stadig `null`. Fallbacket hører til en kilde **uden** feltet.

~~**Fem nye fejlkoder findes allerede** … `ado.excludedWaiting` mangler stadig og hører her.~~ —
**for lidt: der manglede seks.** `ado.excludedWaiting` *plus* rækkevalideringens fem —
`ado.rowKeyRequired`, `ado.rowTitleRequired`, `ado.rowTitleTooLong`, `ado.rowStateRequired` og
`ado.rowWorkItemTypeRequired`. De fire første er Jiras modstykker; den femte har **ingen** Jira-modpart,
fordi Jiras import ikke har et filter at anvende igen: en række uden type ville ellers falde ud af
importen som "ikke en type du bad om", hvilket ligner en tabt sag frem for en afvist request.
`ErrorCodes` har nu **37** koder, og `ErrorCodeTranslationTests` siger tallet selv i sin fejlbesked.

`excluded` bærer en fejlkode frontenden oversætter. **Hver ny kode skal have en `errors.<kode>`-nøgle i
begge sprogfiler**, ellers fejler `ErrorCodeTranslationTests` — og den fanger det paritetstesten
strukturelt ikke kan: en nøgle der mangler i **begge** filer. Set fejle: `errors.ado.excludedWaiting`
fjernet fra **begge** filer giver
`1 of 37 error code(s) have no message under "errors" in src/Todo.Web/public/i18n/da.json` i begge
teori-tilfælde.

**`POST /api/ado/test` bruger `NotConfigured`, ikke `NotReady` — og det er den ene rute hvor Jiras form
passede.** `TestAsync` kalder `_apis/connectionData` på samlingsniveau og rører aldrig `ProjectOf`, så
en afvisning for tomt projekt ville sende brugeren efter et felt requesten ikke bruger — og bryde den
naturlige opsætningsrækkefølge, hvor man prøver tokenet *før* man ved om projektnavnet er stavet rigtigt.
De tre andre ruter kræver projektet, fordi ADO afgrænser en WIQL i **stien**.

**Forhåndsvisningen tjekker *ikke* typelisten, importen gør.** Kilden afviser en tom typeliste før sin
første request og har sin egen test for det, så en vagt i forhåndsvisningen kunne **ikke ses fejle**:
med eller uden den går ingen WIQL ud, og svaret bærer samme kode. Importen taler aldrig med ADO og har
intet andet sted at afvise fra — derfor `NoWorkItemTypes` kun der.

**`url ?? throw` fælder ingenting, og det er målt.** Byttet til `?? string.Empty` består alle 283
Api-tests, fordi `NotReady` allerede har fastslået både samling og projekt, som er præcis hvad
`BrowseUrl` kræver. Grenen er altså uopnåelig; `throw`'et står som nedskrevet invariant efter Jiras
præcedens, og der er **ingen** test for det — en påstand der ikke kan fejle ville være værre end ingen.

**`/api/tasks`' `externalUrl` havde ingen ejer i planen, og Task 4 tog den.** `ToContract` beregnede
kun Jiras URL, så en importeret ADO-opgave ville have haft `externalUrl: null` og en "Åbn sagen"-gren
der aldrig renderes — backend-arbejde, som ingen af Task 5's frontend-opgaver kunne have lavet.
`ToContract` tager nu `AdoSettings` med, og `task.SourceId` afgør formen i en `switch` **uden**
fallthrough: en ADO-nøgle er et bart tal, så "42" findes i hvert af de tre systemer. Målt ved at
fjerne ADO-grenen igen: `Importing_writes_the_rows_as_tasks_with_the_derived_deadline` faldt med
`Assert.Equal() Failure: Strings differ / Actual: null`.

**To mutationer kunne ikke skrives, og det er selve designet.** "Importens deadline udledt af
klientens række" er umulig: `AdoImportRow` **har** intet deadline-felt. Det målbare substitut er
`AdoDeadline.For(...)` → `clock.Today`, som fælder både
`Importing_writes_the_rows_as_tasks_with_the_derived_deadline` (`Expected: 23-08-2026 / Actual:
20-08-2026`) og `Zero_days_imports_a_task_without_a_deadline`. Og **døgnvinduet** — forhåndsvis i dag,
importér i morgen — kan ikke måles med ét fast ur pr. testklasse; hvad der *er* målt, er at importen
udleder fra `IClock` og respekterer `0`.

**Testtal efter Task 4:** Core **164** (+21), Api **283** (+27, **helt grøn** — `ContractDriftTests`
lukkede med de fire ruter, set fejle ved at fjerne `app.MapAdo()`: `Assert.Equal() Failure: Sets
differ`), E2E **35** (uændret), Vitest **198** (uændret).
De 21 Core-tests er **10** `AdoStateRolesTests` og **11** `AdoWorkItemTypesTests`.
De 27 Api-tests er alle `AdoEndpointsTests`. `ErrorCodeTranslationTests` voksede **ikke** — den er en
`[Theory]` over de to sprogfiler, så seks nye koder kan ikke flytte tallet.
Klokken er fastfrosset i `AdoEndpointsTests` (`FixedClock(FakeAdo.Today)`), hvad `JiraEndpointsTests`
ikke havde brug for: hver deadline her er regnestykke på *i dag*, så en datopåstand ville ellers
påstå om den dag suiten tilfældigvis kører.

## Task 5: Frontenden — kørt

En importskærm som Jiras, og en femte indstillingsgruppe. **`app.routes.ts` har fire ruter i dag; ADO
bliver den femte**, og `ContrastTests` går dem alle igennem i begge temaer — tallet står skrevet i
`CLAUDE.md` og designdokumentets afsnit 10 og skal rettes.

**Genvejsbogstavet er `a`.** Frit målt: `app.html` havde `o/i/j/s` og `task-list.html` `n/v/m`, og
`ShortcutStore.register` er et `Map.set` — **last-writer-wins uden nogen vagt**. Målt ved at sætte
nav-ado til `j`: **nul** af 239 Vitest faldt, og badge-mærkaten ville heller ikke afsløre det, fordi
bogstavet i mærkaten er skrevet i skabelonen og ikke afledt af `appShortcut`. Kun en E2E-rejse på det
kolliderede bogstav ville fange det, og kun fordi `Alt_J_follows_the_jira_link` tilfældigvis findes.
Chrome på Windows binder intet `Alt+A`; de nære naboer er `Ctrl+A` og `Alt+D`, andre
modifikator-/tastesæt.

**To E2E-konstanter kunne ikke vente på Task 6, og planen havde dem ikke.** En femte nav-link fælder
`KeyboardJourneyTests` med det samme: `BadgeCount` var **7** (set fejle: `Locator expected to have
count '7' But was: '8'`) og `TrailToTheField` listede fire nav-testid'er, så tab-rækkefølgen var
forkert. Begge er *tal om nav'en*, ikke nye tests, og de er rettet her, fordi `dotnet test Todo.sln`
ellers står rødt mellem Task 5 og Task 6.

**Hele `settings.spec.ts`s forventede PUT-kroppe flyttede sig, og planen nævnte kun de to
fixtures.** `adoWorkItemTypes` er aldrig tom, når den er læst fra serveren, så den følger med **hver**
gemning — otte `toEqual`-påstande på requestkroppen fik `adoWorkItemTypes: defaultTypes` tilføjet.
Det er ikke en fejl i store'n; det er det en fuld erstatning betyder.

**`adoWorkItemTypes` har en tredje tilstand de andre lister ikke har, og "udelad for at rydde" er
forkert for den.** Fraværende betyder *genopret de tre standardtyper*, og en nærværende tom liste
**afvises** med `ado.workItemTypesRequired`. Mønstret `x.length === 0 ? undefined : x` ville derfor
gøre den kode **uopnåelig fra UI'et** — og værre: at fjerne den sidste type ville tavst lægge de tre
standarder tilbage, hvilket ligner at appen fortrød klikket. Reglen er derfor
`types.length === 0 && !('adoWorkItemTypes' in changes)`: tomhed sendes **kun** når kalderen bad om
den. Set fejle i begge retninger — det naive `undefined` fælder to tests
(`expected {} to deeply equal { adoWorkItemTypes: [] }`), og et ubetinget `types` fælder syv, fordi en
sprogændring før første læsning så ville bære `adoWorkItemTypes: []` og blive afvist.

**Noten vises som *at* den findes, ikke som hvad der står i den.** ADO's felt er rå HTML, ikke
CommonMark (Task 3's ejede afvigelse), og importen fører den uændret videre til noten, hvor `marked`
lader inline-HTML passere — så `<div>` og `<br>` **renderer** i detaljepanelet og læses fint. En
forhåndsvisning der viste markuppen som tekst ville altså vise noget brugeren aldrig ser, og et
`[innerHTML]` her ville både være en XSS-flade og en påstand om en konvertering der ikke er sket.
Linjen er `ado.hasNote` ("Beskrivelsen følger med."). Set fejle ved at bytte den til `{{ row.note }}`.

**`waitingSince` er et tidsstempel og må derfor **ikke** gennem `deadlineDate`.** `formatDeadline`s
regex kræver præcis `YYYY-MM-DD` og svarer **tom streng** for et ISO-tidsstempel, så linjen ville stå
som "Venter siden " uden dato. Målt: `expected 'Venter siden ' to match /\b14\b/`. Komponenten har
derfor sin egen `waitingSince()`, som bruger `new Date` med vilje — modsat deadline-reglen, fordi
dette *er* et øjeblik og den lokale dag netop er spørgsmålet.

**De 13 `errors.ado.*`-nøgler fandtes fra Task 3 og 4** — briefen sagde 14. Ingen nye fejlkoder blev
lagt på.

**Testtal efter Task 5:** Core **164** (uændret), Api **283** (uændret), E2E **35** (uændret, to
konstanter rettet), Vitest **239** (+41).
De 41 fordeler sig: **12** `ado-store.spec.ts`, **13** `ado-import.spec.ts`, **8** nye i
`settings-store.spec.ts` og **8** nye i `settings.spec.ts`. Skævheden er, at skærmen er alt hvad denne
opgave er: der kom ingen ren funktion til, så Core og Api står helt stille.

`SettingsStore.save` bærer i dag **otte** felter i `current` (talt i `settings-store.ts`, linje
117–124); ADO's **seks** gør det **fjorten** — ikke tretten, som planen skrev: tokenet er ikke et felt
på den rute. **Udvid den eksisterende regressionstest** frem for at lægge en ny ved siden af.

**Fælden i `current` er `adoDefaultDeadlineDays`.** Mønstret i `put()` er `x.length === 0 ? undefined :
x`, altså "udelad for at rydde" — og skrives dagantallet med `|| undefined` eller et tilsvarende
sandhedstjek, forsvinder et bevidst `0`, som er den ene værdi der betyder noget særligt. Udelades
nøglen, binder serveren til 3; sendes `0`, gemmes `0`. Bemærk også at
`new SettingsRequest({...})`-konstruktøren **ikke** anvender sin `= 3`-default når den får et
data-objekt — den kopierer kun nøglerne der er der — så defaulten kommer fra serveren, ikke fra
klienten.

**Noten er HTML, ikke markdown, indtil konverteren findes** — se Task 3. Skærmen skal altså regne med
at `note` kan indeholde `<div>` og `<br>`; noteeditoren viser dem som tekst.

**De betingede felter Task 4 efterlader, så vagterne kan finde dem.** `AdoPreviewRow`s nullable felter
er `note`, `deadline`, `requester`, `waitingSince` og `excluded`; `url`, `state`, `workItemType`,
`isWaiting` og `alreadyImported` er altid der. Skærmen får derfor mindst disse grene at måle:
`deadline` (kan være `null` når dagantallet er `0`), `requester`, `note`, `waitingSince`, `excluded`
(= `ado.excludedWaiting`), `alreadyImported`, og `isWaiting` — plus `workItemType`, som er **ny mod
Jira** og *ikke* en gren, fordi den altid er udfyldt. Der er **ingen** vagt-mærkat: `isDuty` har ingen
ADO-modpart. Bemærk at `deadline`-grenen er den ene ingen Jira-skærm har, fordi Jiras deadline kom fra
sagen og ADO's er appens eget regnestykke — den er værd at vise som *foreslået*, ikke som hentet.

**To wire-fixtures beskriver nu en form serveren ikke sender:** `settings-store.spec.ts` linje 67 og
`settings.spec.ts` linje 65 har `adoWorkItemTypes: []`, men svaret bærer altid mindst de tre
standardtyper — en tom liste kan ikke opstå. Ret begge til `['Bug', 'User Story', 'Task']` i Task 5.
Og `settings-store.spec.ts` linje 361 påstår
`expect(own.filter((key) => /token/i.test(key))).toEqual(['hasJiraToken'])`; den skal have
`'hasAdoToken'` med, i **erklæringsrækkefølge**.

## Task 6: E2E, kontrast og dokumentation

Én rejse hele vejen: konfigurér, forhåndsvis, importér, og find opgaven i listen. Opsnap ADO-kaldene
med `page.RouteAsync`, og **`/api/system/open-link` skal fortsat opsnappes og afbrydes** — afbrydelsen
gælder den enkelte test, ikke filen.

**Playwright kan ikke bruge `FakeAdo`** — den lever i hostens proces — så `**/api/ado/preview`,
`**/api/ado/test` og `**/api/ado/states` skal svares af en rutehandler. Og skive 11's lektion ét
niveau dybere gælder ordret: **kroppen skal bære felterne**, ellers er grenene umålte. Her er
`workItemType` og `isWaiting` de to der ikke findes i Jiras svar, og et fraværende `deadline` er en
tredje tilstand at svare i. `alreadyImported`-grenen kan derimod nås **uden** en rutehandler, fordi
importen er ægte: importér, forhåndsvis igen.

**Én ny `TaskItemBuilder`-krog mangler.** `FromRetro` og `FromJira` findes; en ADO-opgave i
opgavelisten med et rigtigt `externalUrl` kræver `FromAdo` **plus** en gemt `ado.baseUrl` *og*
`ado.project` — to ting mod Jiras én, fordi `AdoSettings.BrowseUrl` kræver begge. Task 4 lagde
`externalUrl` for ADO på `/api/tasks` og målte det gennem importen; en builder-vej findes ikke endnu.

**Byg før E2E.** `Todo.E2E.csproj` har intet build-trin, og hosten servérer bare `wwwroot`.

**Grenene Task 5 efterlod, med vælger.** Indstillingssiden er allerede halvt dækket, fordi
`ContrastTests`' settings-teori går siden igennem: de altid-renderede dele af `ado-settings` blev målt
i denne kørsel og var grønne. Det der **mangler** en farve er hver af disse:

- Indstillinger: `[data-testid="ado-token-stored"]`, `[data-testid="ado-clear-token"]`,
  `[data-testid="ado-connection"]` (kræver et svar på `**/api/ado/test`),
  `[data-testid="ado-state-row"]` (kræver et svar på `**/api/ado/states` **eller** en gemt
  `adoWaitingStates`), `[data-testid="ado-settings-error"]` og `[data-testid="ado-error"]`.
  `[data-testid="ado-states-empty"]` og `ado-work-item-type-row` er derimod målt allerede — den ene er
  standardtilstanden, den anden følger af de tre standardtyper.
- Importskærmen, som er **helt** umålt: `ado-not-configured` + `ado-settings-link` (standard),
  `ado-deadline-notice` + `ado-preview` (kræver gemt `adoBaseUrl`, `adoProject` **og** et token),
  `ado-import-error`, `ado-none-assigned`, `ado-showing`, `ado-nothing-to-select`, `ado-row`,
  `ado-type`, `ado-deadline`, `ado-no-deadline`, `ado-requester`, `ado-note`, `ado-waiting`,
  `ado-waiting-since`, `ado-excluded`, `ado-already-imported`, `ado-open-item`, `ado-open-error`,
  `ado-import`, `ado-receipt`.

**Rutehandlerens krop skal bære felterne, ellers er grenene tomme.** `**/api/ado/preview` skal svare
med rækker der har `workItemType` (ny mod Jira), `state`, `isWaiting`, `alreadyImported` og `url` — og
mindst fire varianter i rækkefølge, fordi grenene udelukker hinanden: en afvisning, en tom liste, en
liste hvor hver række er blokeret (`excluded: "ado.excludedWaiting"`), og en liste med én række der kan
importeres. `deadline` skal være **udfyldt på én række og fraværende på en anden** — det er den ene
gren ingen Jira-skærm har. `requester`, `note` og `waitingSince` skal stå på mindst én række og mangle
på en anden. Der er **ingen** `isDuty`.

**`ado-settings-error` nås uden en rutehandler:** fjern den sidste sagstype, og den rigtige backend
svarer `ado.workItemTypesRequired`. Samme for `alreadyImported` — importér, forhåndsvis igen.

Dokumentér **hvad abstraktionen ikke tålte**. Det er skivens formål, og det er den ene ting der ikke
kan læses ud af koden bagefter.

---

## Hvad der kan gå galt

**Feltnavnene.** Måling 0b er den eneste vej. Jiras `duedate` gav null i hver deadline uden at en test
faldt, fordi testen for en sag *uden* deadline bestod.

~~**Samlingsnavnets mellemrum.** `%20` gennem en strengbygget URL, en `HttpClient` og en falsk server
på loopback — tre steder det kan af-escapes.~~ — **målt i Task 3: ingen af de tre af-escaper en
sti.** `Uri` re-escaper et bogstaveligt mellemrum ved konstruktion, så både `UriBuilder` og en
interpolation af `Uri`-objektet giver 36 grønne. Mellemrummet ligger i `FakeAdo`s samlingsnavn *og* i
dets projektnavn alligevel, fordi de to escapes ad forskellige veje — samlingen er en **URL** brugeren
har indsat og er escaped i forvejen, projektet er et **navn** appen selv skal escape — og fordi
**dobbelt** escaping (`%2520`) er den ene fejl der kan måles. Se Task 3.

**Fem håndskrevne wire-fixtures uden compiler over sig**, og ADO gør dem til syv. Det er stedet en
fremtidig skive taber et felt; hullet er dokumenteret i `CLAUDE.md`, ikke lukket.

**Ingen test kalder den rigtige instans.** Målingerne i afsnit 10 er afskrevet fra 2026-08-20, ikke fra
en løbende kørsel. En serveropgradering ville vise sig i brug, ikke i `dotnet test`.
