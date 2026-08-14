# Skive 5 — Venter på og Someday/Maybe

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En opgave kan ligge hos en anden eller være parkeret, uden at forurene listen over det, du faktisk kan handle på — og du kan se, hvor længe du har ventet.

**Architecture:** To nye værdier i `TodoStatus` og to nye kolonner. Statussen er allerede gemt som tekst, så enum-værdierne kræver ingen skemaændring; kun `WaitingOn` og `WaitingSince` gør. Antal dages ventetid udregnes på **serveren**, hvor `IClock` allerede findes og kan testes, frem for med datoregning i browseren.

**Tech Stack:** EF Core 10.0.11 · Angular 22 signals · Transloco · Tailwind 4.3.3 · xunit.v3 · Playwright 1.62.0

## Hvorfor

Fra GTD-vurderingen i designdokumentets afsnit 11: **"Venter på"** er en af de mest værdifulde lister i metoden, og den mangler helt. `Requester` er ikke den — den er hvem der bad **dig**; det her er hvem **du** venter på. De to peger hver sin vej, og at slå dem sammen ville gøre begge ubrugelige.

**Someday/Maybe** løser det, at det eneste sted at parkere en idé i dag er at slette den.

## Beslutninger

| Emne | Valg |
| --- | --- |
| Modellering | To nye `TodoStatus`-værdier, ikke en ny dimension. |
| Deadline-sektionerne | Begge tilstande forlader dem. Du kan ikke handle på dem. |
| Venter på | Egen sektion nederst, **altid synlig når den ikke er tom**. |
| Someday | Bag en kontakt, ligesom færdige. Skal netop ikke ses dagligt. |
| Ventetid | `waitingDays` udregnes på serveren fra `WaitingSince`. |
| Deadline på et ventende punkt | Vises i sektionen, men giver ikke plads i "I dag". |

**"Venter på" er altid synlig, Someday er det ikke.** Det er ikke inkonsekvens: en ventende opgave er en forpligtelse, du stadig har — du skal bare rykke en anden. En Someday-opgave er udtrykkeligt ikke en forpligtelse, og hele pointen er, at den ikke fylder.

**Ventetiden vises som antal dage, ikke som en dato.** "siden 3. aug." kræver, at du selv regner; "12 dage" er selve signalet. Serveren udregner det, så der ikke skal dato­regnes i frontend — samme begrundelse som `bucket`.

## To ting arvet fra skive 4, som skal med her

**`mailto:` skal tilføjes til hvidlisten i `SystemEndpoints`.** Marked laver en bar
e-mailadresse i en note om til et `mailto:`-link, og et klik giver i dag
*"Kun http- og https-links kan åbnes."* — en fejlmeddelelse for et fuldstændig
almindeligt notat. `mailto:` åbner brugerens mailprogram med modtageren udfyldt;
det er en velkendt web-scheme med lav risiko, og afvisningen er mere til gene end
til gavn. Tilføj den til hvidlisten, opdatér testen der fastslår at `mailto:`
afvises, og lad `file:` og `javascript:` blive afvist som nu.

**`TaskListScreen.RowTitled` matcher rækkeknappens fulde tilgængelige navn med
`Exact = true`.** Deadline, opgavestiller og underopgave-fremdrift er allerede
`<span>`s inde i den knap, så teksten indgår i navnet. Lægger du "venter på"-linjen
samme sted, holder `RowTitled` op med at matche for **hver** opgave i den tilstand —
og fejlen ser ud som en manglende række, ikke som en for lang etiket. Læg enten
teksten uden for knappen, eller giv `RowTitled` et mål der kun er titlen.

## Bevidst uden for skive 5

Projekter og kontekster — de øvrige GTD-huller, som står beskrevet i designdokumentets afsnit 11 og kræver en langt større omlægning. Automatisk påmindelse om gamle ventende punkter. En ugentlig gennemgang.

---

## Task 1: Skema

**Files:**
- Modify: `src/Todo.Core/Tasks/TodoStatus.cs`, `src/Todo.Core/Tasks/TaskItem.cs`
- Modify: `src/Todo.Core/Persistence/TodoDbContext.cs`
- Create: migration

**Step 1: Statusværdierne**

```csharp
public enum TodoStatus
{
    Open,
    InProgress,
    WaitingFor,
    Someday,
    Done,
}
```

**Indsæt dem før `Done`, ikke efter.** Rækkefølgen er læserækkefølgen i `<select>`, og `Done` hører sidst. Statussen gemmes som tekst via `HasConversion<string>()`, så en ny værdi kræver **ingen** skemaændring og kan ikke omdøbe eksisterende rækker — det er hele grunden til, at konverteringen blev valgt i skive 1.

