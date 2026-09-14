# En slettet sag kan lukkes fra importen — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit, og hver commit skal bygge.

**Mål:** En afstemt række hvis sag kilden ikke kender, skal kunne krydses af og lukke opgaven — i
stedet for at stå grå med en forklaring og ingen handling. Begge kilder.

**Designet er `docs/plans/2026-09-14-closing-a-deleted-item-design.md`.** Læs det først. Afsnit 1
bærer de to målinger der flyttede mistanken væk fra transporten, afsnit 4 den kode ændringen slår
ihjel, og afsnit 6 den pris der skal blive stående. Forgængeren er
`2026-08-31-source-reconciliation-design.md`, som byggede afstemningen.

**Arkitektur:** Ingen ny datamodel, ingen migrering, intet nyt endpoint, ingen nye kald til
kilderne. Én ny værdi på en delt enum, ét betinget krav i en validator, én gren mere i to
identiske udtryk, og et filter i klienten der får en undtagelse.

**Planen kan være forkert.** Mål frem for at tro på det der står her, og **ret planfilen** når en
måling modsiger den.

## Sproget i koden er engelsk, og planens kodestumper er det ikke

Kodekommentarer, XML-docs og testnavne er engelske i hele repoet; `SourceLanguageTests` er vagten.
Dansk er brugervendte strenge, `docs/plans/` og `CLAUDE.md`. **Oversæt planens kommentarer til
engelsk når du skriver koden** — og kopiér dem ikke ordret, skriv dem om så de siger hvorfor og
hvad der blev målt.

## Maskinens fælder der rammer netop denne plan

- **Kør `scripts\build-web.ps1` før enhver E2E-kørsel.** E2E-projektet bygger ikke Angular, så
  Playwright tester ellers den forrige udgave af frontenden. Gælder også når du **ruller en
  mutation tilbage** — den retning er den farlige, fordi resultatet så ligner et fund.
- **En ny `required`-værdi i kontrakten fanges ikke af typetjekkeren i de seks håndskrevne
  wire-fixtures.** De bygger rå svarkroppe og afstemmes ikke mod kontrakten. Her lægges der ingen
  nye felter på, men `closureReason` skal med i de fixtures der iscenesætter en manglende række.
- **Mutér én betingelse ad gangen**, og kør `git diff --stat` imellem, så grundlinjen kan ses ren.
- **Skriv mutationsmålinger som identiteter, aldrig som antal.** *Hvilke* tests der fældes, ikke
  hvor mange.

## Task 0: Målingerne mod den rigtige instans — KØRT 2026-09-11

**Gjort. Svarene står i designets afsnit 1** og er ført ind i `docs/HANDOFF.md`. Opskriften bevares
her, fordi den skal køres igen efter en serveropgradering. Scriptet læser tokenet ud af en **kopi**
af `%APPDATA%\TodoApp\todo.db`, sletter kopien, og giver curl headeren på **stdin** gennem `-K -`,
så tokenet aldrig når en kommandolinje. **Læg aldrig tokenet i en kommandolinje**, og skriv det
aldrig ud.

De tre spørgsmål, og hvad de svarede:

1. `&errorPolicy=omit` med et id der ikke findes → **200**, `count: 23` af 25, de ukendte udeladt.
2. `WHERE [System.Id] IN (…) AND [System.AssignedTo] = @Me` → **200**.
3. Uden flaget, kontrolmålingen → **404 `TF401232`** for et *slettet* id, ikke 400'en fra
   2026-08-20, som gælder id'er der aldrig har eksisteret.

Jiras tredje præmis — svarer `issue/{key}` 404 eller 403 på en sag du ikke må se — er **stadig
umålt** og bliver det efter denne leverance.

## Task 1: Kontrakten, og importen der tager imod en tilstandsløs lukning

**Hvorfor de to hører sammen i én commit:** gøres `state` nullable alene, holder `CloseAsync` og
`ValidateClosure` op med at compilere. Hver commit skal bygge.

`contracts/openapi.yaml`:

- `ClosureReason` får `missing`. Beskrivelsen skal sige hvorfor navnet ikke er `deleted`: kilden
  kan ikke skelne en slettet sag fra en du har mistet adgangen til, og TFS' egen 404 siger begge
  dele i én sætning.
- `AdoClosureRow.state` og `JiraClosureRow.status` ud af `required`, og `nullable: true`.
  Beskrivelsen skal bære det betingede krav, så den ikke læses som en løsnet regel: feltet er
  påkrævet for enhver anden grund end `missing`, og serveren håndhæver det.

Derefter `powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1`.

`ValidateClosure` i begge endpoints: kravet om tilstanden gælder kun når grunden ikke er `missing`.
`CloseAsync` skal ikke ændre adfærd — dens tilstandstest hører allerede `Resolved` alene til — men
den skal compilere mod et nullable felt.

**Tests (`AdoEndpointsTests`, `JiraEndpointsTests`, én af hver):**

- En lukkerække med `reason: missing` og **ingen** tilstand lukker opgaven.
- En lukkerække med `reason: resolved` og ingen tilstand bliver **stadig** afvist med
  `rowStateRequired`.

**Set fejle:** fjern hele kravet i stedet for at gøre det betinget — så falder den anden af de to,
og kun den. Det er den påstand der gør forskellen mellem *betinget* og *fjernet* målbar.

**Vagt der fanger en glemt generering:** `GeneratedCodeFreshnessTests` hasher `contracts/openapi.yaml`.
Se den fejle ved at rette kontrakten uden at køre generatoren.

