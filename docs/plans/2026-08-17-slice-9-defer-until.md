# Skive 9 — `DeferUntil`, en startdato

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En opgave kan lægges væk til en dato og komme tilbage af sig selv, uden at den skal parkeres i Måske og uden at den skal have en falsk deadline for at blive synlig.

**Architecture:** Udskudthed **beregnes**, den gemmes ikke. `DeadlineBuckets.For` får `deferUntil` med og svarer `Deferred`, når datoen ligger ude i fremtiden — præcis som `bucket` og `waitingDays` i dag beregnes på serveren fra `clock.Today`. Frontendens sektionsmaskineri renderer en ny bucket uden at vide noget nyt. Derfor er der ingen statusovergang, ingen baggrundsjob og intet der skal køre ved midnat: opgaven dukker op, fordi klokken i morgen siger noget andet.

**Tech Stack:** EF Core 10.0.11 · ASP.NET Core 10 minimal APIs · NSwag · Angular 22 signals · xunit.v3 · Playwright 1.62.0

## Hvorfor

Designdokumentets afsnit 11 navngiver problemet: **deadline er den eneste organiserende akse**, "Uden deadline" bliver en skraldespand, og *"fristelsen til at sætte falske deadliner for at holde noget synligt er reel — hvilket underminerer de ægte deadliner."*

Det er ikke bare en risiko, det er mekanismen der nedbryder listen. Sætter man en deadline for at holde noget oppe i synsfeltet, betyder ingen deadline noget bagefter.

I dag har noget der **ikke er aktuelt endnu** kun to steder at være:

- **synligt**, og så støjer det i en deadline-sektion hvor det ikke hører til, eller
- **i Måske**, og så er det ude af syne — men Måske betyder udtrykkeligt *"ikke en forpligtelse"*, og det her er en forpligtelse. Bare ikke nu.

GTD kalder det en *tickler*. Det er den billigste ægte GTD-forbedring der findes i appen, og den rører **ikke** hovedaksen — den tager presset af den. En egentlig kontekstakse ville omgøre designdokumentets afsnit 2; den her gør ikke.

## Hvad målingen viste

Målt 2026-08-17. Aftrykket er lille, fordi mønsteret allerede findes.

### Beregnede signaler har allerede et sted at bo

`DeadlineBuckets.For(DateOnly? deadline, DateOnly today)` i `src/Todo.Core/Tasks/DeadlineBuckets.cs` er en **ren funktion** uden afhængigheder. Den er det naturlige sted, og den er triviel at teste.

`TaskEndpoints.ToContract(task, today)` beregner både `Bucket` og `WaitingDays` fra `clock.Today` og lægger dem på kontrakten. **Serveren ejer altså allerede afledte signaler** — det er den samme begrundelse skive 5 brugte til `waitingDays`: `IClock` findes på serveren og kan testes der.

### Sektionerne kommer gratis

`task-store.ts` har:

```ts
const bucketOrder: readonly DeadlineBucket[] = [
  DeadlineBucket.Overdue, DeadlineBucket.Today, DeadlineBucket.ThisWeek,
  DeadlineBucket.Later, DeadlineBucket.NoDeadline,
];
```

og `sections` mapper over den og **filtrerer tomme sektioner væk** (`.filter(section => section.tasks.length > 0)`).

En ny bucket koster derfor: én enum-værdi, én gren i `DeadlineBuckets.For`, én linje i `bucketOrder`, og ét nøglepar. Sektionen dukker op af sig selv når den ikke er tom, og forsvinder igen. **Frontenden skal ikke lære noget nyt.**

### Listen filtrerer kun på status i dag

`GET /api/tasks` fjerner `Done` medmindre `includeCompleted`, og `Someday` medmindre `includeSomeday`. Ordningen er `Deadline == null`, så `Deadline`, så `CreatedAt`.

**Udskudte opgaver skal ikke filtreres væk.** De skal i deres egen sektion, og det klarer bucket'en. Det betyder også, at de kan ses og redigeres uden en tredje kontakt — se afvejningen nedenfor.

### Migreringen er ufarlig, i modsætning til `long`-planens

`DeferUntil` er en **ny nullable kolonne**. Der er ingen ommapning, ingen primærnøgler og ingen fremmednøgler involveret, så SQLite kan tilføje kolonnen direkte og `dotnet-ef migrations add` må gerne generere den.

