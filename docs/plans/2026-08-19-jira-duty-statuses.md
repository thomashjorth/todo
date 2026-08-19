# Vagt-statusser fra Jira Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Har du 2nd level support-vagten, kan du tænde for at sager i puljens status kommer med i
importen — **også når de ikke er tildelt dig** — og de lander som handlingsklare, ikke som ventende.

**Architecture:** To nye indstillinger ved siden af skive 11's `jira.waitingStatuses`:
`jira.dutyStatuses` (hvilke statusser er puljen) og `jira.onDuty` (har jeg vagten nu). JQL'en får et
`OR status IN (…)`-led, og både forhåndsvisningen og importen genudleder ventendeheden serverside med
**vagt før ventende**. Statusvælgeren fra skive 11 genbruges; der kommer ingen ny skærm og ingen
migrering.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core 10 / SQLite, Angular 22 signal-stores, xunit.v3,
Playwright, Vitest.

---

## Kravet

Udviklerne skiftes til at have 2nd level support. Har man vagten, skal man kunne **tænde for**, at
sager i en given Jira-status kommer med i importen — **også når de ikke er tildelt en selv**.

Konkret hos os: `Afventer general` er den status en sag står i, når den venter på den generelle
pulje. Sagen der udløste kravet var `SAAS-6354`, tildelt Flemming, status `Afventer general` — altså
en sag brugeren skulle kunne tage ind i sin vagtuge, men som skive 11's import aldrig ville se, fordi
JQL'en har `assignee = currentUser()`.

Besluttet 2026-08-19 sammen med brugeren.

## Beslutning 1: det er to indstillinger, ikke én — og der er nu fire i alt

Skive 11 leverede `jira.waitingStatuses`, en **mapning** — *"disse Jira-statusser betyder ventende"* —
anvendt på sager der i forvejen er tildelt dig, plus `jira.includeWaiting` som kontakt.

Vagt-statusserne er noget andet: **"disse statusser skal hentes uanset hvem de er tildelt"**. Det er
en udvidelse af *hvad der hentes*, ikke en oversættelse af *hvad det betyder*. Slås de sammen til én
liste, kan man ikke længere sige *"`Afventer Kunden` betyder ventende, men hent den ikke fra
puljen"* — og det er en helt almindelig ting at ville.

**Og vagten får sin egen kontakt af samme grund som ventende fik det.** `jira.onDuty` er default
`false`. Uden den ville man skulle **rydde listen** for at gå af vagt og **vælge den igen** næste
rotation — præcis den irritation skive 11 undgik ved at dele det ventende par i to. Listen skal
overleve, at vagten slutter.

De fire indstillinger er dermed:

| Nøgle | Betyder | Default |
| --- | --- | --- |
| `jira.waitingStatuses` | disse statusser betyder *ventende* | tom |
| `jira.includeWaiting` | hent ventende sager med | `false` |
| `jira.dutyStatuses` | disse statusser er *puljen* | tom |
| `jira.onDuty` | jeg har vagten nu | `false` |

## Beslutning 2: en vagt-status importeres som `Open`, ikke `WaitingFor`

**Det er den vigtigste beslutning i planen, og den er kontraintuitiv.**

`Afventer general` betyder "venter på den generelle pulje". Er **du** puljen denne uge, venter sagen
på **dig** — den er handlingsklar, ikke parkeret.

Importeres den som `WaitingFor`, lander den i "Venter på", altså **væk** fra deadline-sektionerne. Du
ville skjule præcis det arbejde du har vagten for.

Derfor: er `onDuty` slået til og statussen står i `dutyStatuses`, importeres sagen som **`Open`**, og
`WaitingSince` sættes **ikke**.

**Det er også grunden til at de to lister skal kunne overlappe frit.** `Afventer general` er
*ventende* når du ikke har vagten, og *handlingsklar* når du har. Det er kontakten der afgør det,
ikke statussen. **Vagt slår ventende** — en implementation der behandler overlap som en fejl, har
misforstået kravet.

## Beslutning 3: vagt-rækker henter ikke changeloggen

Følger af beslutning 2. `WaitingSince` er kun meningsfuld for noget der venter på en anden, og en
vagt-sag venter på dig. Skive 11 henter changeloggen **kun** for ventende rækker — ét HTTP-kald pr.
sag — så vagt-rækker koster **nul** ekstra kald.

Bemærk hvad den forkerte beslutning ville have kostet: mappet til `WaitingFor` ville hver vagt-sag
have udløst et changelog-kald **og** landet i den forkerte sektion. To fejl af én.

## Beslutning 4: importrækken skal ikke ændres

Skive 11 flyttede `isWaiting` af `JiraImportRow` og satte `status` i stedet, fordi et required bool
er uhåndhæveligt på wiren. **Den beslutning betaler sig her:** serveren har allerede statusnavnet og
kan udlede *både* ventende og vagt af det plus indstillingerne. `JiraImportRow` ændres **ikke**.

