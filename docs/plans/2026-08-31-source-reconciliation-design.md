# Importen afstemmer det den allerede har hentet — design

Truffet 2026-08-31. Status: designet er godkendt afsnit for afsnit; implementeringen følger.

Ønsket, brugerens ord: er en allerede importeret opgave sat som afsluttet i kildesystemet, skal
importen foreslå at den også lukkes i appen — **også hvis den er blevet tildelt til andre**. Og den
anden vej: har jeg sat en opgave som afsluttet her, mens kilden ikke har, skal det foreslås at sætte
den i en status der matcher kilden.

Forgængeren er `2026-08-24-import-closure-design.md`, som leverede den første halvdel af den første
sætning. Dens afsnit 9 fravalgte udtrykkeligt "genåbning den anden vej"; det er den beslutning der
omgøres her, og de to nye tilfælde falder begge ned i det samme hul: **de nuværende forespørgsler
henter dem ikke.**

## 1. Beslutningerne, og hvem der traf dem

Alle brugerens, truffet 2026-08-31.

1. **Opdagelsen sker ved at spørge kilden om de nøgler vi allerede har** — en afstemning — frem for
   ved at læse fravær som et signal. Fravær er tvetydigt: skifter du projektnøgle, sagstypefilter
   eller vagt-kontakten, ser hver eneste opgave "forsvundet" ud på én gang, og en app der så tilbød
   at lukke dem alle ville være farlig præcis den dag du rørte en indstilling.
2. **Forslagene kommer tilbage som almindelige forhåndsvisningsrækker** i den liste der er der
   allerede: samme flueben, samme importknap, samme tælling. Kontrakten vokser med felter, ikke med
   en ny rækketype.
3. **ADO-tilstanden `Closed` tæller altid som færdig**, uden en indstilling ved siden af. Appen
   påstår det allerede ét sted — se afsnit 2 — så reglen bliver skrevet frem for underforstået.
4. **De svar afstemningen ikke kan forklare, vises som en linje uden flueben.** Uden den kan "appen
   sagde ingenting" både betyde "intet har ændret sig" og "jeg kunne ikke finde ud af det", og det er
   forskellen på en funktion man tror på og en man tjekker manuelt bagefter.

Og en beslutning der blev truffet af en måling frem for af nogen: **genåbningen behøver slet ingen
afstemning.** Se afsnit 2.

## 2. Målingerne, gjort frem for gættet

**De to forespørgsler skjuler præcis de to nye tilfælde.** ADO's er
`SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me AND [System.State] <> 'Closed'`
(`AdoTaskSource.cs:94`), Jiras er `project = X AND assignee = currentUser() AND resolution = Unresolved`
(`JiraTaskSource.cs:183`). Tildeles sagen en anden, falder den ud af **begge**. Og den sidste
tilstand — `Closed` hos ADO, en sat resolution hos Jira — falder også ud, hvilket er hvorfor
forgængerens afsnit 6 advarede om, at Jira-halvdelen af lukke-forslaget kunne være **død** hos denne
bruger. Samme årsag, og afstemningen retter begge.

**Genåbningen er synlig i forvejen.** Har du sat en opgave som færdig her, mens sagen stadig er
tildelt dig og uafsluttet i kilden, opfylder den begge forespørgslers betingelser og **kommer med** i
dagens hentning — den står blot som `alreadyImported` med et deaktiveret flueben. Er den derimod
tildelt en anden, er "færdig her" det rigtige svar og der er intet at foreslå. Genåbningen er derfor
en ren regel på en række vi allerede har, spejlvendt af `suggestsClosing`, og afstemningen behøver
kun at spørge om **de nøgler der er åbne her og manglede i hentningen**.

**ADO-tilstanden `Closed` kan ikke vælges gennem appen.** `FetchStatusNamesAsync` bygger på samme
`BaseQuery` (`AdoTaskSource.cs:192`), altså med `<> 'Closed'` i, så vælgeren kan aldrig tilbyde
navnet; og `settings.ts`' `stateOptions()` lægger kun de navne tilbage der **allerede er valgt**. Det
er en hønen-og-ægget: `Closed` kan ikke vælges, før den er valgt. Konsekvensen for afstemningen er
hele forskellen mellem virker og virker ikke — et work item der er nået til `Closed`, kommer tilbage
med den tilstand, og `AdoStateRoles.For` ville kalde den `Actionable`, altså "stadig åben", stik
modsat sandheden. Derfor beslutning 3, og den har sin egen præcedens: `BaseQuery`s kommentar siger
ordret, at `Closed` er det ene navn alle sagstyper deler.

