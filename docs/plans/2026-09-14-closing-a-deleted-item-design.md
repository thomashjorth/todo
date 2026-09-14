# En slettet sag kan lukkes fra importen — design

Truffet 2026-09-14. Status: godkendt afsnit for afsnit; implementeringen følger.

Ønsket, brugerens ord: *"Når en tfs work item helt slettes i tfs så opdager appen det ikke."*

Forgængeren er `2026-08-31-source-reconciliation-design.md`, som byggede afstemningen. Den
**rapporterer** allerede en slettet sag — det den aldrig gør, er at tilbyde noget. Og dens afsnit 9
efterlod tre præmisser umålte mod de rigtige instanser. To af dem er målt nu, og det er de målinger
der skar sagen op: de flyttede mistanken væk fra transporten og hen på en regel.

## 1. Målingerne, gjort frem for gættet

Kørt 2026-09-11 mod den rigtige instans — samlingen `https://tfs.edora.dk/Edora%20Software`,
projektet `Saas` — med brugerens eget token, læst ud af en **kopi** af `%APPDATA%\TodoApp\todo.db`
som blev slettet igen. Tokenet blev hverken skrevet ud eller lagt på en kommandolinje; curl fik sin
`Authorization`-header på stdin gennem `-K -`. Scriptet ligger i sessionens scratchpad og er
gengivet i leveranceplanens Task 0.

| Præmis fra afstemningens afsnit 9 | Svar |
| --- | --- |
| Gør `&errorPolicy=omit` det vi tror? | **HTTP 200**, `count: 23` af 25 spurgte. De to ukendte id'er blev udeladt, resten kom hel igennem |
| Er `WHERE [System.Id] IN (…) AND [System.AssignedTo] = @Me` gyldig WIQL der? | **HTTP 200**, svarer de fem ids der stadig er brugerens |

**Transporten er altså i orden**, og det er værd at sige højt: hele afstemningens ADO-halvdel har
hvilet på den første af de to siden 1. september, og `FakeAdo`s svar målte indtil nu vores egen
antagelse. Det gør det ikke længere.

**Og en tredje ting blev målt, som ingen havde spurgt om.** Uden flaget svarer et *slettet* id
**404** med `TF401232: Work item 17121 does not exist, or you do not have permissions to read it` —
ikke den **400** med `The following Ids are not valid` som repoet har skrevet ned fra 2026-08-20 og
som `FakeAdo` er den eneste form for. De to er ikke samme fejl: 400'en er *"det id har aldrig
eksisteret"*, 404'en er *"det id kender jeg godt, men du får det ikke"*. Konklusionen fra august
flytter sig ikke — begge fælder hele kaldet, og med flaget er begge 200 — men fakets kommentar
påstår en enighed der ikke findes, og den skal rettes.

## 2. Hvorfor rapporten var sand, og af en anden grund end nogen gættede

De to work items TFS ikke længere kender, er **17121** og **17572**. Begge opgaver er **færdige
her** — lukket 2026-09-08 11:31 og 2026-09-11 11:42 — og reglen i `ReconciledRowsAsync` er
`Status != Done && !fetched.Contains(key)`: *en opgave der er færdig her afstemmes aldrig.* Målt
samme dag: hentningen svarer præcis de fem åbne nøgler, så afstemningen spurgte om **nul** nøgler,
og der stod ikke engang en notits på skærmen.

Det er to fund og ikke ét, og kun det andet lukkes her:

1. **Appen kan kun se en slettet sag mens opgaven er åben her.** Det er reglen frem for en fejl: er
   opgaven lukket, er der ingen handling tilbage at tilbyde. Se afsnit 6.
2. **Og selv med en åben opgave foreslår appen ingenting.** `item is null` giver ingen
   `ClosureReason`, så rækken står grå med `missingInSource` og **uden flueben**. En løst sag kan
   lukkes med ét klik, en overdraget kan lukkes med ét klik, en slettet skal findes i hånden.

## 3. Beslutningerne, og hvem der traf dem

1. **En slettet sag skal tilbydes lukket**, ikke bare rapporteres. Brugeren, 2026-09-11.
2. **Begge kilder.** Brugeren, 2026-09-11. `ClosureReason` er delt i kontrakten netop fordi ingen
   af værdierne nævner noget kildespecifikt, og de to kilder har svaret ens på hver eneste regel
   indtil nu. Risikoen er også ens: både Jira DC og TFS skjuler *"du har mistet adgangen"* bag
   samme svar som *"den er slettet"* — TFS' 404-tekst siger begge dele i én sætning.
3. **Værdien hedder `missing`, ikke `deleted`.** Kilden kan ikke skelne de to, så et navn der
   påstod sletning ville være en påstand koden ikke kan indfri. `missing` er desuden det ord
   rækkens egen udelukkelseskode allerede brugte, så ordforrådet står stille.
4. **`ado.missingInSource` og `jira.missingInSource` slettes.** Se afsnit 4 for hvorfor de bliver
   uopnåelige, og hvorfor det ikke er oprydning men en konsekvens der skal følges til dørs.

## 4. Reglen, og den kode den slår ihjel

Udtrykket i begge endpoints får sin tredje gren, sidst:

```csharp
ClosureReason? reason = role == Done ? ClosureReason.Resolved
    : item is not null && !answer.StillMine.Contains(key) ? ClosureReason.Unassigned
    : item is null ? ClosureReason.Missing
    : null;
```

