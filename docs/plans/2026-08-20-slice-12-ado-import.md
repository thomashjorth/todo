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

**Ikke målt, og det er skivens første opgave.** Alt ovenfor kom af mentions-målingen, som handlede om
`System.History`. Tildelte work items er en anden query og andre felter:

- Virker `[System.AssignedTo] = @Me`?
- Hvordan hentes felterne for de fundne id'er — `workitemsbatch`, eller `?ids=`?
- **Findes der et deadline-felt i deres procesmodel, og hvad heder det?**
  `Microsoft.VSTS.Scheduling.DueDate` findes i nogle skabeloner og ikke i andre.
- Hvilke `System.State`-værdier bruger projektet? Det er modstykket til Jiras statusliste.

**Skriv ingen feltnavne i kode, før Måling 0 er kørt.** Skive 11 lærte det på den hårde måde: Jira
staver `duedate` i ét ord, camelCase-politikken ledte efter `dueDate`, og **hver deadline ankom som
null** uden at nogen test faldt — fordi der fandtes en test for en sag *uden* deadline.

---

## Måling 0: kun brugeren kan køre den

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

**0b — felterne på én sag.** Tag et id fra 0a:

```powershell
curl.exe -s -H $h[0] "$col/_apis/wit/workItems/DIT-ID?api-version=7.1" | ConvertFrom-Json | Select-Object -ExpandProperty fields | ConvertTo-Json -Depth 3
```

**Det er den vigtigste af de fire.** Den viser de faktiske feltnavne — titel, opgavestiller, tilstand
og om der er en deadline overhovedet.

**0c — batch-hentning**, fordi et kald pr. sag er dyrt:

```powershell
curl.exe -s -w "`nHTTP %{http_code}`n" -H $h[0] "$col/_apis/wit/workitems?ids=ID1,ID2&fields=System.Id,System.Title,System.State&api-version=7.1"
```

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
