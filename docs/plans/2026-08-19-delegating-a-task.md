# Uddelegering af en opgave Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Vælger du "Venter på" på en opgave, bliver du spurgt hvem — med forslag fra en liste du
vedligeholder i indstillingerne. Og indstillingssiden bliver delt i fire klare grupper.

**Architecture:** Uddelegering er en **genvej til en tilstand der findes**: `WaitingFor` +
`WaitingOn`. Ingen ny status, intet nyt felt på `TaskItem`, **ingen migrering**. Det eneste nye i
backenden er én indstilling: `delegates`, en JSON-liste i `Setting`. Forslagene hænger på det
eksisterende `waitingOn`-tekstfelt som en `<datalist>`.

**Design og begrundelser:** `docs/plans/2026-08-19-delegating-a-task-design.md`. Læs den først —
særligt beslutning 4, som er den der værner om noget der virker i dag.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core 10 / SQLite, Angular 22 signal-stores, xunit.v3,
Playwright, Vitest.

**Testtal før planen:** Core **90**, Api **187**, E2E **34**, Vitest **186** — alle grønne på `main`
(`e6be619`).

---

## Det der ikke skal bygges

- **Ingen ny status og intet `Delegated`-felt.** `WaitingFor` + `WaitingOn` *er* uddelegeret.
- **Ingen migrering.** `delegates` er en nøgle i `Setting`-tabellen.
- **`TaskEndpoints` skal ikke ændres.** Målt: den sætter `WaitingSince = clock.UtcNow`, når status
  flytter **til** `WaitingFor`, og kun ved selve flytningen. Uddelegeringen får uret gratis.
- **Ingen mail, ingen besked, ingen tilbageskrivning til Jira.** En uddelegeret Jira-sag skifter
  **ikke** assignee i Jira, og det skal stå i UI'et — ellers tror man at man har handlet, hvor man
  kun har bogført.
- **`UserAlias` genbruges ikke.** Aliaserne betyder "hvad der er mit" i retro-importen.

---

## Task 1: Kontrakten

**Files:**
- Modify: `contracts/openapi.yaml`
- Generated: `src/Todo.Web/src/app/api/todo-client.ts`, `src/Todo.Contracts/Generated/Contracts.g.cs`, `.source-hash`
- Modify: `src/Todo.Web/src/app/settings/settings-store.spec.ts`, `settings.spec.ts` (se Step 3)

**Step 1: Feltet**

På **både** `SettingsResponse` og `SettingsRequest`:

```yaml
        delegates:
          type: array
          items:
            type: string
          description: >-
            People you hand tasks to, offered as suggestions when a task moves to WaitingFor. A
            suggestion list, not a closed set: the who field stays free text, because waiting on
            somebody unlisted — or on nobody at all — are both valid states.
```

**I `SettingsResponse`s `required`-liste**, ved siden af `jiraWaitingStatuses` og de øvrige. Skive
11 målte hvorfor: uden `required` genereres feltet som optionelt, og så skal Angular skrive `?? []`
overalt — og `@if` indsnævrer **ikke** et signal-kald. Med `required` får C#-DTO'en desuden en
`= new Collection<string>()`-initializer, så en handler ikke kan glemme at sætte den.

**Ikke** i `SettingsRequest`s `required`-liste: dens fulde-erstatnings-semantik afhænger af at hvert
felt kan udelades.