Rækkefølgen er bærende af samme grund som før: `Done` slår `Unassigned`, og den nye gren står
**sidst**, fordi de to første begge kræver et svar fra kilden. Den sidste `null` er nu præcis ét
tilfælde — sagen kom med i svaret, er ikke færdig, og er stadig din — altså `outsideFilter`.

Og her er konsekvensen der skal ses frem for opdages undervejs. `Excluded` er i dag

```csharp
Excluded = reason is not null ? null : item is null ? MissingInSource : OutsideFilter;
```

Med den nye gren er `item is null` **altid** en grund, så `MissingInSource` kan ikke længere nås.
De to fejlkoder, deres fire oversættelser og de seks assertions der påstår dem skal derfor væk —
ellers står der to koder tilbage som intet kan producere, og det er den slags løgn `ErrorCodes`
ikke må bære. De seks assertions vender fra at påstå en udelukkelse til at påstå et tilbud.

Rækkens sætning skal bære hele beskeden, fordi skabelonens to grene udelukker hinanden:
**"Kendes ikke i Azure DevOps — slettet, flyttet eller uden for tokenets rettigheder. Luk opgaven
her."** Uden anden halvdel af den sætning mister brugeren den tvetydighed beslutning 3 er bygget på.

## 5. Kontrakten og muren i klienten

`ClosureReason` får `missing`. Og så løber ændringen ind i en mur der er dokumenteret ordret:
**`AdoClosureRow.state` og `JiraClosureRow.status` er `required`**, og `ValidateClosure` afviser en
tilstandsløs lukkerække med `ado.rowStateRequired`. En slettet sag *har* ingen tilstand.

Kravet bliver derfor **betinget** frem for fjernet: feltet bliver nullable i kontrakten, og
validatoren kræver det stadig, medmindre grunden er `missing`. Det er samme præcedens som
forhåndsvisningsrækken, hvis `state` og `workItemType` blev løsnet af nøjagtig samme grund, og
`CloseAsync` behøver ingen ændring — den rører kun tilstanden for `Resolved`.

Klientens halvdel er `wasAnsweredFor`, som dropper netop de tilstandsløse rækker. Dens kommentar
påstår at sådan en række *"arrives carrying `excluded`, so it is in neither `selectable` nor
`closable` and cannot reach the mappings below"* — den påstand holder ikke efter denne leverance.
Lukkegrenen får sin egen betingelse, og kommentaren skal skrives om frem for at blive stående som
en begrundelse der er blevet falsk. Filteret bliver: svarede kilden for rækken, **eller** er
grunden `missing`.

## 6. Prisen, sagt tydeligt

**Denne leverance ville ikke have vist brugerens egne to.** 17121 og 17572 er færdige her, og
Done-filteret står. Den viser den *næste* sag der slettes mens opgaven stadig er åben. Fund 1 fra
afsnit 2 er altså ikke løst, det er fravalgt — se afsnit 8.

**`noneRecognized`-grænsen går fra at spare støj til at være bærende.** Den er nu det eneste mellem
en mistet projektrettighed og et "vælg alle" der lukker levende opgaver, for et tilbud kan
accepteres hvor en grå linje ikke kunne. Ingen række er krydset af på forhånd, og det er den anden
halvdel af nettet. Den accepterede falske negativ ved præcis **én** manglende nøgle bliver stående,
og det er med vilje: det er netop det tilfælde funktionen findes for.

## 7. Test

- **Wire-format.** `"closureReason":"missing"` i `TaskEndpointsTests.Wire_format_uses_the_names_
  the_contract_declares`, hvor de to andre medlemmer allerede læses af den rå krop. En ny
  enum-værdi kan ikke få drift-testen til at fejle, uanset hvordan den serialiseres.
- **Serveren, begge kilder.** De seks eksisterende `MissingInSource`-assertions vender. Grenen skal
  ses fælde noget: fjern den, og tilbuddet forsvinder igen.
- **Importen.** En tilstandsløs `missing`-lukning lukker opgaven; en tilstandsløs `resolved`-lukning
  bliver **stadig** afvist. Den anden påstand er den der gør den betingede validering målbar —
  uden den ville en fjernelse af hele kravet bestå.
- **Klienten.** Vitest på at storen sender en lukkerække uden tilstand, og at den ikke længere
  filtreres væk.
- **Skærmen.** En ny `@if`-gren er umålt indtil fixturet har en række i den tilstand og rejsen
  åbner den, så `ContrastTests` skal iscenesætte den i begge temaer og for begge kilder.
- **Rejsen.** En E2E pr. kilde: en slettet nøgle bliver tilbudt, krydset af og lukket.

## 8. Fravalgt

**At afstemme de færdige opgaver.** Det er det der ville have fanget brugerens egne to, og det blev
udtrykkeligt ikke valgt 2026-09-11. Prisen er et kald pr. nøgle over hele arkivet på hver
forhåndsvisning, og udbyttet er en besked om arbejde der allerede er lukket. Skal det omgøres, er
det `Status != Done`-leddet i begge `ReconciledRowsAsync` der falder, og så skal grænsen i afsnit 6
tænkes igennem igen: med arkivet inde vil "alle manglede" holde op med at være sjældent.

**At slette den lokale opgave frem for at lukke den.** Brugerens ord var "luk", og appen har
maskineriet til lukning i forvejen — en sletning ville være en fjerde handling i importens svar.
