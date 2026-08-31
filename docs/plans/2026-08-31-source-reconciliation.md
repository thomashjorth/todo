# Importen afstemmer det den allerede har hentet — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** Importen skal foreslå at lukke en opgave hvis sag er **løst** eller **tildelt en anden** i
kilden, og at genåbne en opgave der er færdig her mens kilden siger andet.

**Designet er `docs/plans/2026-08-31-source-reconciliation-design.md`.** Læs det først — de fire
beslutninger står i afsnit 1 med hvem der traf dem, og afsnit 2 bærer de fem målinger resten hviler
på. Forgængeren `2026-08-24-import-closure-design.md` leverede den halvdel af funktionen der
findes i dag, og dens afsnit 9 fravalgte netop den anden halvdel; læs også den, så du kan se hvad
der omgøres.

**Arkitektur:** `ITaskSource` får **én** ny metode, `ReconcileAsync`, der svarer på to spørgsmål —
hvad er tilstanden nu, og er sagen stadig min. Den kaldes kun med de nøgler der er **åbne her og
manglede i dagens hentning**, altså en håndfuld. Svarene bliver til ganske almindelige
forhåndsvisningsrækker; kontrakten vokser med felter, ikke med en rækketype. Genåbningen kræver
**ingen** afstemning og er en ren regel på rækker vi har i forvejen. Ingen migrering, ingen ny
datamodel, intet nyt endpoint.

**Teknologi:** .NET 10 minimal APIs, EF Core 10 + SQLite, Angular 22 med signal-stores, Tailwind 4,
xUnit, Playwright, Vitest.

**Planen kan være forkert.** Over seks leverancer fandt gennemgangene omkring hundrede planfejl og
nul udførelsesfejl. Mål frem for at tro på det der står her, og **ret planfilen** når en måling
modsiger den.

## Maskinens fælder der rammer netop denne plan

- `pwsh` findes ikke. `npm.cmd`/`ng.cmd`, aldrig `npm`/`ng`.
- **Brug `Edit`, ikke `sed` eller `Write`, på eksisterende filer.** Begge skriver LF i denne
  CRLF-arbejdskopi. Skal `Write` bruges, konvertér tilbage og verificér med
  `tr -dc '\r' < fil | wc -c` mod `tr -dc '\n' < fil | wc -c` — **ikke** med `grep -cv $'\r$'`, som
  giver et falsk bestået.
- **Kør `scripts\build-web.ps1` før E2E.** E2E-suiten bygger ikke Angular.
- **Ret kontrakten og kør `scripts\generate-api.ps1`**, ellers fejler friskheds-testen. Genereret
  kode committes.
- **Ingen `"` i en `git commit -m`-heredoc**, og `@'…'@` er PowerShells here-string — ikke Bash'.
  Verificér et emne med gitmoji på byte-niveau med `od` **gennem Bash-værktøjet**; en PowerShell-pipe
  tilføjer en BOM der ligner en fejl der ikke findes. **Bytesne er ikke de samme for de tre gitmoji**,
  og en forventning kopieret fra et doc-trin til et kode-trin ligner en ægte fejl: `✨` er
  `342 234 250`, `✅` er `342 234 205`, `📝` er `360 237 223 235`. Målt i Task 1, hvor netop den
  forveksling stod i opgavens egen tekst.
- **En umappet rute under `/api/` giver ikke 404.** GET giver `200` med `index.html` i kroppen,
  POST og PUT giver `405`.
- Én Vitest-fil køres med `--include` og en **sti**:
  `npm.cmd run test --prefix src\Todo.Web -- --watch=false --include src/app/ado/ado-store.spec.ts`.
- **Kald aldrig noget `Task` eller `TaskStatus`** i C#.
- **Kør aldrig mod `%APPDATA%\TodoApp\todo.db`**, og dræb aldrig en `Todo.Host` du ikke selv har
  startet — find den på porten, ikke på navnet.

## Sproget i koden er engelsk, og planens kodestumper er det ikke

**Kodekommentarer, XML-docs og testnavne er på engelsk** i hele repoet — læs `AdoStateRoles.cs` som
det arbejdende eksempel. Dansk er kun brugervendte strenge (`da.json`), `docs/plans/` og `CLAUDE.md`.

**Kodestumperne i denne plan har danske kommentarer**, fordi planen læses på dansk. **Oversæt dem til
engelsk når du skriver koden.** Formen der skal efterlignes, er husets: kommentaren siger *hvorfor* og
hvad der blev **målt**, ikke hvad linjen gør — og den siger hvilken vej gættet falder, når begrundelsen
mangler. Kopiér ikke kommentarerne ordret; skriv dem om, så de siger det samme på engelsk.

**Og en begrundelse må ikke hvile på et ben ingen har målt.** Task 1's to gennemgange fandt den fejl
**tre gange i én opgave**, hver gang med en konklusion der var rigtig og et argument der ikke var: en
test-doc påstod at søskenden "består uanset hvad" (sletning fælder begge); en kodekommentar påstod at
operandrækkefølgen i et `||` var bærende (den er usynlig for hver test); og en theory begrundede sig
med at sammensatte ADO-tilstandsnavne er almindelige (de tolv målte var enkeltord, og navnene i
eksemplet er `System.Reason`-værdier). Hver gang lå det holdbare argument lige ved siden af — for den
sidste: `<>` sammenligner hele strenge, hvilket gælder uanset hvad instansen indeholder.

**Reglen der følger:** når du skriver en begrundelse, så spørg om den hviler på noget der kan rådne —
et tal, en instans' indhold, en påstand om hvad der er "almindeligt". Kan den skrives på operator- eller
typesemantik i stedet, så gør det. Og skriv **umålt** hvor noget er umålt; det er stærkere end en
formulering der lyder målt.

**Og en mutationsmåling skrives som *identiteter*, aldrig som et antal.** Task 2 fandt den skarpeste
udgave af fælden: en kommentar skrev "denne mutation gør præcis én test rød" — og **samme commit
tilføjede den anden test der fælder præcis den mutation.** Tallet var sandt i det øjeblik det blev
målt og falsk da commit'en landede. Ingen mutation kunne afsløre det, fordi påstanden handler om
suitens sammensætning frem for om koden. Skriv derfor *hvilke* tests der fældes og hvorfor —
"fælder de tests der spørger om en ikke-tom nøgleliste, men ikke denne, fordi vagten returnerer
foran den" — så en femte test ikke kan gøre kommentaren forkert. Samme familie som CLAUDE.md's
regel om tal der forældes, men et niveau dybere: her rådner tallet af *dit eget arbejde i samme
commit*. **Til Task 12:** den lektion hører i `CLAUDE.md`.

