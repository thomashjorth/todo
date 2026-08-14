# Skive 3 — indstillinger og lokalisering

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En indstillingsside hvor du vælger sprog og administrerer dine navne på retro-boardet, og en app der taler dansk eller engelsk — med systemets sprog som udgangspunkt.

**Architecture:** `Setting` er en nøgle/værdi-tabel, men API'et er **typet** (`{ language }`), så kontrakten ikke lækker lagringsformen. Angular skifter sprog i runtime med Transloco; sproget hentes før første rendering, så du aldrig ser dansk blinke forbi på vej til engelsk.

**Tech Stack:** `@jsverse/transloco` 8.4.0 · EF Core 10.0.11 · Angular 22 signals · Tailwind 4.3.3 · xunit.v3 · Playwright 1.62.0

**Verificeret 2026-08-14:** Transloco 8.4.0 kræver `@angular/core >= 16.0.0` og `rxjs >= 6.0.0` — Angular 22.1.3 opfylder begge, ingen peer-konflikt. Fallback hvis den alligevel driller: `@ngx-translate/core` 18.0.0, som kræver `@angular/core >= 18`.

## Hvorfor de to ting er slået sammen

Indstillingssiden alene ville kun flytte aliasredigeringen fra import-skærmen — ingen ny værdi. Og `Setting`-tabellen ville blive bygget uden en eneste indstilling at gemme. Sprogvalget er den første rigtige, så de to hører sammen.

## Beslutninger

| Emne | Valg |
| --- | --- |
| Lagring | `Setting` som nøgle/værdi. API'et er typet — kontrakten kender ikke tabellen. |
| Standardsprog | Systemets. Mangler indstillingen, udledes den af `navigator.language`. |
| Sprogskifte | Runtime via Transloco. Ingen reload, ingen genindlæsning af bundles. |
| Datoformat | `Intl.DateTimeFormat` med det aktive sprog. **Ikke** Angulars `DatePipe`. |
| Flertal | Eksplicitte `.one`/`.other`-nøgler. Ingen ICU-pakke. |
| Aliasendpoints | Bliver på `/api/retro/aliases`. Kun UI'et flytter. |
| API-fejl | `{ code, message }` — `code` er en oversættelsesnøgle, `message` engelsk fallback. |

**Angulars `LOCALE_ID` og `DatePipe` er udelukket.** `LOCALE_ID` bindes ved bootstrap og kan ikke skiftes i runtime, hvilket er hele pointen med en sprogvælger. `Intl.DateTimeFormat(lang, …)` tager sproget som argument, kræver ingen `registerLocaleData`, og virker allerede i WebView2.

**Dansk er kilden.** Appens tekster er skrevet på dansk; `en.json` er oversættelsen. Ved uenighed er den danske formulering den rigtige.

## Bevidst uden for skive 3

Tokens og URL'er på indstillingssiden — der er ingen kilder at konfigurere endnu, de kommer i skive 6 og 7. Sync-interval. Flere sprog end to. Oversættelse af selve retro-boardets indhold, som er brugerens egne data.

---

## Task 1: `Setting`-tabellen og indstillingsendpoints

**Files:**
- Create: `src/Todo.Core/Settings/Setting.cs`
- Create: `src/Todo.Core/Settings/SettingKeys.cs`
- Modify: `src/Todo.Core/Persistence/TodoDbContext.cs`
- Create: migration
- Modify: `contracts/openapi.yaml`
- Create: `src/Todo.Host/Endpoints/SettingsEndpoints.cs`
- Create: `tests/Todo.Api.Tests/SettingsEndpointsTests.cs`

**Step 1: Entiteten**

```csharp
namespace Todo.Core.Settings;

public class Setting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
```

`Key` er primærnøgle, `HasMaxLength(100)`; `Value` er required med `HasMaxLength(2000)`. Konstanterne bor i `SettingKeys` — `public const string Language = "language";` — så en tastefejl bliver en compilerfejl frem for en indstilling der stille aldrig gemmes.

**Step 2: Migration**

```bash
dotnet tool run dotnet-ef migrations add Settings --project src/Todo.Core --startup-project src/Todo.Host
```