**Jira har ikke den fælde**, og asymmetrien er målt frem for antaget: `FetchStatusNamesAsync` henter
statusvalgene fra `project/{key}/statuses` (`JiraTaskSource.cs:87`), altså hele workflowet, så "Løst"
og "Lukket" kan krydses af uden at nogen sag står i dem lige nu.

**Jiras identitet er kun et visningsnavn.** `MyselfBody` binder `DisplayName` og intet andet
(`JiraTaskSource.cs:410`). En sammenligning på det ville knække den dag nogen retter et efternavn, og
det er derfor ejerskabet i afsnit 3 er noget kilden selv svarer på.

## 3. Afstemningen

`ITaskSource` får **én** ny metode, og den svarer på to spørgsmål på én gang:

```csharp
Task<ExternalReconciliation> ReconcileAsync(
    IReadOnlyList<string> keys, CancellationToken ct = default);

public sealed record ExternalReconciliation(
    IReadOnlyList<ExternalTask> Found,
    IReadOnlySet<string> StillMine);
```

`Found` genbruger `ExternalTask` **helt** — titel, tilstand, type, `StatusChangedAt` — så en afstemt
række bygges af den samme kode som en almindelig forhåndsvisningsrække, og der er ingen anden vej ind
i `AdoPreviewRow`. `StillMine` er ejerskabssvaret: en nøgle i `Found` men **ikke** i `StillMine` er
tildelt en anden. En nøgle i **ingen** af dem kender kilden ikke.

Delingen i to mængder frem for et flag pr. række er hvad der holder navnesammenligningen ude. Begge
kilder kan svare på "er den stadig min" med deres eget begreb — `@Me` og `currentUser()` — og det er
serverens egen afgørelse, ikke vores gæt på hvad et visningsnavn betyder. **Bemærk hvad der derfor
ikke skal gøres:** `System.AssignedTo` skal *ikke* lægges i `ItemFields`. Feltets wire-form er umålt,
og `System.CreatedBy` står i koden som en `JsonElement` af netop den grund — en identitet mere at
binde i blinde er en pris afstemningen ikke behøver at betale.

**Hvem bliver spurgt:** opgaverne med denne `SourceId`, med en `ExternalKey`, som ikke er `Done` her,
og hvis nøgle ikke kom med i dagens hentning. De øvrige har allerede en række. Mængden er derfor
afgrænset af din egen arbejdsliste frem for af arkivet, og den er typisk en håndfuld.

**Og opslaget skal bære titlen, ikke kun statussen.** Rettet 2026-08-31 under plan-arbejdet:
`ImportedKeysAsync` svarer i dag `Dictionary<string, TodoStatus>`, og forgængeren udvidede den netop
fra en `HashSet` til det — men en `missingInSource`-række har ingen titel fra kilden, fordi der ikke
kom noget svar at tage den fra. Den eneste titel der findes, er den lokale opgaves. Opslaget bliver
derfor nøgle → (status, titel), og det er samme slags udvidelse af samme metode, en leverance senere.

## 4. De to kilders veje er forskellige, og forskellen er den slettede sag

Det er samme slags fund som skive 12's: de to kilder ser ens ud, indtil man spørger dem om noget
bestemt. Her er det bestemte spørgsmål **en nøgle der ikke findes** — altså præcis det tilfælde
funktionen findes for at fortælle om.

**ADO: to kald, begge genbrug.** En WIQL `WHERE [System.Id] IN (…) AND [System.AssignedTo] = @Me`
giver `StillMine`, og `HydrateAsync` — batch-læsningen der findes — giver `Found`. Batch-læsning efter
id er uafhængig af både tildeling og tilstand, så et `Closed` work item tildelt en anden kommer med.
WIQL'en kan ikke fejle på en ukendt id: den matcher blot ingenting. **Batch-læsningen kan**, og skal
derfor have `&errorPolicy=omit` med — uden den fejler `?ids=` for *hele* forespørgslen, hvis bare én
id er slettet, og hver forhåndsvisning vælter på én slettet sag. Klumpestørrelsen er den eksisterende
`BatchSize` på 200.