## Tre rettelser til designet, fundet før planen blev skrevet

**Designfilen er allerede rettet** i samme commit som denne plan, så der er ingen Task 0 for dem.
De står her frem for at være slettet, fordi planfilerne er repoets hukommelse og fordi den første af
dem ville have gjort halvdelen af funktionen tavst virkningsløs.

1. **`AdoClosureRow` og `JiraClosureRow` skal bære `reason`.** `CloseAsync`s første vagt er
   `AdoStateRoles.For(closure.State, settings) != AdoStateRole.Done` → spring over
   (`AdoEndpoints.cs:419`). En sag der lukkes fordi den er **tildelt en anden**, har en tilstand som
   `Active`, der ikke står i færdig-listen — så hver eneste `unassigned`-lukning ville være blevet
   sprunget over og talt i `skipped`. Uden rettelsen ville funktionen have set færdig ud og gjort
   ingenting.
2. **`workItemType` bliver nullable sammen med `state`.** Designet nævnte kun `state`. ADO-rækken har
   to felter der kun kan udfyldes af et svar fra kilden, og skabelonens egen kommentar holder op med
   at være sand: *"Ingen @if: typen er altid udfyldt"* (`ado-import.html:97`).
3. **Nøgleopslaget skal bære titlen.** `ImportedKeysAsync` svarer i dag `Dictionary<string,
   TodoStatus>`. En `missingInSource`-række har ingen titel fra kilden — der kom jo ikke noget svar —
   så den eneste titel der findes, er den lokale opgaves.

---

## Task 0: De tre målinger mod de rigtige instanser

**Denne opgave køres af brugeren, ikke af dig.** Den kan ikke laves mod `FakeAdo`/`FakeJira`, fordi
en falsk server der udsender vores egen antagelse måler sig selv — samme fælde som skive 11's
`DateTimeOffset`-format.

Spørg brugeren om de tre svar, før Task 2 og Task 3 skrives. **Læg ikke et token i en kommandolinje**
— sæt det i `$env:NAVN` først.

1. **ADO: gør `&errorPolicy=omit` det vi tror?** Bed batch-læsningen om en id der ikke findes, med og
   uden flaget, og se om svaret er en fejl for hele forespørgslen eller de øvrige rækker. **Uden**
   flaget er svaret målt til **400** med de ugyldige id'er navngivet (2026-08-20, står i `FakeAdo` og
   i `AdoTaskSource.Explanation`); det er *med* flaget der er umålt. Designet sagde 404 indtil Task 2,
   og det var et gæt skrevet ved siden af et målt tal.
2. **Jira: svarer `issue/{key}` 404 på en slettet sag** — og ikke 400 eller 403? Og fejler
   `key IN (…)` virkelig med 400 på en ukendt nøgle? Den anden halvdel er hele begrundelsen for at
   Jira går pr. nøgle frem for i én søgning.
3. **ADO: er `WHERE [System.Id] IN (…)` gyldig WIQL** på denne server?

**Hvis et svar modsiger designet:** ret designfilens afsnit 4 og 9 **først**, i sin egen commit, og
ret så denne plan. Antag ikke at koden kan bøjes udenom.

**Hvis brugeren ikke kan svare nu:** kør alligevel videre, men skriv i Task 2 og Task 3's commits at
grenen står umålt mod den rigtige instans, og læg det i `docs/HANDOFF.md`s liste over målinger kun
brugeren kan lave. Det er den ærlige form; en umålt gren der ikke er skrevet ned, er den dyre.

---

## Task 1: `Closed` er altid færdig for ADO

Den mindste af opgaverne og den eneste der ikke afhænger af noget. Reglen står i dag underforstået i
`BaseQuery`s `<> 'Closed'`; den flytter op til det ene sted rollen afgøres.

**Filer:**
- Ret: `src/Todo.Core/Ado/AdoStateRoles.cs`
- Test: `tests/Todo.Core.Tests/Ado/AdoStateRolesTests.cs`

**Trin 1 — skriv de fejlende tests.** To påstande, og den anden er den der gør den første ærlig: en
tom færdig-liste skal **stadig** give `Done` for `Closed`, ellers måler man kun at navnet blev lagt i
listen et sted.

```csharp
[Fact]
public void Closed_is_done_without_being_in_the_done_list()
{
    var settings = SettingsWith(doneStates: []);

    Assert.Equal(AdoStateRole.Done, AdoStateRoles.For("Closed", settings));
}

[Fact]
public void Closed_is_done_even_when_the_user_called_it_waiting()
{
    // Den ene kombination der kan skelne "Closed er hardcodet før alt andet" fra
    // "Closed blev lagt i færdig-listen": står navnet i ventelisten, og vinder færdig
    // alligevel, ligger reglen i den rigtige gren.
    var settings = SettingsWith(doneStates: [], waitingStates: ["Closed"]);

    Assert.Equal(AdoStateRole.Done, AdoStateRoles.For("Closed", settings));
}

[Fact]
public void The_comparison_is_ordinal_for_the_built_in_name_too()
{
    // Resten af klassen sammenligner ordinalt, og en indbygget undtagelse der pludselig
    // var versalufølsom ville være en regel mere at huske.
    var settings = SettingsWith(doneStates: []);

    Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("closed", settings));
}
```

**Trin 2 — kør dem og se dem fejle.**

```
dotnet test tests\Todo.Core.Tests --filter FullyQualifiedName~AdoStateRolesTests
```

Forventet: de to første fejler med `Actionable` hvor `Done` var ventet. Den tredje **består
allerede** — den er en vagt mod den rettelse du er ved at lave, ikke mod nutiden.

**Trin 3 — implementér.** I `AdoStateRoles.For`, i den gren der allerede svarer `Done`:

```csharp
// Closed er færdig uden at stå i nogen liste, og det er ikke en bekvemmelighed. Vælgeren
// kan aldrig tilbyde navnet: FetchStatusNamesAsync bygger på BaseQuery, som indeholder
// <> 'Closed', og settings.ts' stateOptions() lægger kun allerede valgte navne tilbage - så
// navnet kan ikke vælges, før det er valgt. Uden den her linje læser afstemningen et lukket
// work item som Actionable, altså "stadig åben", stik modsat sandheden.
//
// Appen påstår det i forvejen ét sted: BaseQuery udelukker netop Closed, med kommentaren om
// at det er det ene navn alle sagstyper deler. Reglen flytter hermed op til det sted rollen
// afgøres, frem for at blive stående underforstået i en forespørgsel.
if (string.Equals(name, ClosedState, StringComparison.Ordinal)
    || settings.DoneStates.Contains(name, StringComparer.Ordinal))
{
    return AdoStateRole.Done;
}
```

med konstanten på klassen:

```csharp
/// <summary>
/// Den ene tilstand ADO-vælgeren aldrig kan tilbyde. Se grenen i <see cref="For"/> for hvorfor
/// den er hardcodet frem for en indstilling.
/// </summary>
private const string ClosedState = "Closed";
```

**Trin 4 — kør igen.** Alle tre grønne.

**Trin 5 — commit.**

```
git add src/Todo.Core/Ado/AdoStateRoles.cs tests/Todo.Core.Tests/Ado/AdoStateRolesTests.cs
git commit -m "✨ ADO-tilstanden Closed er færdig uden at kunne vælges, fordi vælgeren aldrig kan tilbyde den"
```

---

## Task 2: `ReconcileAsync` på `ITaskSource`, og ADO's halvdel

**Filer:**
- Opret: `src/Todo.Core/Sources/ExternalReconciliation.cs`
- Ret: `src/Todo.Core/Sources/ITaskSource.cs`
- Ret: `src/Todo.Host/Ado/AdoTaskSource.cs`
- Ret: `tests/Todo.TestSupport/Ado/FakeAdo.cs`
- Test: `tests/Todo.Api.Tests/AdoTaskSourceTests.cs`

**Trin 1 — typen først**, fordi begge kilder og begge endpoints hænger på den:

```csharp
namespace Todo.Core.Sources;

/// <summary>
/// Hvad kilden svarer om de nøgler appen allerede har importeret.
///
/// To mængder frem for et flag pr. række, og det er hele grunden til at ejerskabet aldrig bliver en
/// navnesammenligning: begge kilder kan svare på "er den stadig min" med deres eget begreb - @Me og
/// currentUser() - så afgørelsen er serverens. Jiras identitet er i øvrigt kun et DisplayName
/// (MyselfBody binder intet andet), og en sammenligning på det ville knække den dag nogen retter et
/// efternavn.
///
/// Aflæsningen er dermed: en nøgle i <see cref="Found"/> men ikke i <see cref="StillMine"/> er
/// tildelt en anden. En nøgle i ingen af dem kender kilden ikke - slettet, flyttet, eller uden for
/// tokenets rettigheder.
/// </summary>
public sealed record ExternalReconciliation(
    IReadOnlyList<ExternalTask> Found,
    IReadOnlySet<string> StillMine);
```

Og metoden på `ITaskSource`, med hele begrundelsen ved den:

```csharp
/// <summary>
/// Hvad kilden siger om nøgler appen allerede har. Kaldes kun med de nøgler der er åbne lokalt og
/// <em>manglede</em> i FetchAssignedAsync' svar, fordi de øvrige allerede har en række - så mængden
/// er afgrænset af brugerens arbejdsliste og er typisk en håndfuld.
///
/// Den findes fordi begge kilders hentning filtrerer på tildeling: bliver en sag tildelt en anden,
/// falder den ud af svaret og appen kan ikke se forskel på "intet er sket" og "den er ikke min
/// længere". Fravær kan ikke bruges som signal - skifter brugeren projektnøgle eller sagstypefilter,
/// ser hver eneste opgave forsvundet ud på én gang.
///
/// En nøgle kilden ikke kender skal svares som et fravær i begge mængder, ikke som en undtagelse:
/// den slettede sag er et af de udfald funktionen findes for at fortælle om.
/// </summary>
Task<ExternalReconciliation> ReconcileAsync(
    IReadOnlyList<string> keys, CancellationToken ct = default);
```

**Mellemtilstanden mellem Task 2 og Task 3 skal være larmende, ikke tavs.** `ITaskSource` har **to**
implementeringer — `AdoTaskSource` og `JiraTaskSource` — så metoden på grænsefladen brækker Jira indtil
Task 3. Giv derfor `JiraTaskSource.ReconcileAsync` en `throw new NotSupportedException` med Task 3
navngivet i beskeden, **plus en test der påstår at den kaster**. Ikke en tom `ExternalReconciliation`,
som ville betyde "Jira har intet at afstemme" og gøre hele Jira-halvdelen tavst virkningsløs — samme
klasse af fejl som rettelse 1 øverst. Testen er vagten: sker Task 3 aldrig, står den som dokumentation
frem for som et hul, og **Task 3 sletter den som en synlig handling**.

**Trin 2 — udvid `FakeAdo` først**, ellers kan testen ikke skrives. Den skal kunne to nye ting:
kende et work item der **ikke** er tildelt brugeren, og svare på en batch-læsning med en id den ikke
har. Sidstnævnte er den vigtigste — uden den kan hullet i `errorPolicy` ikke skrives ned.

- Læg et work item ind som `@Me`-WIQL'en **ikke** svarer med, men som batch-læsningen kender.
- Lad `MapGet("/{collection}/_apis/wit/workitems")` læse `errorPolicy` fra query'en: mangler den, svar
  **400** med de ugyldige id'er navngivet, når en id er ukendt — det er den målte form, ikke 404; er
  flaget `omit`, udelad rækken og svar 200 med resten.
- Gem den seneste WIQL i `WiqlRequests`, som allerede findes, så testen kan påstå at `IN`-formen og
  `@Me` faktisk gik ud.

**Trin 3 — skriv de fejlende tests.**