Havde vi beholdt `isWaiting`, skulle kontrakten nu have haft et `isDuty` ved siden af — to
beslutninger sendt fra en klient der ikke kender indstillingerne.

## Puljens størrelse — målt og procesbundet

- **Målt 2026-08-19: 2 sager** i `project = SAAS AND status = "Afventer general" AND resolution = Unresolved`.
- **Procesgrænse oplyst af brugeren: op til 10, ikke højere.** Rotationen tømmer puljen, så den
  akkumulerer ikke.

De to tal er forskellige slags fakta: **de 2 er en måling, der kan vokse; de 10 er en procesgrænse,
der siger *hvorfor* den ikke gør.**

**Hvad det afgør:** forhåndsvisningen bliver maksimalt omkring **tyve rækker** — dine tildelte plus
puljen. Altså **ingen filtrering før import, ingen paginering i UI'et, ingen "vis kun de nyeste"**.
Skærmen fra skive 11 duer som den er.

**Hvad det ikke afgør, og det er den vigtige del: koden må ikke *afhænge* af de ti.** Hentningen
håndterer allerede vilkårlig størrelse, fordi skive 11's paginerings-løkke bruger `startAt`/`total`
og stopper hvis en side kommer tom tilbage. Grænsen informerer **UI-beslutningen**, ikke
korrektheden. Bliver puljen tredive i en uge hvor ingen har vagten, bliver skærmen lang — men intet
går i stykker.

**Og grænsen holder kun fordi rotationen kører.** Bryder processen sammen, holder den ikke. Derfor
står begrundelsen her og ikke bare tallet.

## Tre ting der er uafgjorte, og som planen ikke løser

**Ingen minder dig om at slukke.** En vagt er tidsbegrænset; en indstilling er ikke. Glemmer du den,
bliver du ved med at trække puljen ind. En slutdato er dyrere end den lyder — den kræver noget der
kører ved midnat, og skive 9 undgik netop det ved at gøre udskudtheden **beregnet**. Task 5 giver i
stedet tilstanden en **synlig** markør, så den ikke er tavs. Vil du have en slutdato, er det en egen
leverance.

**Importerede pulje-sager forsvinder ikke af sig selv.** `Status` er lokal efter import, så tager en
kollega sagen i næste uge, ligger din kopi stadig der. Det er det rigtige design — ellers kunne en
senere sync trække noget tilbage du havde markeret færdigt — men puljen churner mere end dine egne
sager, så du vil se det oftere.

**`alreadyImported` gælder på tværs af vagtuger.** Dedup er `SourceId` + `ExternalKey`, så en sag du
tog ind i uge 34 vises som "importeret tidligere", hvis den kommer i puljen igen i uge 38. Det er
formentlig rigtigt — du har den jo — men det er ikke prøvet i brug.

## Testtal før planen

`Todo.Core.Tests` **83**, `Todo.Api.Tests` **168**, `Todo.E2E` **32**, Vitest **178** — alle grønne
på `main` efter skive 11.

---

## Task 1: Kontrakten

**Files:**
- Modify: `contracts/openapi.yaml`
- Generated (kør scriptet, commit **alle fire** filer): `src/Todo.Web/src/app/api/todo-client.ts`,
  `src/Todo.Contracts/Generated/Contracts.g.cs`, `.source-hash`

**Step 1: `SettingsResponse` og `SettingsRequest`**

Læg to felter på **begge** skemaer, i samme form som `jiraWaitingStatuses`/`jiraIncludeWaiting`:

```yaml
        jiraDutyStatuses:
          type: array
          items:
            type: string
          description: >-
            Statuses that mean "waiting for the shared duty pool". Issues in these are fetched
            regardless of assignee when jiraOnDuty is on, and they arrive actionable rather than
            waiting — see the plan's decision 2.
        jiraOnDuty:
          type: boolean
          description: >-
            Whether the user currently holds the 2nd level support duty. Off by default. Separate
            from the list so the list survives going off duty.
```

**Læg `jiraDutyStatuses` og `jiraOnDuty` i `SettingsResponse`'s `required`-liste**, ved siden af
`jiraWaitingStatuses`, `jiraIncludeWaiting` og `hasJiraToken`. Skive 11 målte hvorfor: uden det
genereres de som optionelle, og Angular skal så skrive `?? []` overalt — og `@if` indsnævrer ikke et
signal-kald. **Ikke** på `SettingsRequest`, hvis fulde-erstatnings-semantik afhænger af at hvert felt
kan udelades.

**Step 2: `isDuty` på `JiraPreviewRow`**

```yaml
        isDuty:
          type: boolean
          description: >-
            Whether this issue came from the duty pool rather than being assigned to the user. The
            screen labels it, so a pool issue is not mistaken for one of your own.
```

Læg den i `required`-listen sammen med `isWaiting`.