Læs den igennem: én ny tabel, ingen eksisterende kolonner rørt.

**Step 3: Kontrakt**

```
GET /api/settings → 200 SettingsResponse
PUT /api/settings → 200 SettingsResponse, 400
```

`SettingsResponse` og `SettingsRequest` har begge ét felt: `language` (string, nullable, `additionalProperties: false`). `null` betyder "følg systemet" og er en gyldig værdi at gemme — det er ikke det samme som "engelsk".

Regenerér og se drift-testen fejle. Noter teksten.

**Step 4: Endpoints**

- `GET` returnerer `language` fra tabellen, eller `null` hvis nøglen ikke findes.
- `PUT` accepterer `"da"`, `"en"` eller `null`. Alt andet giver `400`. **Valider hvidlistet** — en fri streng i en sprogindstilling er en fejl der først viser sig som en tom app.
- `null` sletter nøglen frem for at gemme strengen `"null"`.

**Step 5: Tests**

`GET` uden indstilling giver `null`; `PUT "en"` kan læses tilbage; `PUT null` fjerner den igen; `PUT "klingon"` giver `400`; `PUT` to gange overskriver frem for at oprette en dublet.

**Step 6: Commit**

```bash
git add -A && git commit -m "✨ Add a settings table with a typed language endpoint"
```

---

## Task 2: Fejlkoder i API'et

I dag er en 400-krop en bar JSON-streng på engelsk. Frontend kan ikke oversætte den.

**Files:** `contracts/openapi.yaml`, `src/Todo.Host/Endpoints/*.cs`, tests.

**Step 1: Kontrakt**

```yaml
    ApiError:
      type: object
      additionalProperties: false
      required: [code, message]
      properties:
        code: { type: string }
        message: { type: string }
```

Alle `'400'`-svar peger nu på `ApiError`.

**Step 2: Koderne**

Stabile, punktopdelte nøgler der matcher oversættelsesfilerne: `retro.emptyExport`, `retro.missingContentColumn`, `task.titleRequired`, `task.titleTooLong`, `settings.unknownLanguage`. **En kode må aldrig ændres**, når den først er sluppet ud — den er en identitet, ikke en beskrivelse.

`message` bliver stående som den engelske tekst. Den er til logs og til nøgler frontend endnu ikke kender.

**Step 3: Tests**

Drift-testen fanger **ikke** det her — den sammenligner stier og metoder. Skriv derfor tests på selve svarformen, ligesom wire-format-testen fra skive 1: fremtving hver 400 og fastslå at `code` er den forventede streng. Mindst tre af dem.

**Step 4: Commit**

```bash
git add -A && git commit -m "✨ Give API errors a stable code the frontend can translate"
```

---

## Task 3: Transloco og oversættelsesfilerne

**Files:** `src/Todo.Web/package.json`, `app.config.ts`, `public/i18n/da.json`, `public/i18n/en.json`, `src/app/i18n/*`, tests.

**Step 1: Installér**

```bash
npm.cmd install @jsverse/transloco@8.4.0 --prefix src/Todo.Web
```

**Step 2: Opsætning**

`provideTransloco` i `app.config.ts` med `availableLangs: ['da', 'en']`, `reRenderOnLangChange: true` og en HTTP-loader mod `/i18n/{lang}.json`. Filerne lægges i `public/`, så Angular kopierer dem til `wwwroot` uden ekstra konfiguration.

**Step 3: Sprogudledning**

En ren funktion, ikke logik gemt i en komponent:

```ts
export function resolveLanguage(stored: string | null, system: string): 'da' | 'en' {
  if (stored === 'da' || stored === 'en') return stored;
  return system.toLowerCase().startsWith('da') ? 'da' : 'en';
}
```

Unit-test den: gemt værdi vinder over systemet; `"da-DK"` og `"DA"` giver dansk; `"en-GB"`, `"de-DE"` og en tom streng giver engelsk. Engelsk er fallback for alt ukendt, ikke dansk — en app man ikke kan læse, er værre end en på et fremmed sprog man kan.