```csharp
[Fact]
public async Task Reconciling_finds_a_work_item_that_is_no_longer_assigned_to_me()
{
    await using var fake = await FakeAdo.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([FakeAdo.HandedOverKey, FakeAdo.MineKey]);

    // Begge findes - batch-læsning efter id er uafhængig af tildeling - men kun den ene er min.
    Assert.Equal(2, answer.Found.Count);
    Assert.Contains(FakeAdo.MineKey, answer.StillMine);
    Assert.DoesNotContain(FakeAdo.HandedOverKey, answer.StillMine);
}

[Fact]
public async Task Reconciling_finds_a_closed_work_item_the_fetch_cannot_see()
{
    await using var fake = await FakeAdo.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([FakeAdo.ClosedKey]);

    var found = Assert.Single(answer.Found);
    Assert.Equal("Closed", found.StatusName);
}

[Fact]
public async Task An_unknown_key_is_an_absence_rather_than_a_failure()
{
    // Den slettede sag er et af de fire udfald, ikke en fejl. Uden errorPolicy=omit svarer ADO
    // 400 for HELE batch-læsningen, så én slettet sag ville vælte hver forhåndsvisning.
    await using var fake = await FakeAdo.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([FakeAdo.MineKey, "999999"]);

    Assert.Single(answer.Found);
    Assert.DoesNotContain("999999", answer.StillMine);
}

[Fact]
public async Task The_ownership_question_is_asked_with_At_Me_rather_than_a_name()
{
    await using var fake = await FakeAdo.StartAsync();
    var source = fake.SourceFor();

    await source.ReconcileAsync([FakeAdo.MineKey]);

    var wiql = Assert.Single(fake.WiqlRequests);
    Assert.Contains("@Me", wiql, StringComparison.Ordinal);
    Assert.Contains("[System.Id] IN", wiql, StringComparison.Ordinal);
}

[Fact]
public async Task No_keys_is_no_request()
{
    // Den tomme mængde er den normale tilstand: er alt hentet, er der intet at afstemme.
    await using var fake = await FakeAdo.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([]);

    Assert.Empty(answer.Found);
    Assert.Empty(fake.WiqlRequests);
    Assert.Empty(fake.BatchRequests);
}
```

**Trin 4 — kør dem og se dem fejle** med at metoden ikke findes:

```
dotnet test tests\Todo.Api.Tests --filter FullyQualifiedName~AdoTaskSourceTests
```

**Trin 5 — implementér i `AdoTaskSource`.** Genbrug `QueryIdsAsync` og `HydrateAsync`; klump med den
eksisterende `BatchSize`. `ItemFields` skal **ikke** udvides — se kommentaren:

```csharp
public async Task<ExternalReconciliation> ReconcileAsync(
    IReadOnlyList<string> keys, CancellationToken ct = default)
{
    if (keys.Count == 0)
    {
        return new ExternalReconciliation([], new HashSet<string>(StringComparer.Ordinal));
    }

    var settings = await _settings(ct);

    // Nøglerne er work item id'er som tekst. En nøgle der ikke er et tal kan ikke være vores egen
    // - importen skriver kun id'er - så den frasorteres frem for at få WIQL'en til at fejle.
    var ids = keys
        .Select(key => long.TryParse(key, out var id) ? id : (long?)null)
        .Where(id => id is not null)
        .Select(id => id!.Value)
        .ToList();

    // System.AssignedTo bliver bevidst IKKE lagt i ItemFields. Feltets wire-form er umålt, og
    // System.CreatedBy står som en JsonElement af netop den grund - en identitet mere at binde i
    // blinde er en pris afstemningen ikke behøver at betale, fordi WIQL'ens @Me svarer på
    // spørgsmålet uden at nogen skal fortolke et navn.
    var mine = new HashSet<string>(StringComparer.Ordinal);
    var found = new List<ExternalTask>();

    foreach (var chunk in ids.Chunk(BatchSize))
    {
        var literal = string.Join(", ", chunk);

        var stillMine = await QueryIdsAsync(
            settings,
            $"SELECT [System.Id] FROM WorkItems WHERE [System.Id] IN ({literal}) "
                + "AND [System.AssignedTo] = @Me",
            ct);

        foreach (var id in stillMine)
        {
            mine.Add(id.ToString(CultureInfo.InvariantCulture));
        }

        // Deadline er null her, ikke afledt. En afstemt række bliver aldrig importeret som ny
        // opgave, så en foreslået deadline ville være støj på en række hvis flueben betyder
        // "luk den her".
        found.AddRange(
            (await HydrateAsync(settings, [.. chunk], ItemFields, ct, omitMissing: true))
                .Select(item => Map(item, deadline: null)));
    }

    return new ExternalReconciliation(found, mine);
}
```

`HydrateAsync` får en `bool omitMissing = false`-parameter, som lægger `&errorPolicy=omit` på
query'en. **Standardværdien er `false`**, så hentningen er byte for byte den forespørgsel den altid
har været — og den nye adfærd er begrænset til den ene kalder der har brug for den.

**Trin 6 — kør igen.** Alle fem grønne.

**Trin 7 — se vagten fælde.** Fjern `omitMissing: true` fra kaldet og kør
`An_unknown_key_is_an_absence_rather_than_a_failure` alene. Forventet: den fejler med en
`SourceException` fra 400'eren, med de ugyldige id'er navngivet. Sæt tilbage.

**Trin 8 — commit.**

```
git add src/Todo.Core/Sources src/Todo.Host/Ado tests/Todo.TestSupport/Ado tests/Todo.Api.Tests/AdoTaskSourceTests.cs
git commit -m "✨ ADO kan afstemme de nøgler appen allerede har, og en slettet sag er et fravær frem for en fejl"
```

---

## Task 3: Jiras halvdel — ét kald pr. nøgle, og derefter ét til

**Filer:**
- Ret: `src/Todo.Host/Jira/JiraTaskSource.cs`
- Ret: `tests/Todo.TestSupport/Jira/FakeJira.cs`
- Test: `tests/Todo.Api.Tests/JiraTaskSourceTests.cs`

**Læs først:** designets afsnit 4. Formen er **ikke** ADO's, og forskellen er den slettede sag —
Jira svarer 400 på en ukendt nøgle i `key IN (…)`, så den elegante ene-søgning ville tage hele
afstemningen med sig præcis når den skal fortælle om en slettet sag.

**Og læs `ITaskSource.ReconcileAsync`s dok, som Task 2 skrev til dig.** To fælder fandtes der:

- **Mængderne svarer med *kalderens* stavemåde, ikke kildens.** Jira kanoniserer nøgler — `saas-1`
  bliver `SAAS-1` i svaret — så bygger du `StillMine` af svarets nøgler, fejler kalderens `Contains`
  **tavst**, og hver afstemt sag ser ud som om kilden ikke kender den. Altså det udfald der foreslår
  en lukning. ADO har samme klasse gennem `long`-parsningen (`"0016901"` → `"16901"`) og løste den
  samme vej.
