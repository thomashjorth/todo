# Skive 4 — markdown i noter

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Noten på en opgave skrives i markdown og vises renderet. Et klik skifter til redigering, og du er tilbage i visning, så snart du forlader feltet.

**Architecture:** Markdown → HTML er en ren funktion i Angular, uden database eller HTTP. Saneringen sker i Angulars `[innerHTML]`, ikke i en pakke vi selv vedligeholder. Links kan ikke navigere i et Photino-vindue, så de sendes til et endpoint, der åbner systemets browser.

**Tech Stack:** `marked` 18.0.9 · `@tailwindcss/typography` 0.5.20 · Angular 22 signals · Tailwind 4.3.3 · xunit.v3 · Playwright 1.62.0

**Verificeret 2026-08-14:** `marked` 18.0.9 og `@tailwindcss/typography` 0.5.20 findes på npm.

## Beslutninger

| Emne | Valg |
| --- | --- |
| Omfang | Fuld CommonMark. Tekst kopieret ind fra Jira må ikke blive forvansket. |
| Styling | `@tailwindcss/typography` — `prose prose-sm dark:prose-invert`. |
| Bred indhold | Tabeller og kodeblokke scroller **inde i sig selv**, aldrig på siden. |
| Sanering | Angulars `[innerHTML]`. **Ingen `dompurify`.** |
| Links | Åbnes i systemets browser gennem et endpoint. |
| Skift til redigering | Klik på teksten, plus en rigtig knap for tastatur. |

**`@tailwindcss/typography` er den eneste vej udenom håndskrevet CSS.** Man kan ikke sætte utility-klasser på HTML, som en renderer selv genererer, og reglen i dette projekt er, at der ikke skrives CSS. Pluginnet giver én klasse på beholderen.

**Saneringen er ikke teoretisk.** Noter kommer allerede fra retro-import og senere fra Jira og ADO — altså tekst andre har skrevet, som renderes som HTML i dit vindue. Angulars `DomSanitizer` fjerner `<script>`, `on*`-attributter og `javascript:`-URL'er ved binding. `bypassSecurityTrustHtml` må aldrig bruges her.

## Bevidst uden for skive 4

Markdown i titler — de er og bliver almindelig tekst. En værktøjslinje med fed/kursiv-knapper. Preview side om side. Markdown i underopgaver.

---

## Task 1: Markdown til HTML som ren funktion

**Files:**
- Modify: `src/Todo.Web/package.json`
- Create: `src/Todo.Web/src/app/markdown/render-markdown.ts`
- Create: `src/Todo.Web/src/app/markdown/render-markdown.spec.ts`

**Step 1: Installér**

```bash
npm.cmd install marked@18.0.9 --prefix src/Todo.Web
```

**Step 2: Funktionen**

```ts
import { marked } from 'marked';

export function renderMarkdown(source: string | null | undefined): string {
  if (!source?.trim()) {
    return '';
  }

  return marked.parse(source, { async: false, gfm: true, breaks: true });
}
```

`async: false` er nødvendig for at få en `string` frem for `string | Promise<string>`. `breaks: true` gør et enkelt linjeskift til `<br>` — i en note skriver man linjer, ikke afsnit, og uden den forsvinder halvdelen af strukturen.

**Step 3: Tests**

- Fed, kursiv, punktliste, nummerliste og afkrydsningsliste giver de forventede elementer.
- Et link bliver til `<a href="...">`.
- En kodeblok bliver til `<pre><code>`.
- En tabel bliver til `<table>`.
- `null`, `undefined`, `""` og `"   "` giver `""` — ikke `"<p></p>"`.
- **Et `<script>` i kilden ender i output'et.** Det er med vilje: funktionen sanerer ikke, det gør Angular ved binding. Testen dokumenterer arbejdsdelingen, så ingen senere tror, funktionen er et sikkerhedslag.

**Step 4: Commit**

```bash
git add -A && git commit -m "✨ Render markdown to HTML as a pure function"
```

---

## Task 2: Noten vises renderet

