# Importen foreslår at lukke en sag der er løst i kilden — design

Truffet 2026-08-24. Status: designet er godkendt afsnit for afsnit; implementeringen følger.

Ønsket: er en opgave hentet fra ADO eller Jira meldt løst i kilden, men stadig åben hos mig, skal
importværktøjet elegant foreslå at tage imod den nye status.

## 1. Beslutningerne, og hvem der traf dem

Alle brugerens, truffet 2026-08-24.

1. **Opdagelsen holder sig inden for det de nuværende forespørgsler allerede henter.** Ingen udvidet
   JQL og ingen udvidet WIQL. Det er den billigste af tre veje og har en målt asymmetri som pris —
   se afsnit 6.
2. **Hvad der *betyder* løst, er en valgt liste pr. kilde:** `ado.doneStates` og
   `jira.doneStatuses`, plukket fra de samme vælgere som de ventende. Ikke én delt liste: ADO staver
   én idé flere måder (en Test Suite siger `In Progress` hvor en Bug siger `Active`), og de to
   systemers ordforråd er forskellige.
3. **Formen er et flueben på rækken, som importknappen udfører.** Én handling, én rundtur, og du kan
   fravælge de enkelte.
4. **Færdigtidspunktet er kildens tidspunkt for tilstandsskiftet**, ikke da du tog imod. Opgaven blev
   løst da den blev løst, og et arkiv sorteret på færdigdato skal ikke lyve.
5. **En færdig sag der aldrig er importeret, udelades med en grund** frem for at blive hentet ind som
   en ny, åben opgave.

## 2. Fire målinger, gjort frem for gættet

De tre første bekræftede designet; den fjerde flyttede det.

**`ImportedKeysAsync` giver kun nøgler.** Den står ordret to gange — `AdoEndpoints.cs:349` og
`JiraEndpoints.cs:282` — identiske på nær `SourceId`, og begge projicerer `Select(t => t.ExternalKey!)`.
Den lokale status er der ikke, så begge skal blive et opslag nøgle → status. Uden det kan forslaget
ikke holde op med at komme igen, når du har taget imod.

**Tidsstemplet nulstilles for en ikke-ventende række.** `WaitingSince = isWaiting ? … : null` står
begge steder (`AdoEndpoints.cs:156`, `JiraEndpoints.cs:118`). En *løst* række er per definition ikke
ventende, så det tidsstempel beslutning 4 hviler på findes **ikke** på rækken i dag — selv om
`ExternalTask.StatusChangedAt` bærer det hele vejen fra ADO's batch-læsning.

**Jiras pris er ét changelog-kald pr. ventende række**, kaldt inde i løkken og await'et sekventielt
(`JiraEndpoints.cs:119`). Færdig-kandidater lægger ét kald til hver, ad samme vej.

**Og fundet der afgjorde arkitekturen: `PUT /api/tasks/{id}` overskriver `CompletedAt` med
`clock.UtcNow`.** Linjen er `task.CompletedAt = status == CoreStatus.Done ? clock.UtcNow : null`
(`TaskEndpoints.cs:133`), og den fyrer på *enhver* overgang til Færdig. Klienten kan derfor **ikke**
lukke opgaven ved at kalde det eksisterende endpoint — kildens tidsstempel ville blive kastet væk på
vejen ind, tavst. Lukningen rider med på importens endpoint, hvor `waitingSince` allerede har
præcedens for at komme fra klienten som et faktum.

## 3. Reglen, og hvor den bor

Færdighed er en **rolle**, ikke et flag ved siden af ventende: en status kan stå i begge lister, og
noget skal afgøre hvad der vinder. **Færdig vinder** — en løst sag venter ikke på nogen, og den
modsatte rækkefølge ville lade en lukket sag stå som "venter på puljen" og skjule forslaget bag
vagt-grenen. Samme slags load-bearing rækkefølge som `DeadlineBuckets.For`, og skrevet ned frem for
gættet.

For **Jira** er det en fjerde værdi på `JiraStatusRole`, foran `Duty`:
`Done → Duty → Waiting → Actionable`.

For **ADO** bliver `AdoStateRoles.IsWaiting` til `AdoStateRoles.For(state, settings) → AdoStateRole`.
Det er ikke en analogi: klassens egen dokumentation forudser det ordret — *"It answers a bool rather
than a two-valued enum. Jira's enum earns itself on three roles; two values would be a bool with
extra steps."* Med tre roller tjener enum'en sig ind. Den ordinale sammenligning og "en tom eller
blank tilstand er ikke ventende" følger uændret med.

**En tom færdig-liste er en gyldig tilstand** — ingen forslag, ingen udeladelser — i modsætning til
`adoWorkItemTypes`, som importen afviser tom. Den nære fælde er at kopiere den forkerte af de to
præcedenser.

## 4. Kontrakten

Forhåndsvisningsrækken får **to** felter, og delingen er den koden allerede praktiserer for
`isWaiting` mod `waitingSince`: `suggestsClosing` er serverens **beslutning**, `doneAt` er
**faktummet** den blev taget på. Et faktum kan sendes tilbage; en beslutning skal tages igen.
`doneAt` er sit eget felt frem for en løsnet `waitingSince`, fordi et felt der skifter betydning med
en anden boolean er den slags man fejllæser om et halvt år.

`AdoImportRequest` og `JiraImportRequest` får en anden liste, `closures`, med `{ key, state, doneAt }`.
Serveren **tager beslutningen igen**: den læser indstillingerne, tjekker at tilstanden står i
færdig-listen, at nøglen hører til en importeret opgave, og at den opgave ikke allerede er færdig.
Ikke pedanteri — `AdoEndpoints` gør nøjagtig det for typefiltret i dag, fordi klienten kan have
forhåndsvist under en ældre indstilling, eller ikke være vores klient.