**Step 4: Nøgleparitet — den vigtigste test i tasken**

En Vitest der indlæser begge JSON-filer og fastslår, at nøglesættene er **identiske** — ikke bare at `en` har alle `da`'s nøgler, men også omvendt. Den skal sammenligne fladtrykte stier, så en nøgle der findes som objekt i den ene og streng i den anden også fanges.

Uden den test er den almindelige fejl at tilføje en nøgle ét sted, og så står der `tasks.sections.overdue` på skærmen i det andet sprog.

**Step 5: Commit**

```bash
git add -A && git commit -m "✨ Add Transloco with Danish and English translation files"
```

---

## Task 4: Træk strengene ud

**Files:** alle `*.html` under `src/Todo.Web/src/app`, plus `da.json`/`en.json`.

Hver brugervendt streng bliver til en nøgle. Nøglerne følger skærmen: `nav.*`, `tasks.*`, `retro.*`, `settings.*`, `errors.*`.

**Krav:**

- **Ingen dansk tekst tilbage i templates eller TypeScript.** Efter tasken skal en søgning efter `æ`, `ø` og `å` i `src/app/**/*.html` og `*.ts` kun ramme kommentarer og oversættelsesnøgler.
- Deadline-sektionernes overskrifter — Overskredet, I dag, Denne uge, Senere, Uden deadline — er nøgler, ikke en `bucketLabels`-record med danske strenge.
- **Flertal løses med `.one`/`.other`-nøgler.** `retro.skipped.one` = "Sprang 1 afstemningskort over.", `.other` = "Sprang {{count}} afstemningskort over." Samme for "Importér N opgaver", som i dag skriver "Importér 1 opgaver" og er grammatisk forkert. En lille ren `plural(count, key)`-hjælper vælger; unit-test den med 0, 1 og 2.
- **API-fejl slås op på `code`** med `message` som fallback, når nøglen ikke findes. Test begge veje.
- `aria-label` og `title` skal også oversættes. De er lige så brugervendte som synlig tekst, og de er dem man glemmer.

Eksisterende Vitest-tests der matcher på dansk tekst, skal opdateres til at matche på den oversatte tekst — **ikke** svækkes til at matche på nøglen. En test der fastslår `tasks.sections.today` beviser ingenting om, hvad brugeren ser.

```bash
git add -A && git commit -m "✨ Move every user-facing string into translation files"
```

---

## Task 5: Indstillingssiden

**Files:** `src/app/settings/*`, `app.routes.ts`, `app.html`, `retro-import.html`, `app.config.ts`.

**Step 1: Ruten og navigationen**

Tredje rute `'settings'`, tredje navigationslink `data-testid="nav-settings"`. Ved 465 px er tre tekstlinks stadig muligt; bliver det trangt, så forkort teksten frem for at indføre en menu.

**Step 2: Sproget hentes før første rendering**

`provideAppInitializer` henter `/api/settings`, kalder `resolveLanguage` med `navigator.language`, og sætter Transloco's aktive sprog **før** appen renderes. Ellers ser du dansk blinke forbi på vej til engelsk hver gang du starter.

Fejler kaldet, så fald tilbage til systemsproget og lad appen starte. En app der ikke kan starte, fordi indstillingerne ikke kunne hentes, er værre end en app på det forkerte sprog.

**Step 3: Siden**

- Sprogvælger med tre valg: Systemets sprog, Dansk, English. Ikke to — "følg systemet" er en tilstand for sig, og den er standarden.
- Skift virker **med det samme**, uden reload.
- Aliasredigeringen flyttes hertil fra import-skærmen, med samme test-id'er (`alias-input`, `alias-row`, `remove-alias`), så E2E-skærmobjektet kan flytte med i stedet for at blive skrevet om.
- Import-skærmen beholder et link til indstillinger i stedet for redigeringen. Fjern `retro-alias-section` helt — efterlad ikke markup der peger på noget, der ikke findes.

**Step 4: `RetroImportScreen.AddAliasAsync` flytter**