- **`Deadline` skal være `null` på en afstemt række.** ADO sætter den bevidst; Jiras `Map` fylder den
  fra `duedate` i én linje, og den genbruges naturligt. Gør du ingenting, bliver `Found` uden deadline
  fra ADO og med fra Jira — en asymmetri ingen test i dag kan se.

**Trin 1 — udvid `FakeJira`:** en sag der ikke er tildelt brugeren, en sag hvis nøgle giver **404**,
og en `/search` der forstår `key IN (…) AND assignee = currentUser()`.

**Trin 2 — skriv de fejlende tests**, samme fem påstande som ADO's, plus den ene der er Jiras egen:

```csharp
[Fact]
public async Task A_deleted_issue_is_asked_for_by_key_and_answers_404()
{
    await using var fake = await FakeJira.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([FakeJira.MineKey, FakeJira.DeletedKey]);

    Assert.Single(answer.Found);
    Assert.DoesNotContain(FakeJira.DeletedKey, answer.StillMine);
}

[Fact]
public async Task The_ownership_search_never_names_a_key_that_was_not_found()
{
    // Hele begrundelsen for formen: nævner JQL'en en ukendt nøgle, svarer Jira 400 og
    // afstemningen dør på det tilfælde den findes for.
    await using var fake = await FakeJira.StartAsync();
    var source = fake.SourceFor();

    await source.ReconcileAsync([FakeJira.MineKey, FakeJira.DeletedKey]);

    var jql = Assert.Single(fake.SearchRequests);
    Assert.Contains(FakeJira.MineKey, jql, StringComparison.Ordinal);
    Assert.DoesNotContain(FakeJira.DeletedKey, jql, StringComparison.Ordinal);
}

[Fact]
public async Task Nothing_found_asks_no_ownership_question()
{
    // Randen: findes ingen af nøglerne, ville en JQL med en tom IN-liste være ugyldig.
    await using var fake = await FakeJira.StartAsync();
    var source = fake.SourceFor();

    var answer = await source.ReconcileAsync([FakeJira.DeletedKey]);

    Assert.Empty(answer.Found);
    Assert.Empty(fake.SearchRequests);
}
```

**Trin 3 — kør og se dem fejle.**

**Trin 4 — implementér.** `GET issue/{key}` pr. nøgle med `fields=status`, hvor **404 er svaret**
"kender den ikke" og alt andet stadig kaster; derefter **én** `/search` med
`key IN (<dem der findes>) AND assignee = currentUser()`. Genbrug `Map` til `ExternalTask`, så en
afstemt Jira-række er den samme type som en hentet.

Den ene ting der er ny mekanik: `GetAsync` skal kunne **skelne 404 fra alt andet** frem for at kaste
ens. Læg en `GetOrNullAsync` ved siden af, som svarer `null` på 404 og ellers kalder videre — ikke et
flag på den eksisterende, hvor en fremtidig kalder kunne komme til at sluge en 404 der betød noget.

**Trin 5 — kør igen, alle grønne.**

**Trin 6 — se vagten fælde.** Skriv midlertidigt JQL'en om, så den nævner **alle** nøgler frem for
kun de fundne, og lad `FakeJira` svare 400 når en ukendt nøgle står i en `key IN`. Forventet:
`The_ownership_search_never_names_a_key_that_was_not_found` og
`A_deleted_issue_is_asked_for_by_key_and_answers_404` fejler sammen. Sæt tilbage.

**Trin 7 — commit.**

```
git add src/Todo.Host/Jira tests/Todo.TestSupport/Jira tests/Todo.Api.Tests/JiraTaskSourceTests.cs
git commit -m "✨ Jira afstemmer pr. nøgle, fordi key IN fejler på præcis den slettede sag funktionen findes for"
```

---

## Task 4: Kontrakten, de fire fejlkoder og oversættelserne

**Filer:**
- Ret: `contracts/openapi.yaml`
- Ret: `src/Todo.Core/Errors/ErrorCodes.cs`
- Ret: `src/Todo.Web/src/assets/i18n/da.json`, `src/Todo.Web/src/assets/i18n/en.json`
- Genereret: `src/Todo.Web/src/app/api/todo-client.ts`

**Trin 1 — ret kontrakten.** På `AdoPreviewRow` og `JiraPreviewRow`:

- `state` / `status`: fjern fra `required`, sæt `nullable: true`.
- `workItemType` (kun ADO): fjern fra `required`, sæt `nullable: true`.
- `closureReason`: `enum: [resolved, unassigned]`, `nullable: true`.
- `suggestsReopening`: `boolean`, **required**.
- `reopenTo`: `enum: [open, waitingFor]`, `nullable: true`.

På `AdoClosureRow` / `JiraClosureRow`: `reason` med samme enum, **required**. På
`AdoImportRequest` / `JiraImportRequest`: `reopenings`, en liste af `{ key, state, waitingSince }`,
valgfri. På `AdoImportResponse` / `JiraImportResponse`: `reopened`, required.

Hvert nyt felt får en `description` der siger **hvorfor**, ikke hvad — det er husets form. For
`closureReason` skal beskrivelsen sige at serverens gen-beslutning er svagere for `unassigned`, og
hvorfor: ejerskabet er et faktum fra kilden, og importen ringer ikke ud.

**Trin 2 — de fire koder** i `ErrorCodes.cs`, hver med sin `<summary>`:

```csharp
public const string AdoOutsideFilter = "ado.outsideFilter";
public const string AdoMissingInSource = "ado.missingInSource";
public const string JiraOutsideFilter = "jira.outsideFilter";
public const string JiraMissingInSource = "jira.missingInSource";
```

**Trin 3 — oversættelserne.** Fire `errors.*`-nøgler i **begge** sprogfiler, plus mærkaterne til
skærmen: `ado.suggestsClosingResolved`, `ado.suggestsClosingUnassigned`, `ado.suggestsReopening`,
`ado.reopenToOpen`, `ado.reopenToWaiting`, `ado.noState`, `ado.noType` og Jiras modparter.

**Ingen `{{value}}` i en `errors.*`-nøgle.** `api-error-message.ts` kalder `translate(key)` uden
params, så en pladsholder når brugeren urenderet. Ingen test kan se det — paritetstesten
sammenligner nøglemængder, og `ErrorCodeTranslationTests` kræver kun at nøglen findes.