**Step 2: To nye felter på `TaskItem`**

```csharp
    public string? WaitingOn { get; set; }

    public DateTime? WaitingSince { get; set; }
```

`WaitingOn` får `HasMaxLength(200)`. `WaitingSince` er `DateTime` i UTC som de øvrige tidsstempler — **ikke** `DateTimeOffset`, som SQLite ikke kan sortere korrekt.

**Step 3: Migration**

```bash
dotnet tool run dotnet-ef migrations add WaitingAndSomeday --project src/Todo.Core --startup-project src/Todo.Host
```

**Læs den og citér linjerne.** Bekræft: to `AddColumn`, ingen `DropColumn`, ingen ombygning af `Tasks`. Der ligger rigtige opgaver i brugerens database.

**Step 4: Verificér**

`dotnet test Todo.sln` skal være uændret grøn. Bekræft desuden mod en **kopi** af en rigtig database (`todo.db`, `-wal` og `-shm` sammen — `.db` alene er en tom header i WAL-tilstand) at migrationen kører og de eksisterende opgaver stadig kan hentes.

```bash
git add -A && git commit -m "🗃️ Add columns for who you are waiting on and since when"
```

---

## Task 2: Kontrakt og endpoints

**Step 1: Kontrakten**

- `TodoStatus` får `waitingFor` og `someday`.
- `TodoTask` får `waitingOn` (nullable), `waitingSince` (nullable `date-time`) og `waitingDays` (nullable integer).
- `UpdateTodoTaskRequest` får `waitingOn` (nullable).
- `listTasks` får en parameter mere: `includeSomeday` (boolean, default false).

To booleans på `listTasks` er lidt klodset, men alternativet — at lave dem om til en liste af tilstande — er en brydende ændring af en kontrakt, der virker, for en æstetisk gevinst.

Regenerér og **se drift-testen fejle**. Bemærk: den fejler kun på ændrede *stier og metoder*. Her ændres kun skemaer og en parameter, så **den fanger det måske ikke** — hvis den forbliver grøn, er det forventet og ikke et tegn på, at ændringen ikke virkede. Sig det tydeligt i rapporten.

**Step 2: Endpoints**

- `listTasks` udelader `Someday` med mindre `includeSomeday=true`, uafhængigt af `includeCompleted`. `WaitingFor` returneres **altid**.
- `updateTask` sætter `WaitingSince = clock.UtcNow` når statussen **skifter til** `WaitingFor`, og rydder både `WaitingSince` og `WaitingOn` når den skifter væk. En opdatering, der lader statussen blive på `WaitingFor`, må **ikke** nulstille `WaitingSince` — ellers viser listen altid nul dage, og det er præcis den fejl, der gør feltet værdiløst.
- `waitingDays` udregnes som hele dage mellem `WaitingSince` og `clock.Today`, og er `null` for alt andet end ventende opgaver. Samme dag giver `0`, ikke `null`.
- `waitingOn` gemmes kun som meningsfuldt felt; tom streng normaliseres til `null`.

**Step 3: Tests**

1. Skift til `waitingFor` sætter `waitingSince` og `waitingDays = 0`.
2. En opdatering der bevarer `waitingFor`, flytter **ikke** `waitingSince`.
3. Skift væk fra `waitingFor` rydder både `waitingSince` og `waitingOn`.
4. `someday` udelades som standard og er med ved `includeSomeday=true`.
5. `waitingFor` er med i svaret **uden** nogen parameter.
6. `includeCompleted` og `includeSomeday` virker uafhængigt — fire kombinationer.
7. `waitingDays` er `null` for en åben opgave.
8. En ventende opgave arrangeret med `WaitingSince` for 12 dage siden giver `waitingDays = 12`. Brug en fast klokke, ikke rigtig tid.

Punkt 8 kræver, at `IClock` kan udskiftes i en kørende host. `TodoHost.Build` har allerede en `configureServices`-parameter fra skive 4, og `RunningHost.StartWithAsync` tråder den igennem — brug den frem for at bygge en ny mekanisme.

```bash
git add -A && git commit -m "✨ Let a task wait on someone or be parked for someday"
```

---

## Task 3: Storen

**Files:** `src/app/tasks/task-store.ts` og dens spec.

- `sections` udelader nu **både** `done`, `waitingFor` og `someday`.
- Nye computeds: `waitingTasks` og `somedayTasks`.
- `showSomeday = signal(false)`, tråd ind i `listTasks` ved siden af `showCompleted`.
- `update()` skal kunne sende `waitingOn` med.