**`JiraImportRow` ændres ikke** — se beslutning 4. Serveren udleder både ventende og vagt af `status`
plus indstillingerne. **Læg ikke et `isDuty` på importrækken.**

> **Rettet efter kørslen, 2026-08-19. Leveret i `6e1c19b`.** Fire fejl, og de to første betyder noget
> for de øvrige tasks:
>
> 1. **"Ingen frontend" holder ikke, og "168, uændret" var 168 med én rød.** Et nyt **required** felt
>    på et skema frontenden **konstruerer** fælder `Spec_project_passes_the_type_checker` — den vagt
>    skive 11 lagde ind allersidst, og som denne plan er skrevet henover.
>    `jira-store.spec.ts` har et håndskrevet `PreviewRowJson`-fixture der føres ind i
>    `new JiraPreviewRow(...)`, altså ind i `IJiraPreviewRow`: `TS2345 … Property 'isDuty' is missing`.
>    **Regn med at hvert nyt required felt koster en fixture-linje, og at den skal lægges i Task 1.**
> 2. **Og den samme fælde findes uden en compiler bag.** `settings-store.spec.ts`' `SettingsJson` er
>    **bevidst** sin egen form frem for `Partial<ISettingsResponse>`, fordi wiren staver et fraværende
>    sprog `null` og ikke `undefined` — og netop derfor **ser typetjekkeren den ikke**. Uden de to nye
>    felter ville fixturet have givet `SettingsStore` `jiraDutyStatuses: undefined` i **hver** settings-
>    spec, mens typerne lover `string[]`: grønt, tavst, i alle 178 Vitest. Fixturets egen doc-kommentar
>    påstod desuden "**three** of them non-optional"; tallet er nu fem, og kommentaren er rettet med en
>    linje om at typetjekkeren ikke dækker her.
> 3. **Scriptet skriver *tre* filer, ikke fire.** `todo-client.ts`, `Contracts.g.cs` og `.source-hash`
>    — `openapi.yaml` er håndskrevet og tælles med i commit'en, ikke i genereringen. Advarslen om
>    `.source-hash` står ved magt; kun tællingen var skæv.
> 4. Kosmetisk: `SettingsResponse.required` er nu filens længste linje (101 tegn mod 98). Der findes
>    ingen linter på `contracts/openapi.yaml`. Skal der flere felter i listen, må den ombrydes.
>
> Endeligt: Core **83**, Api **168**, E2E **32**, Vitest **178** — alle på baseline, som forudsagt.

**Step 3: Generér og verificér**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
dotnet test tests\Todo.Api.Tests\Todo.Api.Tests.csproj --filter "FullyQualifiedName~GeneratedCodeFreshnessTests"
```

Forventet: PASS. Bekræft i `Contracts.g.cs`, at `JiraDutyStatuses` fik en
`= new Collection<string>()`-initializer (det er hvad `required` køber), og at `JiraImportRow` er
uændret.

`ContractDriftTests` skal **blive grøn** — der kommer ingen nye ruter, kun felter. Fejler den, har du
lagt en rute til ved et uheld.

**Step 4: Commit**

```bash
git add contracts/openapi.yaml src/Todo.Web/src/app/api/ src/Todo.Contracts/Generated/
git commit -m "📝 Læg vagt-statusserne på kontrakten"
```

---

## Task 2: Indstillingen i backenden

**Files:**
- Modify: `src/Todo.Core/Settings/SettingKeys.cs`, `src/Todo.Core/Jira/JiraSettings.cs`,
  `JiraSettingsReader.cs`, `src/Todo.Host/Endpoints/SettingsEndpoints.cs`
- Test: `tests/Todo.Api.Tests/JiraSettingsEndpointsTests.cs`

> **Rettet efter kørslen, 2026-08-19. Leveret i `7775afd`.** Tre fejl, og de to første er
> korrektioner af planens egne påstande.
>
> 1. **Planens note om hvad `Going_off_duty_keeps_the_list` *ikke* påstår, er forkert.** Den påstår
>    netop, at kontakten kan slås fra — `Assert.False(after!.JiraOnDuty)` — og den var den **eneste**
>    test der fældede mutation B. Der skulle altså **ingen** ekstra test til, og tallet blev **171**,
>    ikke 172. Forskellen fra skive 11 er, at `Turning_waiting_back_off_turns_it_off` skulle skrives
>    fordi **ingen** ventende-test slog fra igen; her gør round-trip-partneren det indbygget.
> 2. **`Duty_is_off_until_asked_for` er grøn af den forkerte grund og kan stort set ikke fejle** — og
>    årsagen er Task 1's `required`, som ellers var en rigtig beslutning. Den genererede
>    `SettingsResponse` bærer felterne med en `= new Collection<string>()`-initializer, så et svar der
>    **aldrig tildelte dem** serialiserer `[]` og `false`, altså præcis det testen påstår. Den bestod
>    **før** implementeringen fandtes, og består også hvis begge linjer fjernes fra `ReadAllAsync`.
>    Beholdt, med målingen skrevet i docstringen — men **den skal ikke tælles som en vagt.** Samme
>    blindvinkel som dens ventende-modpart allerede indrømmer.
> 3. **To nye required medlemmer på `JiraSettings`-recorden brød begge konstruktionssteder**, som
>    bruger navngivne argumenter og derfor ikke kan udvides tavst: `FakeJira.SourceFor` (`CS7036` —
>    ville have brudt **hele** Api-suiten) og `JiraSettingsTests.With`. Den anden fangede først da
>    **Core** blev kørt, fordi Api-projektet ikke refererer Core.Tests. Planens `git add`-liste nævnte
>    ingen af dem.
>
> **Til Task 3:** `FakeJira.SourceFor` hardcoder i dag `DutyStatuses: []` og `OnDuty: false`. Den skal
> føre rigtige værdier igennem, og de nye parametre skal have defaults der bevarer skive 11's ti tests.
>
> Endeligt: Api **171**, Core **83**, E2E 32, Vitest 178.

**Step 1: Skriv de fejlende tests**

```csharp
    [Fact]
    public async Task The_duty_statuses_round_trip_as_a_list()
    {
        string[] names = ["Afventer general"];

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings", new { jiraDutyStatuses = names, jiraOnDuty = true });

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.Equal(names, after!.JiraDutyStatuses);
        Assert.True(after.JiraOnDuty);
    }

    /// <summary>
    /// Default off, and the plan's decision 1 says why: the list has to survive going off duty, or
    /// you would re-pick it every rotation.
    /// </summary>
    [Fact]
    public async Task Duty_is_off_until_asked_for()
    {
        var settings = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(settings!.JiraOnDuty);
        Assert.Empty(settings.JiraDutyStatuses);
    }

    /// <summary>
    /// The whole point of two settings rather than one. Going off duty must not lose the list.
    /// </summary>
    [Fact]
    public async Task Going_off_duty_keeps_the_list()
    {
        await Host.Client.PutAsJsonAsync(
            "/api/settings",
            new { jiraDutyStatuses = new[] { "Afventer general" }, jiraOnDuty = true });

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings",
            new { jiraDutyStatuses = new[] { "Afventer general" }, jiraOnDuty = false });

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.False(after!.JiraOnDuty);
        Assert.Equal(["Afventer general"], after.JiraDutyStatuses);
    }