Det er værd at sige eksplicit, netop fordi `docs/plans/2026-08-17-long-ids.md` siger det stik modsatte om **sin** migrering: en TEXT-til-INTEGER-konvertering af en primærnøgle er en ommapning, og der ødelægger den genererede udgave data. **Forskellen er, om eksisterende værdier skal omskrives.** Her skal de ikke.

### Enum-værdier på ledningen er vagtet

`TaskEndpointsTests.Wire_format_uses_the_names_the_contract_declares` læser den rå JSON:

```csharp
Assert.Contains("\"status\":\"open\"", json);
Assert.Contains("\"bucket\":\"overdue\"", json);
```

`CLAUDE.md` advarer: *"Enum-værdier blev serialiseret forkert i en hel skive, før en sådan test blev skrevet."* Så en ny bucket-værdi **skal** have sin egen assertion der, ellers er den nye værdi udækket på præcis den måde der allerede er gået galt én gang.

### Nuværende sektionsnøgler

`overdue`, `today`, `thisWeek`, `later`, `noDeadline`, plus `waiting`, `completed`, `someday`. De ligger i `src/Todo.Web/public/i18n/da.json` og `en.json` — **ikke** i `src/app/i18n/`, som holder Transloco-kæden i TypeScript.

### Testtal at holde

**33 Todo.Core.Tests, 111 Todo.Api.Tests, 24 Todo.E2E, 139 Vitest.**

## Beslutninger

| Emne | Valg |
| --- | --- |
| Modellering | **En beregnet bucket**, ikke en ny status. |
| Feltet | `DeferUntil`, `DateOnly?` på `TaskItem` — samme type som `Deadline`. |
| Hvor det beregnes | `DeadlineBuckets.For(deadline, deferUntil, today)`. |
| Synlighed | Egen sektion, **sidst**. Ingen tredje kontakt. |
| Forrang | **`Overdue` slår `Deferred`.** Se nedenfor. |
| Ordning | Uændret. Ingen særordning for den nye sektion. |
| Migrering | Genereret af `dotnet-ef`. Ufarlig; ny nullable kolonne. |
| Redigering | Et `<input type="date">` i detaljepanelet, ved siden af deadline. |

**`Overdue` slår `Deferred`, og det er ikke vilkårligt.** En opgave kan have en fremtidig startdato *og* en overskredet deadline — man udskød den, og så løb tiden fra den alligevel. De to udsagn er i modstrid, og spørgsmålet er hvilken fejl der er værst. At **skjule en forpligtelse man har misset** er værre end at vise noget tidligere end planlagt. Så en overskredet deadline vinder.

Rækkefølgen i `DeadlineBuckets.For` skal derfor være: overskredet først, **derefter** udskudt, derefter resten.

**Ingen tredje kontakt, og det er en afvejning.** "Vis færdige" og "Vis måske" fylder allerede en linje i en 480 px-spalte, og en tredje ville wrappe. Men det betyder, at udskudte opgaver **kan ses** — det er en blødere tickler end GTD's, hvor de er helt ude af syne til datoen falder.

Det er med vilje, af to grunde. En sektion nederst man kan ignorere, er ærligere end en liste man har glemt findes; og **den ugentlige gennemgang skal alligevel kunne se dem** — det er hele pointen med en tickler. Bliver listen lang, er signalet at bruge Måske i stedet, som netop betyder "ikke en forpligtelse".

## Fælder i denne skive