**Step 2: Generér**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
dotnet test tests\Todo.Api.Tests\Todo.Api.Tests.csproj --filter "FullyQualifiedName~GeneratedCodeFreshnessTests"
```

Scriptet skriver **tre** filer. `.source-hash` er den friskheds-testen læser; udelades den, er
commit'en grøn lokalt og rød hos alle andre.

`ContractDriftTests` skal **blive grøn** — der kommer ingen nye ruter, kun et felt.

**Step 3: Ret de håndskrevne fixtures — de fælder typetjekkeren**

Målt to gange i de forrige leverancer: et nyt **required** felt fælder
`Spec_project_passes_the_type_checker`, fordi spec-filerne har **håndskrevne** wire-former uden en
compiler over sig.

**To filer har en settings-form:** `settings-store.spec.ts` (`SettingsJson`) og `settings.spec.ts`
(`settingsJson`/`JiraFixture`). De er **uafhængige** — sidste leverance troede den ene dækkede begge
og tog fejl. Læg `delegates` i **begge**.

> **Rettet efter kørslen, 2026-08-19. Leveret i `6d37597`.** **Typetjekkeren fælder ingen af dem** —
> `Spec_project_passes_the_type_checker` bestod, **før** fixturene blev rørt.
>
> Reglen er rigtig: en form der føres ind i en **genereret type** fælder compileren; en der bruges som
> **rå svarkrop** til `flush(...)` gør ikke. Men **begge** settings-fixtures er af den anden slags —
> de bygger `new Blob([JSON.stringify({...})])`. Så spørgsmålet "hvilken fil falder" svarer
> **ingen**, og rettelsen var nødvendig **udelukkende** af den tavse grund: et `undefined` der lander
> i et `string[]`-signal i Task 3 og 4, hvor intet klager.
>
> Det er tredje gang i tre leverancer, at jeg har taget fejl om denne mekanik. Den rigtige model er:
> **det er `jira-store.spec.ts`' forhåndsvisningsrække der er compiler-synlig**, fordi den føres ind i
> `new JiraPreviewRow(...)`. Settings-fixturene er det ikke, og har aldrig været det.
>
> Tre fejl mere i denne task:
>
> - **"Vælg en form der matcher de øvrige flow-sekvenser" har ingen referent** — der findes **ingen**
>   ombrudt flow-sekvens i filen. Præcedensen er `HealthResponse`s **blok-sekvens**, og den blev fulgt.
>   Længste linje er uændret 102 (præeksisterende); mine 101 var altså ikke filens længste.
> - **Doc-kommentaren i `settings-store.spec.ts` sagde "five of them non-optional".** Med `delegates`
>   er det seks. Et plan-trin der kun lægger feltet til, efterlader en falsk kommentar.
> - **`settings.spec.ts`' override-interface hed `JiraFixture`** og var dokumenteret som "the Jira half
>   of the settings response" — og `delegates` er **emphatically ikke** Jira. Omdøbt til
>   `SettingsFixture`. Det er præcis den betydningssløring designets beslutning 3 advarer imod.
>
> **Og til Task 5:** hverken `ContrastTests` eller E2E stubber `/api/settings` — de kører mod den
> **rigtige** backend. Den nye gruppes farvegrene kræver derfor **rigtige gemte delegerede** via et
> `PUT` i rejsen, ikke en rutehandler.

**Step 4: Kør og commit**

```bash
dotnet test tests\Todo.Api.Tests\Todo.Api.Tests.csproj
npm.cmd run test --prefix src\Todo.Web -- --watch=false
git add contracts/openapi.yaml src/Todo.Web/src/app/api/ src/Todo.Contracts/Generated/ src/Todo.Web/src/app/settings/
git commit -m "📝 Læg de delegerede på kontrakten"
```

Forventet: Api **187** uændret, Vitest **186** uændret. Denne task tilføjer ingen tests.

---

## Task 2: Nøglen og den delte listehjælper

**Files:**
- Create: `src/Todo.Core/Settings/SettingList.cs`
- Modify: `src/Todo.Core/Settings/SettingKeys.cs`, `src/Todo.Core/Errors/ErrorCodes.cs`
- Modify: `src/Todo.Core/Jira/JiraSettingsReader.cs`
- Modify: `src/Todo.Host/Endpoints/SettingsEndpoints.cs`
- Test: `tests/Todo.Api.Tests/SettingsEndpointsTests.cs`, `tests/Todo.Core.Tests/Settings/SettingListTests.cs`

**Step 1: Udtræk listehjælperen først**

`JiraSettingsReader` har en **privat** `ReadList(string?)` der parser en JSON-liste og læser korrupt
JSON som tom. `delegates` er ikke en Jira-nøgle og skal læses af `SettingsEndpoints` — så logikken
ville komme til at findes to steder.

Dette repo har målt prisen for netop det **to gange i denne uge**: basisURL'en blev trimmet i to
lag, og da nogen endelig målte det, havde det ene lag aldrig kunnet fyre og **ingen vidste hvilket**.
Og fem håndskrevne kopier af serverens svarform er nu det sted en fremtidig skive taber et felt.
**Udtræk før du tilføjer.**

```csharp
namespace Todo.Core.Settings;

/// <summary>
/// A setting whose value is a list of strings, stored as one row of JSON. Extracted when the second
/// caller appeared: the parse had lived privately in JiraSettingsReader, and a copy in
/// SettingsEndpoints would have been the third place in this repo where the same rule existed twice.
/// </summary>
public static class SettingList
{
    /// <summary>
    /// A corrupt value reads as an empty list rather than throwing: unreadable settings must not stop
    /// the app from opening, and empty is the safe reading for every list this holds.
    /// </summary>
    public static IReadOnlyList<string> Read(string? json) { … }