```

Udvid `SettingsBody`-recorden med `string[] JiraDutyStatuses` og `bool JiraOnDuty`.

**Step 2: Kør dem og se dem fejle.** De nye felter mangler i svaret.

**Step 3: Implementér**

`SettingKeys`: `JiraDutyStatuses = "jira.dutyStatuses"`, `JiraOnDuty = "jira.onDuty"`.

`JiraSettings` får `IReadOnlyList<string> DutyStatuses` og `bool OnDuty`.

`JiraSettingsReader` læser dem — den henter i forvejen alle rækker med præfiks `jira.`, så der skal
kun mappes.

`SettingsEndpoints`: **gem `jiraOnDuty` som `request.JiraOnDuty ? "true" : null`**, altså slået fra
**fjerner** rækken. Skive 11 målte hvorfor: en literal `"false"`-række gør `Settings`-tabellen
ikke-tom, og to eksisterende sprogtests påstår `Assert.Empty`/`Assert.Single` på **hele** tabellen.
Læseren læser fravær som fra, så adfærden er uændret.

**Step 4: Kør testene.** Api går fra 168 til **171**. Afviger det, sig det frem for at runde af.

**Step 5: Mutationstest**

Fjern grenen der rydder `jira.onDuty`-rækken, så *fra* ikke længere kan slås fra. Forventet: en test
fejler. Skive 11 fandt, at netop den mutation lod **alle fjorten** settings-tests bestå, indtil
`Turning_waiting_back_off_turns_it_off` blev skrevet — så `Going_off_duty_keeps_the_list` er ikke
nok, den påstår om **listen**, ikke om kontakten. **Skriv den test der mangler**, hvis mutationen
ikke fælder noget.

**Step 6: Commit**

```bash
git add src/Todo.Core/ src/Todo.Host/ tests/Todo.Api.Tests/JiraSettingsEndpointsTests.cs
git commit -m "✨ Gem vagt-statusserne og kontakten, hvor fra fjerner rækken"
```

---

## Task 3: JQL'en

**Files:**
- Modify: `src/Todo.Host/Jira/JiraTaskSource.cs`
- Modify: `tests/Todo.TestSupport/Jira/FakeJira.cs`
- Test: `tests/Todo.Api.Tests/JiraTaskSourceTests.cs`

**Step 1: Skriv de fejlende tests**

```csharp
    /// <summary>
    /// The duty clause widens the query beyond assignee, which is the whole requirement. Asserting
    /// on the JQL the source sent is the only place that can see it.
    /// </summary>
    [Fact]
    public async Task On_duty_the_query_also_asks_for_the_pool()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general"], onDuty: true).FetchAssignedAsync();

        Assert.Contains("project = SAAS", jira.LastJql);
        Assert.Contains("assignee = currentUser()", jira.LastJql);
        Assert.Contains("status IN (\"Afventer general\")", jira.LastJql);
        Assert.Contains(" OR ", jira.LastJql);
    }

    /// <summary>
    /// Off duty the query must be byte-for-byte what slice 11 sent. An empty IN list is not just
    /// pointless — `status IN ()` is a JQL syntax error, so a naive implementation fails against the
    /// real instance while the fake happily answers anything.
    /// </summary>
    [Fact]
    public async Task Off_duty_the_query_is_unchanged()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general"], onDuty: false).FetchAssignedAsync();

        Assert.DoesNotContain("status IN", jira.LastJql);
        Assert.DoesNotContain(" OR ", jira.LastJql);
    }

    [Fact]
    public async Task An_empty_duty_list_adds_no_clause_even_on_duty()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: [], onDuty: true).FetchAssignedAsync();

        Assert.DoesNotContain("status IN", jira.LastJql);
    }

    [Fact]
    public async Task Two_duty_statuses_are_both_in_the_clause()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general", "Venter på support"], onDuty: true)
            .FetchAssignedAsync();

        Assert.Contains("\"Afventer general\", \"Venter på support\"", jira.LastJql);
    }

    /// <summary>
    /// A status name comes from a setting, and a setting is user input. JQL has quotes, so a name
    /// carrying one could change the query's meaning. The picker only ever offers instance names, so
    /// this cannot happen by accident — which is exactly why it needs a test rather than trust.
    /// </summary>
    [Fact]
    public async Task A_status_name_with_a_quote_is_refused()
    {
        await using var jira = await FakeJira.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS", duty: ["Af\"venter"], onDuty: true).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.JiraStatusNameInvalid, exception.Code);
        Assert.Empty(jira.SearchRequests);
    }