**Trin 4 — generér og kør vagterne.**

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
dotnet test Todo.sln --filter "FullyQualifiedName~ContractDrift|FullyQualifiedName~GeneratedCodeFreshness|FullyQualifiedName~ErrorCodeTranslation"
```

**Trin 5 — forvent rødt i frontenden nu**, og det er meningen: `state`/`status`/`workItemType` er
blevet nullable, så `ado-store.ts:139`, `:157`, `ado-import.html:103` og Jiras tre tvillinger skal
navngives af compileren. **Det er den eneste ting compileren kan se i hele leverancen** — de seks
håndskrevne wire-fixtures er usynlige for den. Lad rødt stå til Task 8 og 9; commit ikke en halv
frontend.

**Trin 6 — commit** kontrakten, koderne, oversættelserne og den genererede klient sammen. De hører
sammen, og en generering committet for sig er en commit der ikke bygger.

```
git commit -m "✨ Kontrakten bærer afstemningens felter, og tilstanden er nullable fordi en slettet sag ikke har en"
```

---

## Task 5: Genåbningsforslaget — den halvdel der ingen afstemning kræver

Denne opgave afhænger **kun** af Task 4. Den er den billigste brugervendte gevinst i leverancen, og
den måler noget helt andet end Task 6, så den kommer først.

**Filer:**
- Ret: `src/Todo.Host/Endpoints/AdoEndpoints.cs`, `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Test: `tests/Todo.Api.Tests/AdoEndpointsTests.cs`, `tests/Todo.Api.Tests/JiraEndpointsTests.cs`

**Trin 1 — skriv de fejlende tests.** Tre fakta, hvert negeret for sig:

```csharp
[Fact]
public async Task A_task_finished_here_but_open_in_the_source_suggests_reopening()

[Fact]
public async Task A_task_that_is_not_finished_here_suggests_nothing()

[Fact]
public async Task A_task_finished_in_both_places_suggests_nothing()

[Fact]
public async Task A_waiting_state_reopens_to_waiting_rather_than_open()

[Fact]
public async Task A_waiting_state_reopens_even_when_including_waiting_is_off()
```

Den sidste er den der bærer en beslutning: kontakten styrer hvad der kommer **ind** som nyt arbejde,
ikke hvad en opgave du allerede ejer får lov at hedde. Den kan **kun** fejle hvis nogen skriver
`settings.IncludeWaiting` ind i genåbningens betingelse, hvilket er det nærliggende gæt.

**Trin 2 — kør og se dem fejle.**

**Trin 3 — implementér** i begge previews' løkke, ved siden af `suggestsClosing`:

```csharp
// Spejlvendt af suggestsClosing, og på samme tre fakta: rollen, den lokale status, og at de
// to er uenige. Ingen afstemning nødvendig - er sagen stadig tildelt dig og uafsluttet,
// opfylder den hentningens egne betingelser og står allerede i svaret.
var suggestsReopening = role != AdoStateRole.Done
    && localStatus is CoreStatus.Done;
```

og `reopenTo` af den samme rolle importløkken bruger — `Waiting → waitingFor`, alt andet `open`.
**Ikke** en ny mapning; udtræk den fælles så importen og forslaget ikke kan drive fra hinanden.

**Trin 4 — kør igen, alle grønne.**

**Trin 5 — se en vagt fælde.** Skriv `role != Done` om til `role == Actionable` og kør: forventet
er at `A_waiting_state_reopens_to_waiting_rather_than_open` fejler alene. Sæt tilbage.

**Trin 6 — commit.**

```
git commit -m "✨ Forhåndsvisningen foreslår at genåbne en opgave kilden ikke er færdig med"
```

---

## Task 6: Afstemningsrækkerne i forhåndsvisningen

**Filer:**
- Ret: `src/Todo.Host/Endpoints/AdoEndpoints.cs`, `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Test: `tests/Todo.Api.Tests/AdoEndpointsTests.cs`, `tests/Todo.Api.Tests/JiraEndpointsTests.cs`

**Trin 1 — udvid nøgleopslaget først.** `ImportedKeysAsync` skal svare nøgle → (status, titel), og
den skal hente **alle** nøgler for kilden frem for kun de hentede — forhåndsvisningen skal jo kunne
se hvad der *manglede*. Importens kalder bruger samme metode; den ekstra mængde er hele appens
importerede opgaver, hvilket for en personlig liste er trivielt. Skriv den afvejning ned ved metoden.

**Trin 2 — skriv de fejlende tests**, ét udfald pr. test:

```csharp
[Fact]
public async Task A_key_that_fell_out_of_the_fetch_and_is_done_in_the_source_suggests_closing()

[Fact]
public async Task A_key_that_is_no_longer_assigned_to_me_suggests_closing_for_that_reason()

[Fact]
public async Task A_key_that_is_still_mine_and_open_is_shown_as_outside_the_filter()

[Fact]
public async Task A_key_the_source_does_not_know_is_shown_as_missing_and_carries_the_local_title()

[Fact]
public async Task A_task_that_is_done_here_is_never_reconciled()

[Fact]
public async Task Resolved_outranks_unassigned_when_both_are_true()
```

Den sidste er vagten på rækkefølgen i designets afsnit 5, og den er den eneste der kan se en
ombytning: begge udfald er lovlige, så en ombytning compilerer og læser rigtigt.
`A_task_that_is_done_here_is_never_reconciled` er den der holder mængden nede — uden den kan nogen
komme til at afstemme arkivet uden at nogen test siger fra.

**Og så den sikkerhedsregel Task 3 fandt frem, som er hele funktionens net.** Designets afsnit 5
bærer nu: **svarer hver nøgle med et fravær, og blev der spurgt om mere end én, foreslås ingenting** —
i stedet en linje om at kilden ikke genkendte noget. Grunden er at **Jira DC skjuler en sag du ikke må
browse bag `404 Issue Does Not Exist` frem for bag en 403**, så en mistet projektrettighed ellers
læses som at hele din liste er slettet, hvorefter appen tilbyder at lukke alt. ADO har samme klasse
gennem et token der ikke må læse projektet. To tests mere, og de er begge nemme at skrive svage:

```csharp
[Fact]
public async Task Every_key_missing_at_once_suggests_nothing_because_it_is_more_likely_a_permission()