    /// <summary>
    /// Null for an empty list, so the row is removed rather than stored as "[]". Slice 11 measured
    /// why that matters: two existing tests assert Assert.Empty/Assert.Single on the whole Settings
    /// table, and a leftover row makes them red.
    /// </summary>
    public static string? Write(IEnumerable<string?> values) { … }
}
```

`Write` trimmer, dropper blanke, og deduper **versalufølsomt** — samme regel som
`RetroEndpoints`' aliasliste. Lad `JiraSettingsReader` bruge `Read`, så der kun er ét sted.

Core-tests på begge: korrupt JSON, tom liste, blanke navne, dubletter der kun afviger i versalitet.

**Step 2: Nøglen og fejlkoden**

`SettingKeys`: `public const string Delegates = "delegates";`

`ErrorCodes`: `public const string SettingsDuplicateDelegate = "settings.duplicateDelegate";`

**En kode er en identitet**, ikke en beskrivelse — den må aldrig omdøbes efter den er sendt. Og
`ErrorCodeTranslationTests` kræver en `errors.<kode>`-nøgle i **begge** sprogfiler, så koden er ikke
færdig før Task 3 har oversat den. Kør vagten og se den fejle på den manglende nøgle; det er den
vagt der blev skrevet, fordi `jira.statusNameInvalid` stod uoversat i begge filer gennem tre commits.

**Step 3: Endpointet**

`ReadAllAsync` læser `SettingKeys.Delegates` med `SettingList.Read`. `PUT` skriver med
`SettingList.Write`, som giver `null` for en tom liste og dermed fjerner rækken.

Afvis en dublet med `SettingsDuplicateDelegate` — **eller** dedup i stilhed. Vælg **afvisning**, som
`RetroEndpoints` gør for aliaser: en liste hvor to navne blev til ét uden at nogen sagde det, er
værre end en fejl.

**Step 4: Tests, mutation, commit**

Tre Api-tests: rundtur, tom liste fjerner rækken, dublet afvises. Mutationer: **fjern trimningen**
(en test på `"  Flemming  "` skal fejle), og **byt afvisningen ud med stilfærdig dedup** (dublet-testen
skal fejle).

Forventet: Api **190** (187 + 3), Core **90 + dine `SettingList`-tests**.

```bash
git commit -m "✨ Gem de delegerede, med listelogikken samlet ét sted"
```

---

## Task 3: Indstillingssiden i fire grupper

**Files:**
- Modify: `src/Todo.Web/src/app/settings/settings.html`, `settings.ts`, `settings-store.ts`, og deres specs
- Modify: `src/Todo.Web/public/i18n/da.json`, `en.json` — **`public/`, ikke `src/`**

**Step 1: `SettingsStore`**

Et signal mere, `delegates`. **`save` skal bære det med i `current` — nu otte felter.** `PUT` er en
fuld erstatning der læser et fraværende felt som *ryd*; det er fælden der tabte en gemt `DeferUntil`
i skive 9 og som skive 11's egen store faldt i. **Udvid den eksisterende regressionstest** frem for
at lægge en ny ved siden af, så der ikke er to tests der påstår halvdelen hver.

**Step 2: Grupperne**

Målt: **sproget har hverken overskrift eller `<section>`** og ligger løst under sidetitlen, Jira er en
`<section>` med `<h3>`, og retro-aliaserne er en **bar `<h3>` uden section**. Tre grupper, tre
strukturer.

Fire ligestillede `<section>`-elementer, hver med en `<h3>` med **samme klasser**:

1. **Sprog** 2. **Uddelegering** 3. **Jira-import** 4. **Retro-import**

Rækkefølgen er dine egne indstillinger først, kilderne sidst.

**`<h4>`-niveauet inde i Jira-gruppen bliver** — de to statuslister er underafsnit, ikke grupper.
**Hver gruppe beholder sin egen fejllinje.** Og **ingen `data-testid` ændres**: hver E2E- og
Vitest-påstand hænger på dem, så en omdøbning ville se ud som en fejl i tests frem for i markup.

**Step 3: Uddelegeringsgruppen**

Listen redigeres som aliaserne — læs den blok i `settings.html` og følg den. Sig med ord, at
uddelegering **kun er bogføring**: ingen besked til den anden, og en Jira-sag skifter ikke assignee.

Styling: kun Tailwind, `dark:`-modpart til hver farve, dæmpet tekst er
`text-gray-500 dark:text-gray-400` (`dark:text-gray-500` fejler med 3,67:1 på `gray-900`), og hvert
felt har en `placeholder-*`-klasse — uden arves `currentColor` med ~54 % alfa, og farven ligger på
`::placeholder`, ikke på elementet.

**Step 4: Oversættelser**

Nye nøgler i **begge** filer, inklusive `errors.settings.duplicateDelegate`. Paritetstesten er
`src/Todo.Web/src/app/i18n/translations.spec.ts`, en **Vitest**-spec — og
`ErrorCodeTranslationTests` i Api er den der fanger en kode der mangler i **begge**.

**Step 5: Mutation**

Lad `save` udelade `delegates`. Forventet: regressionstesten fejler. Fjern en nøgle fra `en.json`:
paritetstesten fejler med nøglens navn. Fjern `errors.settings.duplicateDelegate` fra **begge**:
`ErrorCodeTranslationTests` fejler — paritetstesten gør **ikke**, og det er hele grunden til at den
vagt findes.

---

## Task 4: Vælgeren spørger, feltet foreslår

**Files:**
- Modify: `src/Todo.Web/src/app/tasks/task-row.html`, `task-row.ts`, `task-list.spec.ts`

**Step 1: Fokus**

Vælger man `WaitingFor` i statusvælgeren, får `waitingOn`-feltet **fokus**. Ikke en dialog, ikke en
ny skærm. Samme konvention som Alt-genvejene i skive 8: en handling flytter fokusringen, fordi
Windows gør det — og et programmatisk `click()` flytter **ikke** fokus, så `focus()` skal kaldes.

**Step 2: `<datalist>`**

Forslagene fra `SettingsStore.delegates` hænger på feltet med `list="…"`. Ét HTML-element,
tastaturtilgængeligt gratis. Appen bruger i forvejen native kontroller, og `<body>` har
`scheme-light-dark` netop for at de følger temaet.

**Prisen: `<datalist>`s popup kan `ContrastTests` ikke måle** — den er browserens chrome, ikke DOM.
Samme handel som sprogvælgeren. **Skriv det i en kommentar**, så ingen jagter en farve der ikke er
vores.

**Step 3: De to vagter der værner om noget der virker**

1. **`WaitingFor` uden et navn skal fortsat kunne gemmes.** Det er beslutning 4 i designet, og uden
   en påstand er der intet der stopper nogen fra at gøre navnet obligatorisk næste gang.
2. **Et navn der ikke står på listen, kan stadig skrives og gemmes.**

**Step 4: Mutation**

- Fjern `focus()`-kaldet → fokus-testen fejler.
- Gør feltet til et `<select>` over listen → **begge** vagter ovenfor fejler. Det er den mutation der
  viser, hvad "forslag, ikke krav" betyder i praksis.
- Fjern `list`-attributten → en påstand om at optionerne findes fejler.

---

## Task 5: Kontrast, E2E og dokumentation

**Files:**
- Modify: `tests/Todo.E2E/ContrastTests.cs`, `SettingsScreen.cs`, en rejse
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Kontrast**

Uddelegeringsgruppens farver i **begge** temaer, inklusive **tom liste** og **liste med rækker** — to
`@if`-grene, og **en gren er umålt indtil fixturet har noget i den tilstand og rejsen åbner den**.
Sidste leverance målte, at det ikke er nok at opsnappe et kald: kroppen skal bære feltet.

**Step 2: E2E**

Én rejse hele vejen: læg et navn på listen i indstillingerne, gå til opgavelisten, sæt en opgave til
"Venter på", **vælg navnet**, og se opgaven i "Venter på"-sektionen med navnet vist. Det sidste led er
det eneste der ville fange, at forslaget blev valgt men ikke gemt.

Bemærk at `<datalist>`s popup ikke kan drives fra Playwright — sæt værdien i feltet, som en bruger der
vælger, og påstå på **feltets værdi** og på hvad der blev gemt.

**Step 3: Byg før E2E**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test Todo.sln
```