**Og fejlkoden er en 400, ikke en 404 — rettet 2026-08-31, fordi repoet allerede havde målingen.**
Denne tekst sagde 404 indtil Task 2 blev bygget, og det var et gæt skrevet ved siden af et målt tal:
`FakeAdo` og `AdoTaskSource.Explanation` bærer begge en måling fra 2026-08-20, hvor svaret er **400**
med de ugyldige id'er navngivet i beskeden. Konklusionen er uændret — hele forespørgslen fejler, så
flaget skal med — men forskellen er værd at holde fast i, fordi den er hele grunden til at den falske
server skal udsende fremmedsystemets *faktiske* format: havde `FakeAdo` fået lov at udsende designets
404, ville testen have målt vores egen antagelse og ikke andet.

**Jira: ét kald pr. nøgle, og derefter ét til.** `GET issue/{key}` pr. nøgle, hvor **404 *er* svaret**
"kender den ikke", giver `Found`. Derefter **én** JQL `key IN (<dem der findes>) AND assignee =
currentUser()` for `StillMine`. Den vej er valgt fordi den elegante — én søgning `key IN (alle
nøgler)` — er den skrøbeligste vi kunne vælge: Jira svarer 400 med *"The issue key … does not exist"*
på en ukendt nøgle i `key IN`, så en enkelt slettet sag ville tage hele afstemningen med sig. Den ene
JQL bagefter kan derimod ikke fejle, fordi den kun nævner nøgler vi lige har set svare 200. Og prisen
er den kendte: Jira betaler i forvejen ét changelog-kald pr. ventende række, så N små kald er samme
omkostningsmodel og ikke en ny.

**Men "én JQL" er ikke det samme som ét kald, og forskellen er en fejl der ligner en funktion.**
Tilføjet 2026-08-31 under Task 3, hvor det blev fundet. Sætningen ovenfor gælder forespørgsels*teksten*
— Jira lofter `maxResults`, så en upagineret læsning taber hver ejer efter første side ned i
*i `Found`, men ikke i `StillMine`*, og det udfald er nøjagtigt det appen læser som **tildelt en
anden** og tilbyder at lukke. Fejlen er derfor ikke en manglende række men et forkert forslag om at
lukke noget der stadig er dit, og den viser sig først når afstemningsmængden vokser forbi sidens
størrelse. Ejerskabssøgningen skal gennem **samme pager som hentningen** — udtrukket som `SearchAsync`
i Task 3 — og det er den ene ting ved formen der ikke kan læses ud af "ét kald pr. nøgle, derefter ét
til".

Bemærk at asymmetrien også løser et andet problem for Jira: `/search` kaldes her som et **GET**, og en
JQL med 300 nøgler er omkring 4 kB URL — tæt på Jira DC's grænse for en forespørgselslinje. Den ene
JQL nævner kun de nøgler afstemningen faktisk fandt.

## 5. Reglerne, og hvor de bor

`AdoStateRoles.For` får `Closed` ved siden af `settings.DoneStates`, i den gren der allerede
returnerer `Done`. Reglen flytter altså **op** fra `BaseQuery`, hvor den i dag står underforstået i en
forespørgsel, til det ene sted rollen afgøres. Jira får ingen modpart: dens vælger tilbyder hele
workflowet, så der er intet skjult navn at kompensere for.

Fire udfald pr. afstemt nøgle, og de bliver til:

| Udfald | Rækken siger |
| --- | --- |
| rollen er `Done` | `suggestsClosing` + `closureReason: resolved` |
| ikke i `StillMine` | `suggestsClosing` + `closureReason: unassigned` |
| stadig din, ikke færdig | `excluded: <kilde>.outsideFilter` |
| kilden kender den ikke | `excluded: <kilde>.missingInSource` |

Rækkefølgen er bærende: **færdig slår ikke-tildelt.** Er sagen både løst og videregivet, er "løst" det
mere præcise af de to, og den handling brugeren skal tage er den samme — så det er mærkaten der
afgøres, ikke fluebenet. Byttes de om, får en løst sag den svagere forklaring.

**`excluded` og en handling udelukker hinanden, og det er en regel frem for en oprydning.** Tilføjet
2026-08-31 under Task 5, som afdækkede modsigelsen. `excluded` er rækkens *"der sker ingenting"*-mærke:
`isBlocked` læser det for at slå fluebenet fra, og mærkatkæden viser det **først**. En række der
foreslår en genåbning har noget at gøre — så bar den også `excluded`, ville brugeren se "springes
over" på en række hvis flueben er **slået til** og som **importerer**.