- **En deadline må aldrig gennem `new Date(string)`.** Det parses som UTC-midnat og kan vise dagen før. `DeferUntil` er samme type og samme fælde. Brug `deadlineDate`-pipen, der findes fra skive 3.
- **`DateOnly`, ikke `DateTime`.** Tidsstempler er `DateTime` i UTC, men en startdato er en dato — som `Deadline`.
- **Ændrer du kontrakten, så kør `scripts\generate-api.ps1`**, ellers fejler friskheds-testen, der hasher `openapi.yaml`.
- **Drift-testen sammenligner kun stier og metoder.** Den fanger **ikke** et nyt felt eller en ny enum-værdi. Det gør wire-format-testen, og kun hvis du tilføjer assertionen.
- **`strictTemplates` er slået til.** Et nyt felt på `TodoTask` er `DateOnly?` i C# og bliver `string | undefined` i TypeScript. `@if` indsnævrer **ikke** et signal-kald — bind med `@let` først, og brug **ikke** `as`, som binder på sandhed.
- **`TaskListScreen.RowTitled` matcher rækkeknappens fulde tilgængelige navn.** Lægger du en startdato-linje ind i knappen, holder den op med at matche, og fejlen ligner en manglende række. Skal datoen vises på rækken, så læg den **uden for** knappen, som "venter på"-linjen ligger.
- **`ContrastTests` og `FocusTests` fra skive 7 dækker nye farver og nyt fokus.** Et nyt datofelt arver detaljepanelets klasser; bekræft det frem for at antage det.
- **Kør aldrig mod `%APPDATA%\TodoApp\todo.db`**, og dræb aldrig en `Todo.Host` du ikke selv har startet — find din på porten, ikke på navnet.
- **Builders er til arrange.** `TaskItemBuilder` skal have en `DeferredUntil(...)`, men brug den aldrig til selve den handling en test skal verificere.

## Bevidst uden for skive 9

**Kontekster.** Det er den ændring der ville gøre appen ægte GTD, og den omgør designdokumentets afsnit 2 — *"Én liste sorteret efter deadline"*. Den hører i en revision af afsnit 2 med hovedvisningen designet først, ikke som en tilføjelse.

**Projekter med en næste handling**, og **den ugentlige gennemgang** med revisionsloggen under. Begge står i afsnit 11 og er større end denne.

**Gentagelse.** En startdato inviterer til "hver mandag", og det er en anden datamodel.

**Omnummerering ud over den nødvendige.** Se Task 5.

---

## Task 1: Kontrakten

**Files:**
- Modify: `contracts/openapi.yaml`
- Regenerate: `src/Todo.Contracts/Generated/`, `src/Todo.Web/src/app/api/todo-client.ts`

**Step 1: Feltet og enum-værdien**

Læs kontrakten først og følg dens stil. Tre ændringer:

1. `TodoTask` får `deferUntil: { type: string, format: date, nullable: true }` — samme form som `deadline`. Giv det en `description`, som nabofelterne har: kontrakten er det dokumentationssiden viser, og kun 4 af 15 operationer har prosa i dag, så der er ingen grund til at gøre det tal værre.
2. `CreateTodoTaskRequest` og `UpdateTodoTaskRequest` får samme felt.
3. `DeadlineBucket`-enumet får **`deferred`** som sidste værdi.

Rør intet andet.

**Step 2: Regenerér**

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

Forventet: `Contracts.g.cs` får `DeferUntil` og `Deferred`, og `todo-client.ts` tilsvarende. **Genereret kode committes** — det er repoets valg.

**Step 3: Se at kun det forventede ændrede sig**

```
git diff --stat
```

Forventet: kontrakten, de to genererede filer, og `.source-hash`. Arbejdskopien er CRLF, så kig efter linjeskift-støj før du committer.

Bygningen kan godt være grøn her: et nyt kontraktfelt uden en tilsvarende egenskab i kernen er ikke en fejl endnu.

**Step 4: Commit**

Besked: `📝 Læg en startdato og en udskudt bucket i kontrakten`

---

## Task 2: Kernen

**Files:**
- Modify: `src/Todo.Core/Tasks/TaskItem.cs`, `src/Todo.Core/Tasks/DeadlineBucket.cs`, `src/Todo.Core/Tasks/DeadlineBuckets.cs`
- Modify: `src/Todo.Host/Endpoints/TaskEndpoints.cs`
- Create: migration
- Modify: `tests/Todo.TestSupport/Builders/TaskItemBuilder.cs`
- Modify: `tests/Todo.Core.Tests/` — den fil der tester `DeadlineBuckets`
- Modify: `tests/Todo.Api.Tests/TaskEndpointsTests.cs`

**Step 1: Testene først**

`DeadlineBuckets.For` er en ren funktion, så den er billig at teste og der er ingen undskyldning for at skrive den før testene. Find den eksisterende testfil i `tests/Todo.Core.Tests` og følg dens stil. Dæk:

- startdato **i morgen** → `Deferred`
- startdato **i dag** → **ikke** `Deferred`; den er startet. Grænsetilfældet, og det der bliver forkert hvis nogen skriver `>=` i stedet for `>`.
- startdato **i går** → ikke `Deferred`
- **ingen** startdato → som i dag, uændret for alle fem eksisterende buckets
- startdato i morgen **og deadline i går** → **`Overdue`**, ikke `Deferred`. Forrangen, og den vigtigste af dem.
- startdato i morgen og deadline i morgen → `Deferred`

**Kør dem og se dem fejle**, før funktionen ændres. Rapportér fejlteksten.

**Step 2: Signaturen og grenen**

```csharp
public static DeadlineBucket For(DateOnly? deadline, DateOnly? deferUntil, DateOnly today)
```

Rækkefølgen inde i den er bærende: **overskredet før udskudt.** En overskredet forpligtelse må ikke skjules, fordi den også var udskudt — se Beslutninger.

Tilføj `Deferred` **sidst** i `DeadlineBucket`, så den serialiseres som `deferred` og lander nederst i `bucketOrder`.

**Step 3: Entiteten og migreringen**

`public DateOnly? DeferUntil { get; set; }` på `TaskItem`.

```
dotnet tool restore
dotnet tool run dotnet-ef migrations add DeferUntil --project src\Todo.Core --startup-project src\Todo.Host
```

**Læs den genererede migrering igennem.** Forventet: én `AddColumn<DateOnly>` med `nullable: true`, og en `DropColumn` i `Down`. Er der mere end det — særligt en tabelombygning — så **stop og rapportér**: så har EF set noget andet i modellen end en ny nullable kolonne.

`dotnet-ef`, aldrig `dotnet ef`: en global 7.0.16 ligger på maskinen og kan ikke læse en EF Core 10-model. Og kør fra repo-roden, ellers henter `dotnet tool restore` et andet repos værktøjer.

**Step 4: `ToContract` og builderen**

`ToContract` sender `DeferUntil` videre og kalder `DeadlineBuckets.For(task.Deadline, task.DeferUntil, today)`.

`TaskItemBuilder` får `DeferredUntil(DateOnly)` og en `DeferredUntilTomorrow()` hvis det læser bedre i testene — builderen har allerede `DueToday()` og `Overdue()` i samme stil, og den bruger `_clock`.

**Step 5: Wire-format**

Tilføj til `Wire_format_uses_the_names_the_contract_declares` en assertion på den nye værdi:

```csharp
Assert.Contains("\"bucket\":\"deferred\"", json);
```

Det kræver en udskudt opgave i den test — læs den først og udvid dens arrange frem for at skrive en ny test, hvis det passer i dens form.

**Se den fejle** ved midlertidigt at stave enum-værdien forkert i kontrakten eller kernen. Det er den fælde `CLAUDE.md` siger allerede har kostet en hel skive.

**Step 6: Kør suiten**

```
dotnet test Todo.sln
```

Forventet: Core og Api er vokset med de tests du skrev; E2E står på 24. Rapportér de faktiske tal.

**Step 7: Commit**

Besked: `✨ Beregn en udskudt bucket ud fra en startdato`

---

## Task 3: Endpoints

**Files:**
- Modify: `src/Todo.Host/Endpoints/TaskEndpoints.cs`
- Modify: `tests/Todo.Api.Tests/TaskEndpointsTests.cs`

**Step 1: Tag imod feltet**

`POST /api/tasks` og `PUT /api/tasks/{id}` skal læse `DeferUntil` fra requesten og gemme den. Læs hvordan `Deadline` håndteres i begge og gør det samme — inklusive hvordan "ryd feltet" udtrykkes, så en startdato kan fjernes igen.

**Der skal ingen validering til.** En startdato i fortiden er ikke en fejl; den betyder bare at opgaven er startet. Og en startdato efter deadline er lovlig — det er præcis det tilfælde forrangsreglen findes for. **Tilføj ikke en regel her uden at spørge først.**

**Step 2: Tests**

- oprettelse med startdato i morgen → svarer med `bucket: deferred`
- opdatering der **sætter** en startdato → bucket'en skifter
- opdatering der **rydder** startdatoen → bucket'en skifter tilbage
- oprettelse **uden** startdato → uændret opførsel