[Fact]
public async Task A_single_missing_key_is_still_suggested_because_all_and_one_are_the_same_there()
```

Den anden er tanden i den første: uden den kan grænsen skrives som "≥ 1 fravær" og bestå, hvilket
ville slå den ægte slettede sag ihjel — altså det udfald funktionen findes for.

**Trin 3 — kør og se dem fejle.**

**Trin 4 — implementér.** Efter hentningens løkke: find de lokale nøgler der ikke er `Done` og ikke
kom med, kald `ReconcileAsync`, og byg en række pr. nøgle efter designets tabel i afsnit 5.
`Deadline` er `null` på alle fire; `Excluded` bærer koden for de to uforklarede.

**Trin 5 — kør igen, alle grønne.**

**Trin 6 — se vagten fælde.** Byt de to lukkegrene om, så `unassigned` afgøres først, og kør:
forventet er at `Resolved_outranks_unassigned_when_both_are_true` fejler alene. Sæt tilbage.

**Trin 7 — commit.**

```
git commit -m "✨ Forhåndsvisningen afstemmer det der faldt ud af hentningen, og siger hvad den ikke kunne forklare"
```

---

## Task 7: Importen tager imod en genåbning og en unassigned-lukning

**Filer:**
- Ret: `src/Todo.Host/Endpoints/AdoEndpoints.cs`, `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Test: `tests/Todo.Api.Tests/AdoEndpointsTests.cs`, `tests/Todo.Api.Tests/JiraEndpointsTests.cs`

**Læs først:** rettelse 1 øverst i denne plan. `CloseAsync`s vagt springer i dag hver
`unassigned`-lukning over.

**Trin 1 — skriv de fejlende tests.**

```csharp
[Fact]
public async Task Closing_a_work_item_that_was_handed_over_does_not_need_a_done_state()

[Fact]
public async Task A_handed_over_closure_takes_its_completion_time_from_the_clock()

[Fact]
public async Task Reopening_puts_the_status_back_and_clears_the_completion_time()

[Fact]
public async Task Reopening_refuses_a_task_that_is_not_done_here()

[Fact]
public async Task A_reopening_whose_state_is_now_done_is_skipped_and_not_counted()
```

**Tanden er `Reopening_puts_the_status_back_and_clears_the_completion_time`.** Så opgaven fuldført
med et `CompletedAt` der ligger **uger** fra `FixedClock` — et fixture hvor feltet allerede er `null`
ville bestå med en implementering der ikke rydder noget. Påstanden er på `null` bagefter. Spejlvendt
af forgængerens tand, som var på den nøjagtige værdi.

**Trin 2 — kør og se dem fejle.** Forvent at de to første fejler med `closed: 0` og en uændret
opgave; det er rettelse 1 der viser sig.

**Trin 3 — implementér.** `CloseAsync`s vagt bliver betinget af `reason`:

```csharp
// Gen-beslutningen er svagere for unassigned, og det er en pris frem for en forglemmelse:
// ejerskabet er et faktum fra kilden, og importen ringer bevidst ikke ud - samme form som
// doneAt og waitingSince allerede har. Tilbage er de tre vi kan kræve: at opgaven findes, at
// den hører til kilden, og at den ikke allerede er færdig.
var stateSaysDone = AdoStateRoles.For(closure.State, settings) == AdoStateRole.Done;
if ((closure.Reason == AdoClosureReason.Resolved && !stateSaysDone)
    || !tasks.TryGetValue(closure.Key, out var task)
    || task.Status == CoreStatus.Done)
```

og `ReopenAsync` ved siden af, symmetrisk med `CloseAsync`: rollen skal **ikke** være `Done`, opgaven
skal findes, høre til kilden og **være** færdig her. Derefter `Status` fra rollen, `CompletedAt = null`
og `WaitingSince` fra rækken når rollen er `Waiting`.

**Trin 4 — kør igen, alle grønne. Kør derefter hele `dotnet test Todo.sln`** — det er første gang
begge endpoints er færdige, og forgængerens lukketests skal stadig være grønne.

**Trin 5 — commit.**

```
git commit -m "✨ Importen genåbner og lukker en overdraget sag, og siger hvorfor gen-beslutningen er svagere den ene vej"
```

---

## Task 8: Storen — tre mængder og tre tal

**Filer:**
- Ret: `src/Todo.Web/src/app/ado/ado-store.ts`, `src/Todo.Web/src/app/ado/ado-import.ts`
- Ret: `src/Todo.Web/src/app/jira/jira-store.ts`, `src/Todo.Web/src/app/jira/jira-import.ts`
- Test: `src/Todo.Web/src/app/ado/ado-store.spec.ts`, `.../jira-store.spec.ts` og de to
  `*-import.spec.ts`

**Trin 1 — skriv de fejlende tests:** at `reopenable()` kun tager rækker med
`suggestsReopening`, at `isBlocked` er falsk for alle tre mængder, at `selectedKeys` starter som
foreningen af de tre, at en uforklaret række er blokeret, og at `import()` deler valget i **tre**
lister på wiren.

**Bemærk hvad der er usynligt her.** `ado-store.spec.ts` fodrer både et `flush(...)` (umålt af
compileren) og et `new AdoPreviewRow(...)` (målt). Læg de nye felter **begge** steder, og husk
`jira-store.spec.ts`, `jira-import.spec.ts` og `ado-import.spec.ts` — seks håndskrevne wire-fixtures
i alt, hvoraf ingen afstemmes mod kontrakten.