**Files:**
- Modify: `src/Todo.Web/package.json`, `src/styles.css`
- Modify: `src/app/tasks/task-list.html`, `task-list.ts`
- Modify: `da.json`, `en.json`

**Step 1: Typography-pluginnet**

```bash
npm.cmd install -D @tailwindcss/typography@0.5.20 --prefix src/Todo.Web
```

I `src/Todo.Web/src/styles.css`, efter `@import "tailwindcss";`:

```css
@plugin "@tailwindcss/typography";
```

Det er Tailwind 4's måde at indlæse et plugin. **Verificér at det virker**, før du bygger videre: byg og bekræft at den genererede CSS indeholder `.prose`.

**Step 2: Vis noten**

I den udfoldede række vises noten renderet i stedet for direkte i en `<textarea>`:

```html
<div
  class="prose prose-sm dark:prose-invert max-w-none [&_table]:block [&_table]:overflow-x-auto [&_pre]:overflow-x-auto"
  [innerHTML]="rendered(task)"
  data-testid="note-rendered"></div>
```

- `max-w-none` er nødvendig: `prose` sætter en læsebredde, der er smallere end vores spalte.
- `[&_table]:block [&_table]:overflow-x-auto` og `[&_pre]:overflow-x-auto` er dét, der holder bred indhold inde i sin egen boks. **Uden dem fejler E2E-testen, der forbyder vandret scroll på siden** — og det er den rigtige måde at fejle på.
- `dark:prose-invert` skal med fra start; skive 6 auditerer, den skal ikke omskrive.

**Step 3: Tom note**

En tom note har ingen flade at klikke på. Vis en dæmpet pladsholder — nøgle `tasks.noteEmpty`, fx "Tilføj en note" — så der er noget at ramme. Den skal kunne skelnes fra en note, der faktisk siger "Tilføj en note"; brug `italic` og en dæmpet farve, ikke en der fejler kontrastkravet.

**Step 4: Verificér**

Byg, kør begge testsuiter, og driv siden med Playwright: opret en opgave med en note der indeholder fed tekst, en liste, en tabel og en kodeblok. Læs DOM'en og bekræft at elementerne er der, og at `document.documentElement.scrollWidth` stadig er 480.

```bash
git add -A && git commit -m "✨ Show the note as rendered markdown"
```

---

## Task 3: Klik for at redigere

**Files:** `task-list.html`, `task-list.ts`, `da.json`, `en.json`

To veje ind i redigering, og det er ikke overflod:

- **Klik på den renderede note.** Hurtigt, og det er sådan man forventer det.
- **En rigtig knap** ved siden af, med `aria-label` fra en nøgle. Tastaturbrugere kan ikke klikke, og den renderede note kan **ikke** gøres til en `<button>` — den indeholder links, og interaktivt indhold i en knap er ugyldig HTML og ødelægger tastaturnavigation.

Ud af redigering: `blur` gemmer og skifter tilbage, som i dag. `Escape` gør det samme. Der er ingen Gem-knap.

- Et klik på et **link** inde i noten må ikke åbne redigering. Tjek `event.target.closest('a')` og lad være.
- `editingNote = signal<string | null>(null)` i komponenten — det er visningstilstand, ikke data, præcis som `expandedId`.
- Når redigering åbnes, sættes fokus i `<textarea>`, ellers skal man klikke to gange.
- Test-id'er: `note-rendered`, `note-edit`, `note-editor`.

Vitest skal dække: klik på den renderede note åbner editoren; klik på et link i noten gør ikke; Escape lukker og gemmer.

```bash
git add -A && git commit -m "✨ Edit the note by clicking it"
```

---

## Task 4: Links åbnes i systemets browser

Et almindeligt link ville navigere hele Photino-vinduet væk til et website — uden adresselinje, uden tilbage-knap. Appen ville være væk, indtil den blev genstartet.

**Files:** `contracts/openapi.yaml`, `src/Todo.Host/Endpoints/SystemEndpoints.cs`, `src/app/markdown/*`, tests.

**Step 1: Kontrakt**

```
POST /api/system/open-link → 204, 400
```

