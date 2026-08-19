# Åbn sagen fra forhåndsvisningen Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Hver række i Jira-forhåndsvisningen får en knap der åbner sagen i systemets browser, så man
kan se på en sag før man beslutter at importere den.

**Architecture:** Serveren beregner URL'en med den eksisterende `JiraSettings.BrowseUrl(key)` og lægger
den på `JiraPreviewRow` som et **required** felt. Knappen går gennem `/api/system/open-link` som alle
andre udadgående links i appen. Ingen datamodel, ingen migrering, intet skivenummer.

**Tech Stack:** ASP.NET Core minimal APIs, Angular 22 signal-stores, xunit.v3, Playwright, Vitest.

**Testtal før planen:** Core **90**, Api **186**, E2E **33**, Vitest **184** — alle grønne på `main`.

Besluttet 2026-08-19 sammen med brugeren.

## Kravet

Når forhåndsvisningen viser Jira-sagerne man kan vælge at importere, skal hver række have en **knap
der åbner sagen i browseren**.

## Hvorfor det er et reelt hul og ikke en bekvemmelighed

Skive 11 gav opgavelisten et `external-link` på hver importeret Jira-opgave. Men det virker **efter**
import. På forhåndsvisningen — hvor beslutningen om at importere faktisk tages — er der ingen vej til
sagen.

Med puljen slået til er listen op mod tyve rækker (dine tildelte plus vagt-puljen, se
`2026-08-19-jira-duty-statuses.md`). Titel, status og deadline er ikke altid nok til at afgøre, om en
sag hører i din liste. I dag skal man slå nøglen op i Jira i hånden.

## Beslutning 1: serveren beregner URL'en

Et nyt felt på `JiraPreviewRow`, sat af `/api/jira/preview` gennem den eksisterende
`JiraSettings.BrowseUrl(key)`.

Frontenden *kunne* selv sætte den sammen — den har `jiraBaseUrl` i `SettingsStore` — men så bor
URL-formen `/browse/{key}` **to** steder. Skive 11 målte prisen for netop det: basisURL'en blev
trimmet både i `BrowseUrl` og i `PUT /api/settings`, og da nogen endelig målte det, viste `git log -S`,
at begge ankom i samme commit — så den ene havde aldrig kunnet fyre, og **ingen vidste hvilken**.

Det er desuden samme beslutning som `TodoTask.externalUrl` fra skive 11: **beregnet, aldrig gemt**, så
den følger en ændret basisURL frem for at blive forkert den dag URL'en skifter. `BrowseUrl` er
unit-testet i `tests/Todo.Core.Tests/Jira/JiraSettingsTests.cs`.

## Beslutning 2: feltet er `required`, ikke nullable

Det går imod instinktet, og begrundelsen er værd at have skrevet ned.