**Trin 2 — kør og se dem fejle:**

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false --include src/app/ado/ado-store.spec.ts
```

**Trin 3 — implementér** `reopenable()` ved siden af `closable()`, den tredje mængde i `isBlocked`,
`selectedReopenings` med samme værn som `selectedClosures` — filtreret gennem butikkens egen liste,
så et flueben tvunget på udefra ikke slipper igennem — og `reopenings` på forespørgslen. Tællingen
bliver tre tal.

**Trin 4 — kør igen, alle grønne.**

**Trin 5 — commit.**

```
git commit -m "✨ Importbutikken kender tre mængder, og knappen siger hvad hver af dem koster"
```

---

## Task 9: Skabelonen — mærkatkæden og de to felter der kan mangle

**Filer:**
- Ret: `src/Todo.Web/src/app/ado/ado-import.html`, `src/Todo.Web/src/app/jira/jira-import.html`

**Trin 1 — mærkatkæden**, i denne rækkefølge og ikke en anden:

```
@if (row.excluded) … @else if (row.suggestsClosing) … @else if (row.suggestsReopening) …
@else if (row.alreadyImported) …
```

`alreadyImported` bliver ved at være sidst, fordi både en lukke- og en genåbningskandidat pr.
definition **også** er importeret tidligere; står den før, nås de to nye grene aldrig. Skriv
begrundelsen i skabelonen som den allerede står ved `suggestsClosing`.

`suggestsClosing` deler sig i to sætninger efter `closureReason`.

**Trin 2 — de to felter der nu kan mangle.** Type- og tilstandslinjen får hver sin `@if` med en
`@else` der siger det højt (`ado.noType`, `ado.noState`). **Ret kommentaren ved typelinjen** — den
siger i dag *"Ingen @if: typen er altid udfyldt"*, og det holder op med at være sandt. En kommentar
der lyver er værre end ingen, og Tailwind udtrækker desuden klassenavne fra kommentarer.

**Trin 3 — byg og kør Vitest samlet.**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

**Trin 4 — commit.**

```
git commit -m "✨ Importrækken siger hvorfor den foreslår hvad den gør, og hvad kilden ikke kunne fortælle"
```

---

## Task 10: Rejsen der måler det ægte

**Filer:**
- Ret: `tests/Todo.E2E/AdoImportJourneyTests.cs`, `tests/Todo.E2E/AdoImportScreen.cs`

**Trin 1 — kør `scripts\build-web.ps1` først.** E2E bygger ikke Angular, så uden den tester
Playwright den forrige frontend uden at noget ser forkert ud.

**Trin 2 — skriv rejsen.** `FakeAdo` er sin egen Kestrel på 127.0.0.1 og `RunningHost` starter appen
i testprocessen, så hele kæden kan køre ægte — **ingen opsnapning**:

1. importér en sag, og påstå at den står på opgavelisten;
2. lad `FakeAdo` **tildele den en anden**;
3. forhåndsvis igen, og påstå at rækken siger "tildelt en anden" og er valgbar;
4. tag imod, og påstå at opgaven er fuldført.

Det er den eneste form der måler at afstemningen faktisk stiller et **andet** spørgsmål end
hentningen — en rutehandler ville måle vores egen antagelse om svaret.

**Bemærk locator-fælden:** rækken *er* en `<label>`, så mærkaten havner i afkrydsningsfeltets
tilgængelige navn, og E2E-suiten matcher navne i deres helhed. `AdoImportScreen`s locators flytter
sig, præcis som de gjorde i forgængeren.

**Trin 3 — kør den, og skriv bredden eksplicit** (`ColumnWidth = 480`), også hvor du tror den er uden
betydning.

**Trin 4 — commit.**

```
git commit -m "✅ En rejse måler at en overdraget sag foreslås lukket, hele vejen gennem den falske server"
```

---

## Task 11: `ContrastTests`' fire nye grene

**Filer:**
- Ret: `tests/Todo.E2E/ContrastTests.cs`

**Trin 1 — læs først** hvad der gør en gren målt: en `@if`-gren er **umålt**, indtil et fixture har en
række i den tilstand og rejsen renderer den. Vagten kan ikke se en farve der aldrig blev malet.

**Trin 2 — udvid rutehandleren.** `The_Ado_screens` og `The_Jira_screens` svarer allerede på
`**/api/ado/preview` med skiftende kroppe. De nye grene kræver en krop mere: en række der lukkes fordi
den er overdraget, en der genåbnes, og de to uforklarede. **Kroppen skal bære felterne** — hænger
mærkaten på `row.closureReason` og sender handleren rækker uden feltet, læser klienten `undefined`,
grenen er falsk, og der males aldrig en farve, mens handleren ser ud til at dække sagen.

**Trin 3 — se vagten fælde.** Mal én af de fire nye mærkater `text-gray-400 dark:text-gray-600` og
kør begge teorier. Forventet: fire fejl pr. tema. **Læs hvilke poster der ændrede sig, ikke antallet**
— og husk at fejllinjen navngiver det element der *bærer teksten*, altså `span`, ikke rækken omkring
det. Sæt tilbage.

**Trin 4 — commit.**

```
git commit -m "✅ Kontrastvagten maler afstemningens fire nye grene i begge temaer"
```

---

## Task 12: Wire-format, dokumentation og tallene

**Filer:**
- Ret: `tests/Todo.Api.Tests/` — wire-format-testen
- Ret: `CLAUDE.md`, `docs/HANDOFF.md`, `README.md`

**Trin 1 — de fire nye enum-værdier.** Drift-testen sammenligner **kun stier og metoder**, så
`resolved`, `unassigned`, `open` og `waitingFor` kan ikke få den til at fejle uanset hvordan de
serialiseres. Læg dem i `Wire_format_uses_the_names_the_contract_declares` med en påstand på den rå
JSON, som `deferred` har det.

**Trin 2 — kør `Check.cmd`** og noter de fire tal.

**Trin 3 — ret tallene i `CLAUDE.md` og `docs/HANDOFF.md`, begge steder.** Regnskabet er drevet fra
virkeligheden **fem** gange, senest hvor de to filer stod på 73 og 75 mens sandheden var 77. Skriv
**hvorfor** hvert tal flyttede sig, og hvis du retter et tal du ikke selv har fået til at flytte sig,
så sig at det var forældet. Et ændret tal kan kun betyde en tabt eller duplikeret test, hvis tallet
var sandt i forvejen.

**Trin 4 — læg lektionerne i `CLAUDE.md`**, kun dem der ikke kan udledes af koden. Kandidaterne fra
denne leverance:

- ADO's tilstandsvælger kan aldrig tilbyde `Closed`, og hvorfor.
- `key IN (…)` fejler på en ukendt Jira-nøgle, og det er derfor de to kilder afstemmer forskelligt.
- `CloseAsync`s vagt og hvorfor gen-beslutningen er svagere for `unassigned`.

Skriv **ikke** noget der forældes, og ikke noget `git log --stat` allerede har.

**Trin 5 — `docs/HANDOFF.md`:** en linje under "Færdigt" med hvad leverancen gav, og de tre
målinger fra Task 0 i listen over det kun brugeren kan svare på — hvis de stadig står ubesvarede.

**Trin 6 — commit.**

```
git commit -m "📝 Afstemningen er leveret, og de fire testtal er målt frem for påstået"
```