Det kunne ikke ske før, og grunden er værd at kende: `excludedDone` er vagtet af `localStatus is null`,
altså kun for rækker der aldrig er importeret, mens `excludedWaiting` **ikke** er vagtet — den sættes
af `isWaiting && !includeWaiting` alene. Det var harmløst, så længe en allerede importeret ventende
række ingenting kunne. Med genåbningen kan den. Reglen er derfor at **`excluded` ikke sættes på en
række der foreslår noget**, og den løser det i den ende hvor betydningen bor, frem for ved at bytte om
på mærkatkæden — kædens rækkefølge har sin egen begrundelse i skabelonen og skal blive stående.

Genåbningen er `suggestsReopening`, sat når opgaven er `Done` her og kildens rolle **ikke** er `Done`.
`reopenTo` er den status en frisk import ville have givet den: rollen `Waiting` giver `waitingFor`,
alt andet giver `open`. Det er ikke en ny mapning — det er den `AdoEndpoints`' importløkke bruger i
dag — og vagt-rollen falder derfor med rette på `open`, fordi en vagt-sag venter på *dig*.

**`includeWaiting` slukket forhindrer ikke en genåbning.** Kontakten styrer hvad der kommer *ind* i
listen som nyt arbejde; en opgave du allerede ejer, holdes ikke tilbage af et filter for nye. Brugeren
bad om "status matchende kildesystem", og det er `waitingFor`.

**Og der er én regel mere, som er hele funktionens sikkerhedsnet: svarer *hver* nøgle med et fravær,
foreslås ingenting.** Tilføjet 2026-08-31 under Task 3, hvor risikoen blev målt frem. Grunden er
Jiras egen adfærd: **Jira DC skjuler rutinemæssigt en sag du ikke må browse bag
`404 Issue Does Not Exist` frem for bag en 403**, netop for ikke at afsløre at sagen findes. Mister
du en projektrettighed — eller udløber tokenet på en måde der giver 404 frem for 401 — svarer hver
eneste nøgle "kender den ikke", og uden reglen læser appen det som at **hele din liste er slettet**
og tilbyder at lukke alt. Det er værre end den fejl afstemningen findes for at rette.

Grænsen er derfor: **alle nøgler fraværende og mere end én nøgle spurgt → ingen forslag**, og i stedet
en linje der siger at kilden ikke genkendte noget. En massesletning af hele din arbejdsliste er
sjældnere end en mistet rettighed, så det sikre gæt er rettigheden. Reglen hører i forhåndsvisningen,
altså ved siden af de fire udfald i tabellen ovenfor, og den gælder **begge** kilder — ADO har samme
klasse gennem et token der ikke må læse projektet. Bemærk at én nøgle spurgt er undtaget: der er
"alle" og "én" det samme, og en enkelt slettet sag skal stadig kunne foreslås lukket.

**Og reglens falske negativ skal siges højt, for den findes.** Sletter du rent faktisk *begge* dine
to eneste afstemte sager, foreslås ingen af dem — grænsen kan ikke skelne det fra en mistet
rettighed, fordi de to ser identiske ud fra kildens svar. Det er accepteret frem for overset: prisen
er et manglende forslag, som du selv kan lukke opgaverne uden, mod den anden vejs pris, som er et
tilbud om at lukke **hele** din liste. Ved store N forsvinder problemet af sig selv, fordi en
massesletning bliver usandsynlig; det er kun ved to og tre at reglen koster noget, og der er den
billig.

## 6. Kontrakten

`AdoPreviewRow` og `JiraPreviewRow` får tre felter: `closureReason` (`resolved` | `unassigned`,
nullable og kun meningsfuld når `suggestsClosing`), `suggestsReopening` (boolean) og `reopenTo`
(`open` | `waitingFor`, nullable). Samme deling som forgængeren fandt: `suggestsReopening` er
serverens **beslutning**, `reopenTo` er hvad den ville føre til, og `state`/`status` er **faktummet**
begge blev taget på.

**Og `state`/`status` bliver nullable.** En nøgle kilden ikke kender har ingen tilstand, og et
påtvunget `""` er netop den slags felt CLAUDE.md advarer imod — et felt hvis type siger "altid der",
mens værdien siger noget andet. Præcedensen peger samme vej og siger hvorfor: `WorkItemType` er
`item.ItemType ?? string.Empty` med kommentaren om at *"a null is unreachable — which is why this is
an empty string rather than a branch."* Her **er** den nåelig. Prisen er seks kaldesteder, som
compileren så navngiver: `ado-import.html:103`, `ado-store.ts:139` og `:157`, og Jiras tre tvillinger.
De to store-kaldesteder ligger i `selectable`/`closable`-mapninger, som pr. konstruktion aldrig ser en
uforklaret række, så deres værn bliver en filtrering der matcher det de gør i forvejen.