Valideringen af en lukkerække **genbruger** `AdoRowKeyRequired` og `AdoRowStateRequired`: samme to
fakta, samme to afvisninger, så nye koder ville være nye oversættelser for en sætning der findes.

Nye koder bliver der **to** af, `ado.excludedDone` og `jira.excludedDone`. Begge skal i **begge**
sprogfiler — `ErrorCodeTranslationTests` enumererer `ErrorCodes` med refleksion — og formuleres
**uden** `{{value}}`, fordi `apiErrorMessage` oversætter uden params.

`AdoImportResponse` og `JiraImportResponse` får `closed` ved siden af `imported` og `skipped`. Og
importen bliver ved med **ikke** at ringe til ADO eller Jira.

## 5. Skærmen

Forslaget behøver **ingen ny kontrol.** Valget er allerede en `Set` af nøgler, fluebenet er
forudmarkeret for alt der kan importeres (`ado-import.ts:183`), og det er deaktiveret af `isBlocked`.
En lukkekandidat bliver derfor simpelthen valgbar igen: samme afkrydsningsfelt, ikke længere
deaktiveret, forudmarkeret som alt andet — men rækkens egen linje siger at et flueben her betyder
*luk den her*, ikke *importér*. Én kontrol, én betydning pr. række, og betydningen står på rækken.

`store.closable()` ved siden af `store.selectable()`, og `isBlocked` spørger om **begge**.
`selectedKeys` initialiseres til foreningen. Ved indsendelse deles den efter hvilken mængde nøglen
ligger i, og bliver til `rows` og `closures` i den ene forespørgsel. `selectedRows`' eksisterende
værn — at filtrere gennem butikkens egen liste, så et flueben tvunget på udefra ikke slipper igennem
— får en tvilling for lukkerne.

Rækkens mærkat er en gren **foran** `alreadyImported`, fordi en kandidat pr. definition også er
importeret tidligere, og `@else if` ellers aldrig ville nå den.

To ting der skal siges højt. **Mærkaten havner i afkrydsningsfeltets tilgængelige navn**, fordi
rækken *er* en `<label>` — her en gevinst, for navnet siger hvad fluebenet gør, men E2E-suiten
matcher navne i deres helhed, så `AdoImportScreen`s locators flytter sig. Og **optællingen skal vise
begge tal**: "3 importeres, 2 lukkes" frem for ét samlet, ellers kan man ikke se at knappen gør to
ting.

## 6. Prisen for beslutning 1, sagt tydeligt

Uden en udvidet forespørgsel kan forslaget kun ramme det kilden allerede sender.

- **ADO** holder kun `[System.State] <> 'Closed'` ude, så `Resolved` og `Done` er der allerede i dag.
  Funktionen virker.
- **Jira** filtrerer på `resolution = Unresolved`, som er *resolutionsfeltet* og ikke statussen. En
  sag der står i status "Løst" **uden** at workflowet sætter en resolution kommer stadig med, og for
  den virker funktionen. Sætter workflowet en resolution, forsvinder sagen ud af JQL'en, og
  Jira-halvdelen kan **aldrig** udløse.

## 7. Målingen der bør komme før implementeringen

Sætter din Jira en resolution, når en sag flyttes til en færdig-status? Åbn en løst sag og se om
resolutionsfeltet er udfyldt. Gør den det, er `jira.doneStatuses`, den fjerde rolle og de tilhørende
sprognøgler en gren der er **død hos dig** — og så er det værd at vide, før de bygges.

## 8. Test

- **Core:** færdig slår ventende, og for Jira også vagt. Set fejle ved at bytte grenene om — den
  eneste vagt der kan fange en ombytning, fordi begge udfald er lovlige roller.
- **Api:** `suggestsClosing` kun når alle tre fakta holder, med hver af de tre negeret som sin egen
  test. Importen lukker, sætter `CompletedAt` fra `doneAt`, og tager reglen igen: en lukkerække hvis
  tilstand ikke står i færdig-listen springes over og tælles ikke med i `closed`.
- **Tanden ligger i færdigtidspunktet.** `TaskEndpoints.cs:133` sætter `clock.UtcNow`, så et fixture
  hvis `doneAt` ligger tæt på testurets nu ville bestå med den forkerte implementering. Tidsstemplet
  skal ligge **uger** fra `FixedClock`, og påstanden skal være på den nøjagtige værdi.
- **E2E kan måle det ægte.** `FakeAdo` er sin egen Kestrel på 127.0.0.1 og `RunningHost` starter
  appen i testprocessen, så rejsen kan importere en sag, lade den falske server melde den færdig,
  forhåndsvise igen, tage imod, og påstå at opgaven står som fuldført **med kildens dato**. Ingen
  opsnapning, hele kæden.
- **Kontrastvagten** får to nye `@if`-grene at male, forslagslinjen og udeladelseslinjen, i begge
  temaer.
- **Fire håndskrevne `PreviewRowJson`-fixtures** (`ado-import`, `ado-store`, `jira-import`,
  `jira-store`) plus tre E2E-filer med rutehandlere skal bære de nye felter. Kun fem kaldesteder er
  compiler-synlige (`new AdoPreviewRow`/`new JiraPreviewRow`); de fire rå svarkroppe fejler tavst.

## 9. Fravalgt

Ingen udvidelse af forespørgslerne, ingen genåbning den anden vej, intet bånd med "tag imod alle",
intet nyt endpoint, og ingen anden lokal status end Færdig.