Metoden og aliaslokatorerne flytter til et nyt `SettingsScreen`-objekt i `tests/Todo.E2E`, og `TodoApp` får `GoToSettings()` med sin egen ventetid — samme mønster som `GoToImport()`. Rejsetesten fra skive 2 skal navigere til indstillinger og tilbage; det er præcis den situation `TodoApp` blev bygget til.

```bash
git add -A && git commit -m "✨ Add a settings page with language and board aliases"
```

---

## Task 6: Datoer på det rigtige sprog

**Files:** `src/app/i18n/format-date.ts` (eller en pipe), `task-list.html`, `retro-import.html`, tests.

Deadlines vises i dag som den rå `"2026-08-13"`. Med sproget på plads skal de formateres — men **datoen må stadig aldrig rejse gennem en `Date`-konstruktion, der kan flytte den en dag**. Del strengen på `-` og byg datoen eksplicit:

```ts
const [y, m, d] = value.split('-').map(Number);
return new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short', year: 'numeric' })
  .format(new Date(y, m - 1, d));
```

`new Date(y, m - 1, d)` er lokal midnat og bliver aldrig fortolket som UTC — i modsætning til `new Date("2026-08-13")`, som er præcis den fejl hele skive 1 blev designet til at undgå.

Unit-test at `"2026-08-13"` giver noget dansk for `da` og noget engelsk for `en`, og — vigtigst — at **dagen er 13 i begge**. Test også nytårsaften og 1. januar, hvor en tidszoneforskydning ville flytte året.

```bash
git add -A && git commit -m "✨ Format deadlines in the active language"
```

---

## Task 7: E2E og dokumentation

**Files:** `tests/Todo.E2E/SettingsJourneyTests.cs`, `README.md`, designdokumentet.

**Rejsen**, ved 480 × 1000 mod en tom database:

1. Opret en opgave med deadline i dag via en builder.
2. Åbn appen — sproget følger systemet.
3. Gå til indstillinger, vælg **English** → sektionsoverskriften skifter til "Today" **uden reload**, og deadline skifter format.
4. Gå til opgavelisten og tilbage → engelsk holder.
5. Genstart hosten mod samme database og åbn igen → stadig engelsk. Det beviser, at valget blev gemt og ikke bare lever i hukommelsen.
6. Vælg **Systemets sprog** → tilbage til dansk.
7. `document.documentElement.scrollWidth <= 480`.

**Se den fejle:** få `PUT /api/settings` til ikke at gemme, og bekræft at rejsen fejler på skridt 5 — ikke tidligere. Består den alligevel, tester skridt 5 ikke det, det påstår.

Husk at `GetByRole(Name:)` matcher på delstreng uden `Exact = true`.

**Dokumentation:** README får et afsnit om sprog og indstillinger. Designdokumentet får skive 3 markeret som færdig og de sammenlagte skiver rettet i leveranceplanen.

```bash
git add -A && git commit -m "✅ Cover language switching end to end"
```

---

## Færdig når

- Du kan skifte sprog i indstillinger, og appen skifter med det samme og husker valget.
- "Systemets sprog" er standarden og kan vælges igen.
- Ingen dansk streng er tilbage i templates eller TypeScript.
- `da.json` og `en.json` har identiske nøglesæt, håndhævet af en test.
- Deadlines vises på det aktive sprog og med den rigtige dag i begge.
- API-fejl har en kode frontend kan oversætte.
- `dotnet test Todo.sln` og Vitest er grønne, 0 advarsler.
- Drift-testen er set fejle i task 1, og lagringen af sproget i task 7.

## Til skive 4 (tilgængelighed, tastatur og dark mode)

- Skallen mangler stadig en baggrundsfarve: `<body>` sætter hverken `bg-*` eller `text-*`, så komponenternes `dark:`-farver ville stå på hvid. Det er det første, den skive skal rette.
- `text-gray-400` på health-linjen er en kendt kontrastovertrædelse (~2,9:1).
- Genvejsoverlayet skal vise **oversatte** genvejsnavne, så nøglerne fra denne skive er en forudsætning.
- Sprogvælgeren er den første rigtige formularkontrol i appen og bliver en god prøveklud for tastaturnavigation og `aria`-mærkning.