## Task 2: Den tredje lukkegrund, og den kode den slår ihjel

`AdoEndpoints.ReconciledRowsAsync` og dens Jira-tvilling får grenen `item is null →
ClosureReason.Missing`, **sidst** i udtrykket. `Excluded`-udtrykket mister sin
`MissingInSource`-gren, som dermed ikke kan nås.

Følg det til dørs frem for at lade koderne stå:

- `ErrorCodes.AdoMissingInSource` og `ErrorCodes.JiraMissingInSource` slettes, og
  `JiraMissingInSource` er krydsrefereret fra to XML-docs i samme fil — ret dem.
- `errors.ado.missingInSource` og `errors.jira.missingInSource` ud af **begge** sprogfiler.
  `ErrorCodeTranslationTests` kræver en nøgle pr. kode og er en `[Theory]` over sprogfiler, så
  tallet flytter sig ikke.
- De **seks** assertions — tre i `AdoEndpointsTests`, tre i `JiraEndpointsTests` — vender fra at
  påstå `Excluded == MissingInSource` til at påstå `SuggestsClosing` og
  `ClosureReason == Missing`.

**Wire-format:** `Assert.Contains("\"closureReason\":\"missing\"", reconciledJson)` i
`TaskEndpointsTests.Wire_format_uses_the_names_the_contract_declares`, hvor de to andre medlemmer
allerede læses af den rå krop. Fixturet skal have en nøgle `FakeJira` ikke kender, **ved siden af
en den kender** — ellers udløser alle-fraværende-grænsen og der kommer ingen rækker at læse.

**Set fejle:** fjern den nye gren igen; tilbuddet forsvinder og de seks vendte assertions falder.

## Task 3: Klienten — muren i `wasAnsweredFor`

`ado-store.ts` og `jira-store.ts`. Lukkegrenens `.filter(wasAnsweredFor)` bliver til et filter der
også lukker en `missing`-række igennem, og `AdoClosureRow`/`JiraClosureRow` sendes med feltet som
det er.

**Kommentaren over `wasAnsweredFor` skal skrives om.** Den påstår i dag at sådan en række *"cannot
reach the mappings below"* — det er præcis den invariant denne leverance bryder, og en begrundelse
der er blevet falsk er værre end ingen. Behold `!= null` og dens måling; det er en anden sag.

**Vitest, én pr. store:** storen sender en lukkerække uden tilstand for en `missing`-række, og
filtrerer den ikke væk.

**Set fejle:** sæt filteret tilbage til `wasAnsweredFor` alene — rækken forsvinder fra kroppen.

## Task 4: Skabelonen og de fire oversættelser

`ado-import.html` og `jira-import.html`: `@else if (row.suggestsClosing && row.closureReason)`-grenen
får sin tredje mærkat. Nøglerne er `ado.suggestsClosingMissing` og `jira.suggestsClosingMissing` i
**begge** sprogfiler — paritetstesten kan ikke se en nøgle der mangler i begge.

Ordlyden på dansk, i de eksisterendes stemme og med den tvetydighed designets beslutning 3 hviler
på: **"Kendes ikke i Azure DevOps — slettet, flyttet eller uden for tokenets rettigheder. Luk
opgaven her."**

## Task 5: Rejsen, én pr. kilde

En E2E der sår en åben opgave med en nøgle kilden ikke kender, henter, krydser rækken af, importerer,
og påstår at opgaven er lukket. **Bredden skal skrives** (`ColumnWidth = 480`), også selvom den ser
ligegyldig ud.

For ADO kan rejsen gå mod den **ægte** `FakeAdo` — dens `UnknownKey` er netop det id — og det er
den stærkere måling, fordi dedup'en, filtret og lukningen så er appens egne. Husk en nøgle mere som
kilden **kender**, ellers udløser alle-fraværende-grænsen.

## Task 6: `ContrastTests`' nye gren

En `@if`-gren er umålt indtil fixturet har en række i den tilstand og rejsen åbner den. Grenene
udelukker hinanden, så der skal stages ét svar pr. gren i rækkefølge — læs klassens konstanter frem
for at gætte tallet. Begge kilder, begge temaer.

**Set fejle:** mal mærkaten `text-gray-400 dark:text-gray-600` og se den falde i begge temaer med
sit eget navn i fejllinjen.

## Task 7: Tallene, dokumentationen og fakets 404

- `Check.cmd`. Sammenlign med **186 / 399 / 83 / 358** fra 2026-09-08, og skriv i `CLAUDE.md`
  hvilke tests der kom til og hvorfor — fordelingen målt på diffen, ikke påstået. Et ændret tal
  uden en forklaring betyder en tabt eller duplikeret test.
- `FakeAdo`s kommentar om at ugyldige id'er svarer 400 skal bære **begge** former nu: 400 for et id
  der aldrig har eksisteret, 404 `TF401232` for et slettet. Faket udsender fortsat 400'en; det er
  kommentarens påstand om enighed der er forkert, ikke koden.
- `docs/HANDOFF.md`: de to ADO-præmisser er besvaret i Task 0 og skal ud af "Målinger kun du kan
  lave" — Jiras tredje bliver stående.
- `CLAUDE.md`: sætningen om at begge afstemningens veje står **umålt** mod de rigtige instanser er
  nu halvt forkert. Ret den, og lad være med at lægge tal til afsnittet.