```

**`FakeJira.SourceFor` skal have to nye valgfrie parametre**, `duty` og `onDuty`, med defaults der
bevarer skive 11's ti tests uændret. Verificér at de ti stadig er grønne bagefter — er de ikke, har
du ændret en default.

**Step 2: Kør dem og se dem fejle.**

**Step 3: Implementér**

Læg `ErrorCodes.JiraStatusNameInvalid = "jira.statusNameInvalid"` til. En kode er en **identitet**;
stav den rigtigt første gang.

JQL'en bygges som:

```
project = {key} AND resolution = Unresolved
  AND (assignee = currentUser() OR status IN ("A", "B"))
  ORDER BY duedate ASC
```

**Kun** når `OnDuty` **og** `DutyStatuses` er ikke-tom. Ellers **præcis** skive 11's streng — læs den
i koden frem for at skrive den om, så `Off_duty_the_query_is_unchanged` betyder noget.

Hvert statusnavn valideres før det sættes ind: afvis navne der indeholder `"` eller `\` med
`SourceException(ErrorCodes.JiraStatusNameInvalid, …)`, og gør det **før** kaldet, så
`Assert.Empty(jira.SearchRequests)` er sand. Samme greb som projektnøglens `^[A-Z][A-Z0-9_]*$` —
men et statusnavn kan indeholde mellemrum og danske tegn, så en hvidliste af tegn er forkert her;
en sortliste af de to JQL-farlige er den rigtige.

**Step 4: Mutationstest**

1. **Udsend `status IN ()` når listen er tom.** Forventet: `An_empty_duty_list…` fejler.
2. **Behold `OR`-leddet uanset `OnDuty`.** Forventet: `Off_duty_the_query_is_unchanged` fejler.
3. **Drop citat-valideringen.** Forventet: `A_status_name_with_a_quote_is_refused` fejler — og
   bemærk at `Assert.Empty(jira.SearchRequests)` er den halvdel der beviser, at afvisningen skete
   **før** kaldet.

Rapportér hver fejltekst ordret.

**Step 5: Commit**

```bash
git add src/Todo.Core/Errors/ErrorCodes.cs src/Todo.Host/Jira/ tests/Todo.TestSupport/Jira/ tests/Todo.Api.Tests/JiraTaskSourceTests.cs
git commit -m "✨ Udvid JQL'en med puljen, kun når vagten er slået til"
```

---

## Task 4: Mapningen — vagt slår ventende