`Todo.E2E.csproj` har **intet** build-trin, og hosten servérer bare `wwwroot`. Springer du bygningen,
tester Playwright den **forrige** frontend — grøn mod det forkerte input.

**Step 4: Dokumentation**

- **`CLAUDE.md`:** testtallene, med hvad hver blok lagde til. Og at **uddelegering er en genvej, ikke
  en tilstand** — der er ingen `Delegated`-status, og den næste der leder efter en, skal kunne se at
  det var et valg.
- **`docs/HANDOFF.md`:** en linje om leverancen, og at **ADO-mentions stadig ikke er verificeret**.
- **Designdokumentet:** afsnit 4 om at `WaitingOn` nu har en forslagsliste, og at listen er forslag
  frem for et krav — med begrundelsen, ikke kun reglen.

---

## Hvad der kan gå galt

**Grep efter hver værdi, ikke efter et tema.** Ordlydsrettelsen af "puljen" blev rød i første kørsel,
fordi der blev grep'et efter én frase mens en anden tekst brugte et andet ord. Ændrer du en
brugervendt streng, så grep efter **strengen**.

**Fem håndskrevne kopier af serverens svarform**, ingen af dem afstemt mod kontrakten af en compiler.
To af dem er settings-former, og sidste leverance troede den ene dækkede begge.

**`prettier --check` flagger filer ingen har rørt.** Arbejdskopien er CRLF, prettier vil have LF. Kør
den kun på filer du selv har navngivet, og kør **ikke** `--write` på hele repoet.

**`<datalist>` i en spalte på 480 px.** Popup'en er browserens og kan overskride bredden uden at
`scrollWidth` ændrer sig. Det kan vi ikke måle — men feltet selv skal fortsat overholde bredden, og
den påstand findes.
