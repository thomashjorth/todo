# Åbn sagen fra forhåndsvisningen — besluttet, ikke planlagt endnu

Dette er **kravet og de beslutninger der allerede er truffet**, skrevet ned mens de er friske. Den
egentlige plan med opgaver og kode skrives, når leverancen "vagt-statusser" er færdig (dens Task 6
skriver dokumentation og testtal netop nu).

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

**Formentlig én opgave.** Bemærk at det er en udvidelse uden datamodel og uden migrering — den skal
derfor ikke have et skivenummer, af samme grund som Swagger-linket i skive 11's forarbejde ikke fik et.

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