`OpenLinkRequest` med ét felt: `url` (string, required). Tag `System`.

Regenerér og se drift-testen fejle.

**Step 2: Endpointet**

```csharp
Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
```

**Kun `http` og `https` må slippe igennem.** Alt andet giver `400` med koden `system.unsupportedScheme`. Det er ikke pedanteri: `UseShellExecute` beder Windows om at åbne hvad som helst med dets standardprogram, og en note fra et retro-board eller et Jira-issue er tekst, en anden har skrevet. Uden hvidlisten er det en vej til at starte programmer.

Valider med `Uri.TryCreate` og `uri.Scheme`, ikke med et regulært udtryk på strengen.

**Step 3: Frontend**

En klikhåndtering på den renderede note: find `event.target.closest('a')`, kald `preventDefault()`, og send URL'en til endpointet gennem en store — komponenten kalder ikke klienten direkte.

**Step 4: Tests**

API: `http` og `https` giver `204`; `file:`, `javascript:` og en tom streng giver `400` med koden. **Kald ikke `Process.Start` i testene** — læg det bag et lille interface, så testen kan fastslå, at den validerede URL nåede frem, uden at der åbnes noget på maskinen.

Vitest: et klik på et link sender URL'en og forhindrer standardhandlingen.

```bash
git add -A && git commit -m "✨ Open links from a note in the system browser"
```

---

## Task 5: E2E og dokumentation

**Files:** `tests/Todo.E2E/MarkdownNoteJourneyTests.cs`, `README.md`, designdokumentet.

**Rejsen**, 480 × 1000, mod en tom database, med en opgave arrangeret via `TaskItemBuilder`:

1. Fold rækken ud → noten vises renderet, med fed tekst, en liste og en tabel som rigtige elementer.
2. `document.documentElement.scrollWidth <= 480`, **også med tabellen på skærmen**. Det er hele grunden til at tabellen scroller inde i sig selv.
3. Klik på noten → editoren åbner med den rå markdown i.
4. Ret teksten, tryk Escape → visningen er tilbage og viser det nye.
5. Klik på et link i noten → **testen opsnapper kaldet** til `/api/system/open-link` med `page.RouteAsync` og fastslår URL'en, og afbryder det. Uden opsnapningen ville testkørslen åbne en rigtig browser på maskinen.

**Se den fejle:** fjern `[&_table]:overflow-x-auto` og bekræft at rejsen fejler på skridt 2. Det beviser, at kravet om ingen vandret scroll faktisk håndhæves og ikke bare står i en plan.

**Dokumentation:** README får et kort afsnit om, at noter er markdown, og at links åbner udenfor. Designdokumentet får skive 4 markeret som færdig.

```bash
git add -A && git commit -m "✅ Cover markdown notes end to end"
```

---

## Færdig når

- En note vises renderet og redigeres med ét klik.
- Tabeller og kodeblokke scroller inde i sig selv; siden gør aldrig.
- Links åbner i systemets browser, og kun `http`/`https` slipper igennem.
- Et `<script>` i en note bliver ikke til et script i DOM'en.
- `dotnet test Todo.sln` og Vitest er grønne, 0 advarsler.
- Ingen CSS- eller SCSS-regler er skrevet. `@plugin` i `styles.css` tæller ikke — det er Tailwinds egen indlæsningsmekanisme.

## Til skive 5 (Venter på og Someday)

- `TodoStatus` udvides med to værdier. `<select>` i den udfoldede række vokser med dem, og oversættelsesnøglerne følger mønsteret `tasks.statuses.${status}`.
- `WaitingOn` og `WaitingSince` er nye kolonner og dermed en migrering.

## Til skive 6 (tilgængelighed)

- Renderet markdown indfører overskrifter, tabeller, citater og kodeblokke, som alle skal kontrasttjekkes i begge temaer. `prose-invert` gør arbejdet, men det skal efterprøves, ikke antages.
- Den renderede note er klikbar uden at være en knap. Knappen ved siden af er tastaturvejen; kontrollér at den er i fokusrækkefølgen på et fornuftigt sted.