**Og `workItemType` går samme vej, hvad denne tekst først ikke sagde.** Rettet 2026-08-31 under
plan-arbejdet: ADO-rækken har *to* felter der kun kan udfyldes af et svar fra kilden, og skabelonen
siger det selv i en kommentar der holder op med at være sand — *"Ingen @if: typen er altid udfyldt"*
(`ado-import.html:97`). Én nullable og én tvunget tom ville være den værste af de to former, fordi
inkonsekvensen skjuler hvilken af dem der er den bevidste.

**`AdoClosureRow` og `JiraClosureRow` får `reason` med.** Også rettet 2026-08-31, og det er den
rettelse der ellers ville have gjort halvdelen af funktionen tavst virkningsløs: `CloseAsync`s første
vagt er `AdoStateRoles.For(closure.State, settings) != AdoStateRole.Done → spring over`, så en sag der
er lukket fordi den er **tildelt en anden** — og hvis tilstand derfor er `Active` og ikke i
færdig-listen — ville blive sprunget over og talt i `skipped`. Serveren skal altså vide *hvorfor*.
Prisen er at gen-beslutningen bliver **svagere** for `unassigned`: ejerskabet er et faktum fra kilden,
og importen ringer ikke ud, så serveren kan kun kræve at opgaven findes, hører til kilden og ikke
allerede er færdig. Det er samme form som `doneAt` og `waitingSince` allerede har, og den er værd at
sige højt frem for at lade nogen opdage den. `doneAt` er til gengæld **null** for en `unassigned`
lukning — sagen blev ikke fuldført, den holdt op med at være din — så `CompletedAt` falder tilbage på
`clock.UtcNow`, hvilket er det rigtige svar og allerede er den kodede adfærd.

`excluded` genbruges til de to uforklarede svar, og det er en ren gevinst: feltet **er** allerede en
fejlkode, `excludedReason()` oversætter den gennem `errors.<kode>`, og `isBlocked` deaktiverer
allerede fluebenet for en ekskluderet række. De to linjer koster derfor **fire nye koder** —
`ado.outsideFilter`, `ado.missingInSource` og Jiras to — og **nul** ny mekanik. Alle fire skal i
**begge** sprogfiler, fordi `ErrorCodeTranslationTests` enumererer `ErrorCodes` med refleksion, og
alle fire formuleres **uden** `{{value}}`, fordi `apiErrorMessage` oversætter uden params.

`AdoImportRequest` og `JiraImportRequest` får en tredje liste, `reopenings`, med
`{ key, state, waitingSince }` — nøjagtig de samme tre fakta som en lukkerække bærer, og valideret med
de samme to koder, `…RowKeyRequired` og `…RowStateRequired`. Serveren tager beslutningen igen: læser
indstillingerne, slår tilstanden op, og kræver at opgaven findes, hører til kilden og **er færdig
her**. Så `Status` fra rollen, `CompletedAt` ryddet, og `WaitingSince` fra kildens tidsstempel når
rollen er `Waiting`. Svaret får `reopened` ved siden af `imported`, `skipped` og `closed`.

Og importen bliver ved med **ikke** at ringe til ADO eller Jira.

## 7. Skærmen

Mærkatkæden får to led mere, og rækkefølgen er bærende af samme grund som forgængerens var — en
genåbningskandidat *er* også importeret tidligere, så `alreadyImported` skal blive ved at være sidst:

```
excluded → suggestsClosing → suggestsReopening → alreadyImported
```

`suggestsClosing` deler sig i to sætninger efter `closureReason`, så "løst i kilden" og "tildelt en
anden" aldrig ligner hinanden. Tilstandslinjen får en `@if`, fordi feltet nu kan være fraværende — og
rækken forklarer selv hvorfor, med sin `missingInSource`-linje.

`store.reopenable()` kommer ved siden af `closable()`, `isBlocked` spørger om alle tre mængder, og
`selectedKeys` starter som foreningen af dem. `selectedRows`' eksisterende værn — at filtrere gennem
butikkens egen liste, så et flueben tvunget på udefra ikke slipper igennem — får en tredje tvilling.

**Tællingen skal vise tre tal:** "3 importeres, 2 lukkes, 1 genåbnes". Ét samlet tal kan ikke sige
hvad knappen gør, og knappen gør nu tre ting.

## 8. Prisen, sagt tydeligt