Den tredje er den der falder, hvis "ryd feltet" ikke kan udtrykkes. **Se den fejle først.**

**Step 3: Kør suiten og commit**

Besked: `✨ Lad en opgave få og miste sin startdato`

---

## Task 4: Frontenden

**Files:**
- Modify: `src/Todo.Web/src/app/tasks/task-store.ts`
- Modify: `src/Todo.Web/src/app/tasks/task-row.html`
- Modify: `src/Todo.Web/public/i18n/da.json`, `en.json`
- Modify: `src/Todo.Web/src/app/tasks/task-list.spec.ts` eller `task-store.spec.ts`

**Step 1: Sektionen**

`DeadlineBucket.Deferred` **sidst** i `bucketOrder` i `task-store.ts`. Det er hele ændringen — `sections` mapper over listen og filtrerer tomme væk, så sektionen kommer og går af sig selv.

**Step 2: Nøglerne**

`tasks.sections.deferred` i **begge** sprogfiler i `src/Todo.Web/public/i18n/`, ellers fejler paritetstesten. Dansk noget i retning af **"Udskudt"**, engelsk **"Deferred"**.

Og en label til datofeltet: `tasks.deferUntil` — dansk **"Startdato"**, engelsk **"Start date"**. *"Startdato"* siger hvad feltet gør; *"udskyd til"* siger hvad man bruger det til. Vælg det ene og vær konsistent med sektionsnavnet.

**Step 3: Feltet i detaljepanelet**

Ved siden af deadline-feltet i `task-row.html`. Kopiér deadline-feltets form — samme `<label>`, samme klasser, samme `(blur)`/`(keyup.enter)`-par — så farverne og fokusringen fra skive 7 følger med gratis.

**Bind ikke datoen gennem `new Date(string)`.** Og husk at `@if` ikke indsnævrer et signal-kald: skal du læse `task().deferUntil` inde i en `@if`, så bind med `@let` først, og brug ikke `as`.

**Rør ikke rækkeknappens indhold.** `TaskListScreen.RowTitled` matcher dens fulde tilgængelige navn.

**Step 4: En Vitest der beviser sektionen**

At sektionen findes, og at den ligger **sidst**. Læs `task-store.spec.ts`s eksisterende sektionstest — den fastslår rækkefølgen af buckets — og udvid den frem for at skrive en ny, hvis det passer.

**Se den fejle** ved at flytte `Deferred` op i `bucketOrder`.