Vitest skal dække: en ventende opgave optræder ikke i nogen deadline-sektion; den optræder i `waitingTasks`; en Someday-opgave optræder kun i `somedayTasks`; `showSomeday` sendes med i forespørgslen.

```bash
git add -A && git commit -m "✨ Keep waiting and parked tasks out of the deadline sections"
```

---

## Task 4: Skærmen

**Files:** `task-list.ts`, `task-list.html`, `da.json`, `en.json`.

**Statusvælgeren** får de to nye værdier. Nøglerne følger mønsteret `tasks.statuses.${status}` — dansk "Venter på" og "Måske", engelsk "Waiting for" og "Someday".

**Feltet "venter på hvem"** vises i den udfoldede række **kun når statussen er `waitingFor`**. Ellers er det støj i en 465 px spalte. Rigtigt `<label>`, gemmes på blur som de øvrige felter.

**"Venter på"-sektionen** nederst, over færdige, altid synlig når den ikke er tom. Hver række viser titel, hvem du venter på, og hvor længe — `tasks.waitingDays.one` / `.other` med `pluralKey`. Har punktet en deadline, vises den også; er den overskredet, markeres den med samme røde accent som "Overskredet"-sektionen. Rækkerne skal kunne foldes ud som normale rækker — du skal kunne ændre status tilbage derfra.

**Someday-sektionen** nederst, bag en `data-testid="show-someday"`-kontakt ved siden af "Vis færdige".

Test-id'er: `waiting-section`, `waiting-days`, `waiting-on-input`, `someday-section`, `show-someday`.

**Krav der ikke er til forhandling:** kun standard Tailwind, ingen CSS. Hver ny `bg-*`/`text-*`/`border-*` skal have en `dark:`-modpart. Ingen `text-gray-400` på lys baggrund (~2,9:1). Siden må ikke scrolle vandret ved 480 px — tjek også på engelsk, hvor teksterne er længere.

```bash
git add -A && git commit -m "✨ Show what you are waiting on and what is parked"
```

---

## Task 5: E2E og dokumentation

**Rejsen** — `tests/Todo.E2E/WaitingJourneyTests.cs`, 480 × 1000, tom database, arrangeret med builders:

1. En opgave med deadline i dag står i "I dag".
2. Fold ud, sæt status til "Venter på", skriv et navn → rækken **forsvinder fra "I dag"** og dukker op i "Venter på"-sektionen med navnet og "0 dage".
3. En opgave arrangeret med `WaitingSince` for 12 dage siden viser "12 dage". Brug en fast klokke gennem `StartWithAsync`.
4. Sæt status tilbage til "Åben" → den er tilbage i "I dag", og navnet er ryddet.
5. Sæt en opgave til "Måske" → den forsvinder helt. Slå "Vis måske" til → den er der igen.
6. `document.documentElement.scrollWidth` er inden for `clientWidth`.

**Se den fejle:** lad `updateTask` bevare `waitingSince` når statussen skifter *væk* fra `waitingFor`, og bekræft at rejsen fejler på skridt 4 — ikke tidligere.

Husk at `GetByRole(Name:)` matcher på delstreng uden `Exact = true`.

**Dokumentation:** README får et kort afsnit om de to tilstande og hvad de er til. Designdokumentet får skive 5 markeret som færdig, og afsnit 11 opdateret, så det fremgår hvilke GTD-huller der nu er lukket, og hvilke der stadig står åbne.

```bash
git add -A && git commit -m "✅ Cover waiting and parked tasks end to end"
```

---

## Færdig når

- En opgave kan sættes til at vente på en person, og listen viser hvem og hvor længe.
- Ventende og parkerede opgaver optræder aldrig i deadline-sektionerne.
- "Venter på" er synlig uden at bede om det; "Måske" er ikke.
- Ventetiden nulstilles ikke, når du redigerer noget andet på et ventende punkt.
- `dotnet test Todo.sln` og Vitest er grønne, 0 advarsler.
- Ingen CSS- eller SCSS-regler er skrevet.

## Til skive 6 (tilgængelighed, tastatur og dark mode)

- To nye sektioner og et nyt inputfelt skal med i gennemgangen.
- `task-list.html` har stadig ingen `dark:`-varianter ud over det, skive 4 og 5 tilføjede. Hele filen skal tages.
- Statusvælgeren har nu fem valg og bliver den længste kontrol i appen — tjek den ved 465 px på engelsk.