**Files:**
- Modify: `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Test: `tests/Todo.Api.Tests/JiraEndpointsTests.cs`

**Det er planens kerne.** Læs beslutning 2 igen før du begynder.

> **Omformet 2026-08-19 efter Task 3's overlevering, som målte at reglen bor to steder.**
> `JiraEndpoints.cs` beregner `isWaiting` **to** gange — linje 92 (forhåndsvisning) og linje 159
> (import) — så vagt-reglen er to steder den kan glemmes, og kun det ene har i dag en test der ville
> bemærke det. **Udtræk beslutningen til én ren funktion i `Todo.Core` først**, og lad begge
> kaldesteder bruge den. Så er der ét sted, én test, og Core har ingen HTTP at slås med.
>
> To filer, **én type pr. fil** som repoet kræver — også for enums:
>
> ```csharp
> namespace Todo.Core.Jira;
>
> /// <summary>What a Jira status means to this user right now. Three roles, not two: Duty and
> /// Actionable both import as Open, but only Duty is labelled on screen and only Waiting pays for a
> /// changelog call.</summary>
> public enum JiraStatusRole { Actionable, Duty, Waiting }
> ```
>
> ```csharp
> namespace Todo.Core.Jira;
>
> public static class JiraStatusRoles
> {
>     /// <summary>
>     /// Branch order is load-bearing, exactly as in DeadlineBuckets.For. Duty wins: the same status
>     /// means "waiting for the pool" when you are not it, and "waiting for you" when you are — so the
>     /// switch decides, not the status. Reversing these two hides the work you hold the duty for.
>     /// </summary>
>     public static JiraStatusRole For(string statusName, JiraSettings settings)
>     {
>         if (settings.OnDuty
>             && settings.DutyStatuses.Contains(statusName, StringComparer.Ordinal))
>         {
>             return JiraStatusRole.Duty;
>         }
>
>         return settings.WaitingStatuses.Contains(statusName, StringComparer.Ordinal)
>             ? JiraStatusRole.Waiting
>             : JiraStatusRole.Actionable;
>     }
> }
> ```
>
> `StringComparer.Ordinal` af samme grund som i skive 11 — navnene kommer fra instansen i samme form
> begge veje, og skive 11 målte at netop det valg var **uvagtet**, indtil
> `A_status_that_differs_only_in_case_is_not_the_waiting_status` blev skrevet.
>
> **Læg Core-tests på rækkefølgen** i `tests/Todo.Core.Tests/Jira/`. De kræver ingen host, og de er
> det sted ombytningen skal ses fejle — Api-testene nedenfor måler at *endpointsene* bruger reglen,
> Core-testene at reglen *er* rigtig. To forskellige påstande.
>
> **Trim og blank-filtrering hører ikke her.** Task 3 lagde dem i `JqlFor`, hvor de beskytter
> forespørgslen. Sammenligningen her sker mod navne Jira selv har sendt tilbage, så en gentagelse
> ville være den døde-trimning-fælde igen.

**Step 1: Skriv de fejlende tests**

```csharp
    /// <summary>
    /// The plan's decision 2, and the assertion that makes it real. `Afventer general` means
    /// "waiting for the shared pool"; when you *are* the pool, the issue is waiting for you, so it
    /// has to arrive actionable. Imported as WaitingFor it would land in "Venter på" — hidden from
    /// the deadline sections, which is exactly the work you hold the duty for.
    /// </summary>
    [Fact]
    public async Task A_duty_row_arrives_open_rather_than_waiting()
    {
        await using var jira = await ConfigureAsync(
            dutyStatuses: ["Afventer general"], onDuty: true);

        await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
        });

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Null(task.WaitingOn);
    }

    /// <summary>
    /// The two lists overlap on purpose: the same status is waiting when you are off duty and
    /// actionable when you are on it. The switch decides, not the status. An implementation that
    /// treats the overlap as a conflict has misread the requirement.
    /// </summary>
    [Fact]
    public async Task Duty_beats_waiting_when_a_status_is_in_both_lists()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsDuty);
        Assert.False(row.IsWaiting);
        Assert.Null(row.Excluded);
        Assert.Null(row.WaitingSince);
    }

    /// <summary>
    /// The same fixture off duty. Same status, opposite meaning — this is the pair that proves the
    /// switch is what decides.
    /// </summary>
    [Fact]
    public async Task The_same_status_is_waiting_when_off_duty()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: false);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.False(row.IsDuty);
        Assert.True(row.IsWaiting);
    }

    /// <summary>
    /// Decision 3. WaitingSince is only meaningful for something waiting on somebody else, and the
    /// changelog is one HTTP call per issue — so a duty row must not pay for one.
    /// </summary>
    [Fact]
    public async Task A_duty_row_does_not_fetch_the_changelog()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: true);

        await Preview();

        Assert.Empty(jira.ChangelogRequests);
    }

    /// <summary>
    /// A pool issue is not one of yours, and the screen has to be able to say so.
    /// </summary>
    [Fact]
    public async Task A_row_that_is_not_in_the_duty_list_is_not_marked_as_duty()
    {
        await using var jira = await ConfigureAsync(
            dutyStatuses: ["Afventer general"], onDuty: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.False(row.IsDuty);
    }
```

Udvid `ConfigureAsync` med `dutyStatuses` og `onDuty`, og `PreviewRow`-recorden med `bool IsDuty`.
**Bevar defaults**, så skive 11's sytten tests er uændrede.

**Step 2: Kør dem og se dem fejle.**

**Step 3: Implementér**

Rækkefølgen i handleren er bærende, ligesom `DeadlineBuckets.For`'s grene:

```csharp
var isDuty = settings.OnDuty
    && settings.DutyStatuses.Contains(item.StatusName, StringComparer.Ordinal);

// Duty wins. The same status means "waiting for the pool" when you are not it, and "waiting for
// you" when you are — so the switch decides, not the status. Reversing these two lines hides the
// work you hold the duty for.
var isWaiting = !isDuty
    && settings.WaitingStatuses.Contains(item.StatusName, StringComparer.Ordinal);
```

`StringComparer.Ordinal` af samme grund som i skive 11: navnene kommer fra instansen i samme form
begge veje, og en versalufølsom sammenligning ville gøre to forskellige Jira-statusser til én.

Derefter uændret: `waitingSince` hentes **kun** når `isWaiting`, `excluded` sættes **kun** når
`isWaiting && !IncludeWaiting`, og importen skriver `Status = isWaiting ? WaitingFor : Open`.
`isDuty` bæres med på forhåndsvisningsrækken.

**Step 4: Mutationstest — fem, og de to første er planens kerne**

1. **Byt de to linjer om**, så `isWaiting` beregnes først og `isDuty` bliver `!isWaiting && …`.
   Forventet: `Duty_beats_waiting…` fejler.
2. **Map en vagt-række til `WaitingFor`.** Forventet: `A_duty_row_arrives_open…` fejler.
3. **Ignorér `OnDuty`**, så listen alene afgør. Forventet: `The_same_status_is_waiting_when_off_duty`
   fejler.
4. **Hent changeloggen for vagt-rækker.** Forventet: `A_duty_row_does_not_fetch_the_changelog` fejler.
5. **Sæt `isDuty = true` på hver række.** Forventet:
   `A_row_that_is_not_in_the_duty_list_is_not_marked_as_duty` fejler.

Rapportér hver fejltekst ordret. **Fælder en mutation ingenting, er den tilsvarende vagt en
formodning** — skive 11 fandt ni af dem, alle skrevet i planen frem for i koden.

**Step 5: Wire-format**

Læg `"isDuty":` på `Wire_format_uses_the_names_the_contract_declares`. Drift-testen ser kun stier og
metoder, så et nyt felt er usynligt for den.

**Step 6: Commit**

```bash
git add src/Todo.Host/Endpoints/ tests/Todo.Api.Tests/
git commit -m "✨ Lad vagten slå ventende, så puljens sager er handlingsklare"
```

---

## Task 5: Frontenden

**Files:**
- Modify: `src/Todo.Web/src/app/settings/settings-store.ts`, `settings.html`, `settings.ts`
- Modify: `src/Todo.Web/src/app/jira/jira-import.html`, `jira-import.ts`
- Modify: `src/Todo.Web/public/i18n/da.json`, `en.json` — **`public/`, ikke `src/`**
- Test: de tilhørende `.spec.ts`

**Step 1: `SettingsStore`**

To nye signaler, `jiraDutyStatuses` og `jiraOnDuty`. **`save` skal bære dem med i `current`** — nu
syv felter. Skive 11's egen fejl var, at storen sendte ét felt og ryddede resten; en test på netop
det findes, og den skal udvides frem for at blive suppleret.

**Step 2: Indstillingssiden**

En statusvælger mere, under den ventende, plus kontakten `jiraOnDuty`. **Genbrug den hentede
statusliste** — der skal ikke et kald mere til.

**Rækkefølgen på siden bærer betydning:** vagt-sektionen skal stå **efter** den ventende, fordi vagt
*overskriver* ventende. Læses de omvendt, ser det ud som om ventende vinder.

**Og tilstanden skal være synlig.** Det er min beslutning frem for en slutdato: ingen minder dig om
at slukke, så når `jiraOnDuty` er slået til, skal det stå med ord — ikke kun som en afkrydset boks
langt nede på en side du sjældent åbner. Sæt en markør på **importskærmen**, hvor du faktisk ser
puljen: *"Du har vagten — puljens sager er med."*

**Step 3: Importskærmen**

En vagt-række mærkes, så en pulje-sag ikke forveksles med en af dine egne. Brug `isDuty` fra
kontrakten. Mærkaten er **ikke** en fejl eller en advarsel — den er kontekst, så dæmpet tekst er
rigtigt: `text-gray-500 dark:text-gray-400`.

**Frontendens fælder** (målt i skive 11, alle sammen):

- Kun Tailwind utility-klasser; hver `bg-*`/`text-*`/`border-*` har en `dark:`-modpart.
- **Tailwind 4's palette er oklch**, ikke Tailwind 3's hex. `gray-400` er **2,60:1** på hvid. Regn
  fra `node_modules/tailwindcss/theme.css`.
- **Paret er ikke samme trin på begge sider:** `dark:text-gray-500` fejler med 3,67:1 på `gray-900`.
- Hvert felt har en `placeholder-*`-klasse — uden arves `currentColor` med ~54 % alfa.
- **`@if` indsnævrer ikke et signal-kald.** Bind med `@let`, ikke `as`.
- Hver streng er en nøgle i **begge** sprogfiler. Paritetstesten er
  `src/Todo.Web/src/app/i18n/translations.spec.ts`, en **Vitest**-spec.
- Specs bruger `HttpTestingController` med de **rigtige** genererede klienter. Der findes **ingen**
  fake af klienten i dette repo.

**Step 4: Mutationstest**

1. **Lad `save` udelade de to nye felter.** Forventet: regressionstesten fejler.
2. **Fjern `isDuty`-mærkaten.** Forventet: en spec fejler.
3. **Fjern en nøgle fra `en.json`.** Forventet: paritetstesten fejler med nøglens navn.

**Step 5: Kør og commit**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
git add src/Todo.Web/src/ src/Todo.Web/public/i18n/
git commit -m "✨ Vælg vagt-statusserne, og vis når puljen er med"
```

---

## Task 6: E2E og dokumentation

**Files:**
- Modify: `tests/Todo.E2E/JiraImportJourneyTests.cs`, `JiraImportScreen.cs`, `SettingsScreen.cs`,
  `ContrastTests.cs`
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: E2E**

Én rejse, der er værd at have hele vejen: **slå vagten til, forhåndsvis, se en pulje-række mærket,
importér den, og se opgaven i deadline-sektionerne frem for i "Venter på".** Det er kravet, og det er
den ene test der ville fange, at beslutning 2 blev omgjort.

Opsnap kaldene med `page.RouteAsync` som skive 11 gør — `**/api/jira/preview` svarer med en række i
`Afventer general` og `isDuty: true`. **`/api/system/open-link` skal fortsat opsnappes og afbrydes**,
ellers åbner hver testkørsel en rigtig browser.

**Step 2: Kontrastvagten**

To nye grene: vagt-mærkaten på rækken, og markøren om at vagten er slået til. **En `@if`-gren er
umålt, indtil fixturet har noget i den tilstand og rejsen åbner den** — så begge koster en
fixture-tilstand og et klik.

**Step 3: Byg før E2E**

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test Todo.sln
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

`Todo.E2E.csproj` har **intet** build-trin; hosten servérer bare `wwwroot`. Springer du bygningen,
tester Playwright den forrige frontend — grøn mod det forkerte input.

**Step 4: Dokumentationen**

- **`CLAUDE.md`:** testtallene, og at der nu er **fire** Jira-indstillinger. Skriv **vagt slår
  ventende** ned som en konvention, ikke kun som en kommentar i koden — det er en
  rækkefølgeafhængighed på linje med `DeadlineBuckets.For`'s grene, og gættet uden begrundelsen
  falder den anden vej.
- **`docs/HANDOFF.md`:** en linje om leverancen, og at ADO-mentions **stadig** ikke er verificeret.
- **Designdokumentet:** afsnit 4a udvides med vagt-begrebet, og afsnit 9 får leverancen. Skriv
  puljens to tal og deres kilder — de 2 målt, de 10 oplyst — plus at koden **ikke** afhænger af dem.
- **Skriv de tre uafgjorte ting ned** fra afsnittet ovenfor, så de ikke ser ud som huller nogen har
  overset: ingen påmindelse om at slukke, importerede pulje-sager der bliver liggende, og
  `alreadyImported` på tværs af vagtuger.

**Step 5: Commit**

```bash
git add tests/ CLAUDE.md docs/
git commit -m "✅ E2E på vagt-puljen og lektionerne skrevet ned"
```

---

## Hvad der kan gå galt

**`status IN ()` er en JQL-syntaksfejl**, og den falske Jira svarer villigt på hvad som helst. Derfor
er `An_empty_duty_list_adds_no_clause_even_on_duty` en påstand om **strengen**, ikke om svaret — det
er den eneste form der kan fange det uden den rigtige instans.

**Statusnavne med danske tegn i en URL.** JQL'en sendes som query-parameter, og skive 11 målte at
`UriBuilder` af-escaper `%20` tilbage til et mellemrum mens `%3D` bliver stående. URL'en bygges som
streng; `Venter på support` skal escapes rigtigt hele vejen. Der er en test på to statusser netop
fordi kommaet og mellemrummet er der, hvor det kan gå galt.

**Overlappet er ikke en kanttilfælde, det er hovedtilfældet.** `Afventer general` står i begge lister
hos denne bruger. En implementation der antager, at listerne er disjunkte, virker på tomme lister og
fejler i brug.

**Puljens to tal er en procesantagelse.** Bliver rotationen afbrudt, vokser puljen, og skærmen bliver
lang. Intet går i stykker — men hvis det sker ofte, er filtrering før import den næste samtale, ikke
en fejlrettelse.