Afstemningen spørger kun om opgaver der **ikke** er færdige her, så genåbningen kan kun udløse for en
sag dagens hentning selv bringer med. To huller følger af det, og de er navngivet frem for opdaget
senere:

- **ADO:** sagstypefiltret ligger i WIQL'en, så en fuldført opgave hvis work item nu har en type uden
  for filtret kommer ikke med, og genåbningen kan ikke foreslås.
- **Jira:** `resolution = Unresolved` er resolutionsfeltet, ikke statussen. Sættes status tilbage til
  "I gang" uden at resolutionen ryddes, kommer sagen ikke med.

Begge kunne lukkes ved også at afstemme de færdige opgaver, og begge er bevidst ladt åbne: det ville
gøre afstemningens mængde til arkivet frem for arbejdslisten, for et tilfælde der kræver at kilden
modsiger sig selv.

## 9. Målingerne der skal komme før implementeringen

Tre, og de to første er den slags der afgør om koden virker mod din instans:

1. **Gør `&errorPolicy=omit` det jeg tror?** Bed batch-læsningen om en id der ikke findes, med og uden
   flaget, og se om svaret er en fejl for hele forespørgslen eller de øvrige rækker. Uden flaget er
   svaret målt til **400** med de ugyldige id'er navngivet; det er *med* flaget der er umålt.
2. **Svarer Jiras `issue/{key}` 404 på en slettet sag** — og ikke 400 eller 403? Og fejler `key IN`
   virkelig på en ukendt nøgle? Den anden halvdel er hele begrundelsen for at Jira går pr. nøgle.
3. **Er `WHERE [System.Id] IN (…)` gyldig WIQL** på denne server? Billig at afvise, dyr at antage.

## 10. Test

- **Core:** `Closed` er `Done` for ADO uden en indstilling. Set fejle ved at fjerne navnet fra grenen —
  og bemærk at `AdoStateRoles`' egne tests i forvejen har den vagt der kan se en ombytning af
  grenene, så den nye påstand skal være på navnet og ikke på rækkefølgen.
- **Api:** de fire udfald i afsnit 5, hver som sin egen test, og hvert af de tre fakta bag en
  genåbning negeret for sig. En genåbningsrække hvis tilstand nu **er** i færdig-listen springes over
  og tælles ikke med i `reopened`.
- **Tanden i genåbningen er at `CompletedAt` bliver ryddet.** Et fixture hvor den allerede er `null`,
  ville bestå med en implementering der ikke rydder noget — så opgaven skal sås fuldført med et
  tidsstempel **uger** fra `FixedClock`, og påstanden være på `null` bagefter. Spejlvendt af
  forgængerens tand, som var den nøjagtige værdi.
- **E2E kan måle det ægte, og skal.** `FakeAdo` er sin egen Kestrel på 127.0.0.1 og `RunningHost`
  starter appen i testprocessen, så rejsen kan importere en sag, lade den falske server **tildele den
  en anden**, forhåndsvise igen og tage imod — uden at ét kald opsnappes. Det er den eneste form der
  måler at afstemningen faktisk stiller et andet spørgsmål end hentningen.
- **`FakeAdo` skal kunne svare på batch-læsning af en id den ikke har**, ellers kan hullet i
  `errorPolicy` ikke skrives ned som en test.
- **Kontrastvagten får fire nye `@if`-grene at male** — de to lukkesætninger, genåbningen og de to
  uforklarede linjer — og hver af dem koster en fixture-række i begge temaer. En gren er umålt,
  indtil noget renderer den.
- **De fire nye enum-værdier kan ikke fælde drift-testen**, som kun sammenligner stier og metoder, så
  `resolved`, `unassigned`, `open` og `waitingFor` skal have deres egne påstande i
  `Wire_format_uses_the_names_the_contract_declares`.
- **Seks håndskrevne wire-fixtures og rutehandlerne skal bære de nye felter**, og kun fem kaldesteder
  er compiler-synlige. Nullabiliteten på `state`/`status` er den ene ting compileren *kan* se, så
  forvent den som den første røde.

## 11. Fravalgt

Ingen udvidelse af de eksisterende forespørgsler. Ingen afstemning af færdige opgaver, hvis pris står
i afsnit 8. Ingen indstilling for `Closed`. Ingen binding af `System.AssignedTo`. Intet nyt endpoint —
afstemningen rider på forhåndsvisningen. Og ingen anden handling for en sag der er tildelt en anden
end at lukke den: uddelegering ville passe på papiret, men brugeren bad om lukning, og appen skriver
ikke til kilden.