**En forhåndsvisning kan ikke ske uden en konfigureret basisURL.** `JiraSettings.IsConfigured` kræver
den (og at den parser som en absolut `http`/`https`-URI — strammet i skive 11's Task 4), og
`/api/jira/preview` afviser med `jira.notConfigured` uden. Nøglen kommer altid fra Jira. **URL'en er
derfor aldrig fraværende på en forhåndsvisningsrække.**

Gør vi den nullable, får vi en `@if`-gren — og de to sidste opgaver i vagt-leverancen har handlet om
præcis dem: en gren er umålt indtil fixturet har noget i den tilstand, og `ContrastTests` sendte rækker
**uden** `isDuty`, så mærkatens farve aldrig blev malet. **Et required felt fjerner grenen frem for at
tilføje en vagt til den.**

Konsekvensen at kende: `BrowseUrl` returnerer `string?`, så endpointet skal håndtere det. Da
`IsConfigured` allerede er passeret på det tidspunkt, er en `?? throw` eller en eksplicit
`SourceException` det ærlige valg — ikke en `!`-assertion, som skjuler antagelsen.

## Beslutning 3: en `<button>` gennem `/api/system/open-link`

**Ikke et `<a href>`.** Photino-vinduet har hverken adresselinje eller tilbage-knap, så en navigation
væk er enkeltrettet. Det gælder markdown-links fra skive 4, dokumentationslinket på health-linjen, og
`external-link` på opgavelisten — samme vej hver gang.

`ApiDocsJourneyTests` har præcedensen for påstanden:

```csharp
Assert.Equal("BUTTON", await el.EvaluateAsync<string>("el => el.tagName"));
```

Det er det eneste der stopper en senere "forenkling" til et link. Læg den samme påstand her, i Vitest
frem for E2E hvis det er nok — skive 11's Task 9 målte, at en Vitest-påstand på `tagName` fangede
netop det brud.

## Tre fælder der allerede er målt

**Playwright skal opsnappe og afbryde `/api/system/open-link`**, ellers åbner **hver** testkørsel en
rigtig browser på maskinen. Det står i `CLAUDE.md` og er ikke teoretisk.

**Knappen er en ny farve på skærmen**, så `ContrastTests` skal nå den — og fixturet skal have en URL,
ellers renderes den ikke. `ContrastTests`' egen `**/api/jira/preview`-handler skal altså have feltet i
kroppen; at opsnappe kaldet er ikke nok. Det var præcis hullet med `isDuty`.

**Mærkaten må ikke havne inde i et element hvis tilgængelige navn en test matcher præcist.** På
opgavelisten er det `TaskListScreen.RowTitled`, og skive 11's Task 9 målte, at `RowTitled` **fanger**
tekst lagt i rækkeknappen (tre E2E-tests faldt). Forhåndsvisningsrækken har ikke samme struktur, så
tjek om der findes en tilsvarende locator, frem for at antage at den er fri.

## Hvad planen skal røre

Kontrakten (`JiraPreviewRow`, et required felt), `JiraEndpoints`' forhåndsvisning (én linje),
`jira-import.html` og dens spec, to oversættelsesnøgler i **`src/Todo.Web/public/i18n/`** (ikke under
`src/app/`), og en gren i `ContrastTests`.

**To opgaver**, delt ved kontraktgrænsen: Task 1 er feltet og endpointet, Task 2 er knappen og dens
vagter. Delingen er ikke kosmetisk — Task 1's required felt fælder `Spec_project_passes_the_type_checker`
på et håndskrevet spec-fixture, og den rettelse hører sammen med kontrakten frem for at gøre Task 2 rød
af en grund der ikke er dens.

Bemærk at det er en udvidelse uden datamodel og uden migrering — den skal derfor **ikke** have et
skivenummer, af samme grund som Swagger-linket i skive 11's forarbejde ikke fik et.

## Mærkaten er afgjort: "Åbn sagen"

Besluttet af brugeren 2026-08-19. Ikke "Åbn SAAS-6354" — rækken viser nøglen i forvejen, og i en spalte
på ~480 px er plads knap.

**Det er samme streng som opgavelistens `external-link` bruger**, og nøglen findes allerede:
`tasks.openIssue`, lagt ind i skive 11's Task 9. **Genbrug den frem for at oprette `jira.openIssue`.**

To grunde. Handlingen er den samme på den samme slags ting — en Jira-sag — så to nøgler med identisk
tekst ville skulle holdes i sync i hånden, og den slags glider fra hinanden. Og skive 11 målte, hvor let
en oversættelsesnøgle bliver tabt: `jira.statusNameInvalid` manglede i **begge** sprogfiler i to
opgaver, uden at paritetstesten kunne se det, fordi den kun sammenligner filerne med hinanden.

**Prisen er en navnerumsskavank**, og den skal stå skrevet frem for at blive opdaget: en `tasks.*`-nøgle
bruges nu på Jira-skærmen. Alternativet — at flytte nøglen til noget delt — ville røre skive 11's
oversættelser og opgavelistens template for en ren kosmetisk gevinst. **Lad den ligge**, men navngiv
skavanken i planen, så den næste der læser `tasks.openIssue` på en Jira-skærm ved, at det var et valg.

---

## Task 1: Kontrakten og endpointet

**Files:**
- Modify: `contracts/openapi.yaml`
- Generated: `src/Todo.Web/src/app/api/todo-client.ts`, `src/Todo.Contracts/Generated/Contracts.g.cs`, `.source-hash`
- Modify: `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Test: `tests/Todo.Api.Tests/JiraEndpointsTests.cs`, `TaskEndpointsTests.cs`

**Step 1: Feltet på kontrakten**

På `JiraPreviewRow`, og **i `required`-listen**:

```yaml
        url:
          type: string
          description: >-
            Where Jira shows this issue. Computed from the configured base URL and the key, never
            stored, so it follows a changed base URL. Always present: a preview cannot happen without
            a configured base URL, so this is required rather than nullable — a nullable field would
            add an @if branch, and a branch is unmeasured until a fixture renders it.
```

**Step 2: Skriv de fejlende tests**

```csharp
    /// <summary>
    /// The button on each row needs somewhere to go, and the server owns the URL shape so `/browse/`
    /// is spelled in one place — the same decision as TodoTask.externalUrl.
    /// </summary>
    [Fact]
    public async Task A_preview_row_carries_the_url_of_the_issue()
    {
        await using var jira = await ConfigureAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.Equal($"{jira.BaseUrl.TrimEnd('/')}/browse/SAAS-1", row.Url);
    }
```

Udvid `PreviewRow`-recorden med `string Url`. **Bevar defaults**, så de toogtyve eksisterende tests er
uændrede.

Læg desuden en påstand på `Wire_format_uses_the_names_the_contract_declares` i `TaskEndpointsTests.cs`.
**Påstå på en værdi, ikke på at feltet findes:** `Assert.Contains("\"url\":\"http", json)`. Et required
felt serialiseres altid, så `Assert.Contains("\"url\":", json)` kan **ikke fejle** — det var præcis
fejlen med `isDuty` i vagt-leverancens Task 4.

**Step 3: Implementér**

I forhåndsvisningen, ét udtryk:

```csharp
Url = settings.BrowseUrl(item.Key)
    ?? throw new SourceException(
        ErrorCodes.JiraNotConfigured,
        "A preview row has no browse URL, which cannot happen once IsConfigured has passed."),
```

**`?? throw`, ikke `!`.** `BrowseUrl` returnerer `string?`, og antagelsen — at `IsConfigured` allerede
er passeret — skal stå skrevet frem for at blive skjult af en assertion. Kaster den nogensinde, er det
en fejl i vores egen rækkefølge, ikke i brugerens data.

**Step 4: Mutationstest**

1. **Byt `BrowseUrl` ud med `item.Key` alene.** Forventet:
   `A_preview_row_carries_the_url_of_the_issue` fejler med den nøgne nøgle mod den fulde URL.
2. **Svæk wire-påstanden** til `"url":` og fjern `Url` fra svaret. Bekræft at den **består** — det er
   beviset på at den svage form er tandløs. Rul tilbage til `"url":"http`.

Rapportér begge, og fejlteksten fra 1 ordret.

**Step 5: Commit**

```bash
git add contracts/openapi.yaml src/Todo.Web/src/app/api/ src/Todo.Contracts/Generated/ src/Todo.Host/Endpoints/ tests/Todo.Api.Tests/
git commit -m "✨ Læg sagens URL på forhåndsvisningsrækken"
```

---

## Task 2: Knappen

**Files:**
- Modify: `src/Todo.Web/src/app/jira/jira-import.html`, `jira-import.ts`, `jira-import.spec.ts`
- Modify: `tests/Todo.E2E/JiraImportScreen.cs`, `JiraImportJourneyTests.cs`, `ContrastTests.cs`

**Step 1: Knappen**

En `<button>` pr. række med mærkaten `tasks.openIssue` — **den nøgle findes allerede** ("Åbn sagen" /
"Open the issue"), lagt ind i skive 11 til opgavelistens link. **Genbrug den; opret ikke
`jira.openIssue`.** Handlingen er den samme på den samme slags ting, og to nøgler med identisk tekst
glider fra hinanden. Prisen er en navnerumsskavank — en `tasks.*`-nøgle på Jira-skærmen — og den er et
bevidst valg frem for at røre skive 11's oversættelser.

**Ikke et `<a href>`.** Photino-vinduet har hverken adresselinje eller tilbage-knap, så en navigation
væk er enkeltrettet. Kaldet går gennem `SystemStore` som markdown-links og opgavelistens
`external-link` — læs `src/Todo.Web/src/app/system/` og følg vejen.

**Der er ingen `@if`.** Feltet er required, så knappen renderes på hver række. Det er hele pointen med
Task 1's beslutning: én gren mindre at måle.

Farven skal have en `dark:`-modpart. Knappen er handlingsnær, ikke dæmpet kontekst — se hvad
opgavelistens `external-link` bruger, og følg den frem for at vælge en ny.

**Step 2: Vitest**

```ts
    const open = row.querySelector<HTMLButtonElement>('[data-testid="jira-open-issue"]')!;

    // A BUTTON, not an anchor: the Photino window has no address bar and no way back.
    expect(open.tagName).toBe('BUTTON');
    expect(open.textContent!.trim()).toBe('Åbn sagen');

    open.click();

    const request = await vi.waitFor(() => http.expectOne('/api/system/open-link'));
    expect(JSON.parse(request.request.body).url).toBe('https://jira.test/browse/SAAS-1');
```

**Step 3: E2E og kontrast**

- `JiraImportScreen` får en `OpenIssueIn(row)`-locator.
- **Rejsen** påstår, at klikket beder `/api/system/open-link` om `…/browse/SAAS-9`, og at elementet er
  en `BUTTON` — samme påstand som `ApiDocsJourneyTests`, og det eneste der stopper en senere
  "forenkling" til et link. **`/api/system/open-link` skal opsnappes og afbrydes**, ellers åbner hver
  testkørsel en rigtig browser på maskinen.
- **`ContrastTests`' og rejsens fixtures skal have `"url"` i kroppen.** Uden feltet læser klienten
  `undefined`, knappen renderes med en tom URL, og påstanden om `/browse/…` måler ingenting. At
  opsnappe kaldet er ikke nok — kroppen skal bære feltet. Samme hul som `isDuty` havde.
- Knappens farve bliver målt **uden** en ny fixture-tilstand, netop fordi der ingen `@if` er. Bekræft
  det ved at male den `text-gray-400 dark:text-gray-600` og se vagten fælde den i begge temaer.

**Step 4: Kør i rækkefølge**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test Todo.sln
```

**Byg før E2E.** `Todo.E2E.csproj` har intet build-trin, og hosten servérer bare `wwwroot`.

**Step 5: Mutationstest**

1. **Skift `<button>` til `<a href>`.** Forventet: `tagName`-påstanden fejler i Vitest **og** i E2E.
2. **Send den nøgne nøgle til open-link.** Forventet: kropspåstanden fejler.
3. **Fjern `"url"` fra `ContrastTests`' fixture.** Forventet: knappen renderes stadig (ingen `@if`),
   men rejsens `/browse/…`-påstand fejler. Det viser hvorfor kroppen skal bære feltet.

**Step 6: Commit**

```bash
git add src/Todo.Web/src/ tests/Todo.E2E/
git commit -m "✨ Åbn Jira-sagen fra forhåndsvisningen"
```

Oversættelserne røres **ikke** — nøglen findes. Er det forkert, sig det frem for at oprette en ny.

---

## Hvad der kan gå galt

**Grep efter hver værdi, ikke efter et tema.** Ordlydsrettelsen `🌐 Kald den 2nd. level supporten` blev
rød i første kørsel, fordi der blev grep'et efter mærkatens frase og nøglenavnet — men markørens tekst
sagde "puljens", ikke "generelle pulje", så tre påstande slap igennem: `ContrastTests`,
`JiraImportJourneyTests` og Vitest-specen. Ændrer du en brugervendt streng, så grep efter **strengen**.

**Fem håndskrevne kopier af serverens svarform.** `settings-store.spec.ts`, `settings.spec.ts`,
`jira-import.spec.ts`, og rutehandlerne i `ContrastTests` og `JiraImportJourneyTests`. Ingen af dem
afstemmes mod kontrakten af en compiler, så et nyt required felt skal skrives ind i **hver** af dem der
sender forhåndsvisningsrækker. Det er stedet en fremtidig skive taber et felt; hullet er dokumenteret i
`CLAUDE.md`, ikke lukket.