**Step 5: Byg og kør**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
npm.cmd run test --prefix src\Todo.Web -- --watch=false
dotnet test tests/Todo.E2E/Todo.E2E.csproj
```

Forventet: Vitest vokset med dine tests, **24 E2E** uændret og grønne. `ContrastTests` måler det nye felt i begge farvetemaer — det arver detaljepanelets klasser, men **bekræft det**.

**Step 6: Formatér kun de filer du har rørt**, navngivet eksplicit. Aldrig hele repoet.

**Step 7: Commit**

Besked: `✨ Vis udskudte opgaver i deres egen sektion`

---

## Task 5: E2E, vagten og dokumentation

**Files:**
- Modify: `tests/Todo.E2E/` — en ny journey, eller en udvidelse af en eksisterende
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Rejsen**

Én E2E der viser det hele: en opgave med en startdato i morgen står i **Udskudt** og **ikke** i sin deadline-sektion; rydder man datoen, flytter den. Brug `FixedClock`, som `WaitingJourneyTests` gør — på en rigtig klokke ville en kørsel hen over midnat flytte grænsen.

Læs `WaitingJourneyTests` først: den har præcis denne form for skive 5, og `TaskListScreen` har allerede `Section(heading)` og `RowsIn(heading)`.

**Step 2: Se vagten fejle**

Fjern `Deferred` fra `bucketOrder`. Rejsen skal fejle, fordi sektionen ikke findes. **Rapportér fejlteksten**, og sæt tilbage.

**Step 3: Omnummereringen — og checklisten der manglede sidst**

Denne skive **er** en skive: den ændrer datamodellen og skærmen. Den tager nummer **9**, og det skubber **Jira-import til 10, ADO til 11, mentions til 12, baggrundssync til 13, livscyklus til 14, pakning til 15.**

Omnummereringen efter skive 6 kostede to commits og efterlod tre forældede henvisninger, som først et review fangede. Så gør det med en søgning, ikke med øjnene:

```
git grep -n "skive 9\|skive 10\|skive 11\|skive 12\|skive 13\|skive 14" -- CLAUDE.md docs
```

Gennemgå **hver** forekomst. Nogle er forældede fra tidligere omnummereringer og hører ikke til denne skive — `docs/plans/2026-08-13-slice-0-skeleton.md` har en henvisning til "skive 8" der handlede om `TreatWarningsAsErrors` og har været forkert siden skive 6. **Ret ikke gamle planer**; de er historiske dokumenter. Ret `CLAUDE.md`, `docs/HANDOFF.md` og designdokumentet, og **rapportér** resten.

**Step 4: Designdokumentet**

- Afsnit 9: indsæt skive 9 som `DeferUntil`, marker den **Færdig.**, og omnummerér 10–15.
- Afsnit 11: opdatér GTD-vurderingen. Punktet om at deadline er den eneste akse skal **ikke** slettes — kontekster mangler stadig — men det skal sige, at presset på deadline-feltet er lettet, og hvordan. Skriv også at Måske nu har en nabo der betyder noget andet: *ikke endnu* frem for *ikke en forpligtelse*.
- Afsnit 4, `TaskItem`: tilføj `DeferUntil` til feltlisten med en linje om at udskudthed beregnes og ikke gemmes.
- Afsnit 10: ét punkt om at `Overdue` slår `Deferred`, og hvorfor — at skjule en misset forpligtelse er værre end at vise noget tidligt.

**Step 5: `HANDOFF.md`**

Ny række i Færdigt-tabellen. Og under "Tilbage": `DeferUntil` var det ene punkt jeg pegede på som den billigste ægte GTD-forbedring — den er nu leveret, så skriv hvad der er tilbage af afsnit 11's liste. **`long` som id er stadig det eneste punkt der bliver dyrere af at vente**, og det har nu ligget over to leverancer.

**Step 6: `CLAUDE.md`**

- Under **Datoer**: at en startdato er `DateOnly` som `Deadline`, og at udskudthed **beregnes** af `DeadlineBuckets.For` frem for at være en status — så der ikke skal køre noget ved midnat.
- Under **Testdisciplin**, hvis wire-format-assertionen var nødvendig at tilføje i hånden: at en ny enum-værdi ikke er dækket af drift-testen og skal have sin egen assertion i wire-format-testen.
- **Testtal** opdateret til de tal du **målte**.

**Step 7: Commit**

Besked: `📝 Marker skive 9 færdig og omnummerér de eksterne kilder`

---

## Færdig når

- En opgave med en startdato i fremtiden står i **Udskudt** og forurener ingen deadline-sektion.
- En startdato **i dag** gør opgaven aktuel — grænsetilfældet er testet.
- **`Overdue` slår `Deferred`**, og det er testet.
- Startdatoen kan **fjernes** igen, og det er testet.
- **Vagterne er set fejle**: bucket-testene før funktionen fandtes, wire-format-assertionen på en forkert stavet enum-værdi, sektionstesten med `Deferred` flyttet, og E2E-rejsen med `Deferred` fjernet fra `bucketOrder`. Alle fire med fejltekst i rapporten.
- Migreringen er **én `AddColumn`** — ikke en tabelombygning.
- `ContrastTests` og `FocusTests` er grønne med det nye felt.
- Skive 10–15 er omnummereret, og **søgningen i Step 3 er kørt og rapporteret** — ikke kun de tre filer rettet.
- Afsnit 11 siger hvad der stadig mangler, ikke kun hvad der er kommet.

## Til næste gang

Efter denne står afsnit 11's to store huller tilbage: **projekter med en næste handling**, og **kontekster** — hvor det andet omgør afsnit 2 og bør designes som en revision, ikke som en tilføjelse. Den ugentlige gennemgang hviler på revisionsloggen, som stadig er uplaceret.

Og `long` som id, som nu er udskudt over to leverancer. Planen ligger færdig og målt i `docs/plans/2026-08-17-long-ids.md`; aftrykket vokser med hver skive der lægges imellem.
