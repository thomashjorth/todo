# Hvor projektet står

Sidst opdateret: 2026-08-20

Konventioner og maskinens fælder: `CLAUDE.md` i roden — den indlæses automatisk.
Design, datamodel og beslutninger: `docs/plans/2026-08-13-todo-app-design.md`.

## Færdigt

| Skive | Hvad den gav | Plan |
| --- | --- | --- |
| 0 | Photino-vindue + Kestrel + Angular, kontraktgenerering, drift- og friskhedstest, første Playwright-test | `2026-08-13-slice-0-skeleton.md` |
| 1 | Opgaver med deadline, opgavestiller, note, status og underopgaver. Deadline-sektioner. SQLite med migrationer | `2026-08-13-slice-1-own-tasks.md` |
| 2 | Retro-import fra indsat CSV: afstemningskort filtreres, aliaser afgør hvad der er dit, gen-import er sikker | `2026-08-14-slice-2-retro-import.md` |
| 3 | Indstillingsside og lokalisering — dansk/engelsk med Transloco, systemets sprog som standard | `2026-08-14-slice-3-settings-and-localization.md` |
| 4 | Noter i fuld CommonMark, renderet, klik for at redigere. Links åbner i systemets browser | `2026-08-14-slice-4-markdown-notes.md` |
| 5 | "Venter på" og "Måske" som statusser, med hvem og hvor længe | `2026-08-14-slice-5-waiting-and-someday.md` |
| 6 | TypeScript strict mode, og opgaverækken som en typet børnekomponent frem for en delt `ng-template` | `2026-08-17-slice-6-typescript-strict.md` |
| 7 | WCAG AA i begge temaer med en kontrastvagt der måler i browseren, `dark:`-modparter så godt som overalt (én bevidst undtagelse, se designdokumentets afsnit 10), synligt fokus og en tastaturgennemgang | `2026-08-17-slice-7-accessibility.md` |
| 8 | Alt-genvejssystemet: hold Alt for at se mærkaterne, og Alt+O/I/S/N/V/M udfører elementets aktiveringshandling — links følges, de to kontakter skifter og tager fokus, feltet får fokus. (`Alt+J` kom til med skive 11, så bogstavlisten er nu `O/I/J/S/N/V/M`) | `2026-08-17-alt-shortcuts.md` |
| 9 | Startdato (`DeferUntil`): en opgave ligger i Udskudt indtil dagen den begynder. Udskudtheden er **beregnet** af dagens dato, ikke gemt som en status, så intet skal køre ved midnat. Overskredet slår Udskudt — se designdokumentets afsnit 10 | `2026-08-17-slice-9-defer-until.md` |
| 10 | `long` som id: `Guid` er væk fra `TaskItem`, `SubTask` og `UserAlias`, og id'et tildeles af SQLite ved indsættelse, så **"opgave 42" kan siges højt**. Migreringen er skrevet i hånden — en `CAST` af Guid-strenge ville have flettet rækker sammen — og en vagt stiller rigtige Guid-rækker op foran den og kræver dem intakte bagefter | `2026-08-17-long-ids.md` |
| 11 | **Jira-import** mod Data Center 10.3.24: `ITaskSource` + `JiraTaskSource`, wiki-markup → CommonMark, Jira-opsætning på indstillingssiden med tokenet på **sit eget** endpoint, en fjerde skærm med forhåndsvisning, grunde og dedup, `WaitingSince` læst af changeloggen, og "Åbn sagen" på en importeret række. **Ingen migrering** — `ExternalUrl` er beregnet, og `Ext*`-felterne plus afstemningen ligger bevidst i skive 14 | `2026-08-18-slice-11-jira-import.md` |

**Uden for skiverne, som en udvidelse af skive 11: vagt-statusser fra Jira** (2026-08-19, plan i
`docs/plans/2026-08-19-jira-duty-statuses.md`). To nye indstillinger — `jiraDutyStatuses` og
`jiraOnDuty` — udvider JQL'en med et `OR status IN (…)`-led, **kun** når kontakten er slået til, så
den generelle puljes sager kommer med selvom de ikke er tildelt dig. Reglen bor i
`JiraStatusRoles.For` i `Todo.Core`, hvor **vagt slår ventende**: samme status er *ventende* uden
vagten og *handlingsklar* med, så en puljesag importeres som `Open` og lander i deadline-sektionerne
frem for i "Venter på". Forhåndsvisningen mærker rækken (`isDuty`), og import-skærmen siger med ord
at vagten er slået til. **Den fik bevidst ikke et skivenummer** — ingen ny datamodel, ingen
migrering, ingen ny skærm — af samme grund som Swagger-linket: et nummer ville have skubbet ADO til
13 og hver skive efter den med. Se designdokumentets afsnit 4a og 9. **ADO-mentions er stadig ikke
verificeret**, og målingen nedenfor bør køres, før skive 12 planlægges.

Uden for skiverne: app-ikon og titel, `Todo.cmd`-launcher, omstrukturering til feature-mapper,
testdata-builders, `ApiTest`/`BrowserTest`-basisklasser, og **linket til API-dokumentationen på
health-linjen** (2026-08-17, plan i `docs/plans/2026-08-17-swagger-link.md`): "API: ok" har nu en
knap ved siden af, der beder systemets browser åbne `/scalar/`, hvor kontrakten selv vises fra
`/openapi/contract.yaml`. **Den fik bevidst ikke et skivenummer** — én affordance, ingen datamodel
og ingen ny skærm — og et nummer ville have tvunget Jira-importen til 10 og hver skive efter den
med (ADO, mentions, baggrundssync, livscyklus, pakning). Skive 9 og siden skive 10 har gjort netop
det, hver af en anden grund: de rører begge datamodellen og er derfor skiver. Jira-importen er
dermed nummer 11. Hvad arbejdet efterlod af
viden, står i
designdokumentets afsnit 10: appen udstiller **to** OpenAPI-dokumenter med hver sin rolle, en ny
rute uden for `/api/` skal have `.ExcludeFromDescription()`, og dokumentationssiden er vagtet mod
at kalde ud.

Uden for skiverne, fundet ved et tilfælde: **`TaskStore` kunne lade et forsinket ældre load
overskrive den nyeste liste.** `setShowCompleted` og `setShowSomeday` sætter hver sit signal og
kalder derefter `load()`, så to genindlæsninger kan være i luften på én gang uden noget der
ordner svarene. Ankom det ældste sidst, stod listen forkert indtil noget andet udløste en ny
indlæsning — slås begge kontakter hurtigt efter hinanden, kunne "Måske"-sektionen forsvinde.
Fundet fordi `ContrastTests` flakkede (7–9 s frem for 2 s), ikke fordi nogen ledte efter det.
`load()` har nu en sekvenstæller, og regressionstesten
`should not let a slow earlier load overwrite a newer list` blev set fejle først.

**Uden for skiverne: uddelegering af en opgave** (2026-08-20, design i
`docs/plans/2026-08-19-delegating-a-task-design.md`, plan i `2026-08-19-delegating-a-task.md`).
**Uddelegering er en genvej til en tilstand der findes** — `WaitingFor` + `WaitingOn` — så der er
**ingen `Delegated`-status**, intet nyt felt på `TaskItem` og **ingen migrering**. Nyt er én
indstilling, `delegates` (JSON i `Setting`), en delt listehjælper `SettingList` udtrukket fra
`JiraSettingsReader`, en uddelegeringsgruppe på indstillingssiden, **indstillingssiden delt i fire
ligestillede grupper** (Sprog, Uddelegering, Jira-import, Retro-import), og en delt
`<datalist id="delegate-names">` der giver forslag på `waitingOn`-feltet, som statusvælgeren giver
fokus når en opgave flytter til "Venter på". **Listen er forslag, ikke et krav:** feltet bliver et
tekstfelt, fordi "venter på ingen" og "venter på en der ikke står på listen" begge er gyldige
tilstande. **Ingen besked til den anden og ingen tilbageskrivning til Jira** — en uddelegeret
Jira-sag skifter ikke assignee, og UI'et siger det med ord. **Den fik bevidst ikke et skivenummer**
— ingen ny datamodel, ingen migrering, ingen ny skærm — af samme grund som Swagger-linket og
vagt-statusserne. Testtal efter: Core **103**, Api **191**, E2E **35**, Vitest **198**. To kendte
huller er skrevet ned i designet: `settings-error` bærer **hver** Jira-indstillings fejl og står nu
ved sproggruppen (og **ingen** test påstår hvilken gruppe den bor i), og der findes ingen
formateringsvagt — leverancen efterlod fire prettier-afvigelser, som først blev fundet i hånden.
**ADO-mentions er målt 2026-08-20, og antagelsen holder.** `CONTAINS WORDS` på `System.History`
virker, så **skive 12 er ikke længere blokeret**. Elleve ting blev afgjort — se designdokumentets
afsnit 10. De fire der ændrer designet: `comments` er **preview-only** på denne server, så `updates`
(GA på **7.1**) er den primære vej; serveren kan **ikke** filtrere på mentions, fordi indekset dækker
prosaord og ikke markup, så GUID-matchet sker i klienten; det **fulde visningsnavn** er præcist
(25 af 25 bar GUID'et) hvor fornavnet gav et falsk positiv; og kommentaren er **HTML**, så skive 13
skal konvertere HTML til markdown.

## Tilbage

Skive 11 er færdig, og **næste nummererede skive er 12 (ADO-import)** — men den har en forudsætning
der ikke er indfriet, se "Åbne spørgsmål" nedenfor: ADO-mentions skulle verificeres i skive 11 og
blev det ikke, fordi målingen kræver brugerens egen instans. Kør den, før skive 12 planlægges.

Punkterne nedenfor kan i øvrigt tages i vilkårlig rækkefølge, og **uden undtagelser**: `long` som id
stod her gennem tre leverancer som det eneste punkt der kostede noget at udskyde, og den er leveret
som skive 10. Der er ikke længere noget i den kategori — alt herunder koster det samme om et halvt
år som i dag.

**Hvad skive 11 efterlod af arbejde, og hvad den bevidst ikke gjorde.** Afstemningen mod en senere
sync, `Ext*`-felterne, `TitleOverridden` og `LastSyncedAt` er **ikke** glemt: de ligger i skive 14
sammen med baggrundssyncen, fordi de beskytter mod noget der ikke findes endnu — felterne ville
blive skrevet og aldrig læst, og en vagt på dem kunne ikke bringes til at fejle. Skiven har derfor
ingen migrering. Af rigtige huller står tre: **en mislykket "Åbn sagen" er tavs på en sammenfoldet
række** (bevidst ikke rettet — se designdokumentets afsnit 10 for begrundelsen), `jira-error` på
indstillingssiden er ikke kontrastmålt (farveparret er dækket andetsteds), og **intet i suiten
kalder den rigtige instans** — den eneste vagt der er, er at intet i repoet må navngive den.

**Hvad ventetiden faktisk kostede, er værd at tage med.** Prisargumentet var sandt, men beskedent:
aftrykket voksede med cirka ét sted per leverance, og målt endte det på 11 id-relaterede steder i
tests — ikke "næsten hver test" — mens builderne aldrig rørte `Guid` overhovedet. Beslutningen kom
til sidst i hus på ergonomien, at "opgave 42" kan siges højt, frem for på en stigende regning.
Dukker der igen et punkt op der "bliver dyrere af at vente", så **mål taksten**, før det får
forrang over resten.

### Kan ligge stille

**Revisionslog med trends.** En hændelseslog ved siden af opgaverne — hvad ændrede sig hvornår
— der kan bære "hvor mange lukker jeg om ugen" og "hvor længe ligger noget i Venter på". Den er
også fundamentet for GTD's ugentlige gennemgang, som appen slet ikke understøtter. Største af
alle punkterne. Skriver hele tiden, sletter aldrig — en anden slags tabel end resten.

**"Sådan er den tænkt"-side.** Beskriver brugen i GTD-termer. Skrives som markdown-filer pr.
sprog og renderes med kæden fra skive 4 — prosa hører ikke hjemme i oversættelsesnøgler.
**Skal også sige hvad værktøjet ikke gør**, ellers lover den GTD og leverer en deadline-liste.
Materialet er designdokumentets afsnit 11.

**Resten af GTD-hullerne.** `DeferUntil` var det billigste ægte af dem, og det er leveret i skive
9 — presset på deadline-feltet er lettet, fordi noget der ikke er handlingsklart endnu ikke
længere skal vælge mellem en falsk deadline og Måske. Tilbage af designdokumentets afsnit 11 står
de dyre: **ingen projekter** — underopgaver er en tjekliste under én opgave og kan hverken have
egen deadline eller stå selvstændigt på listen — **ingen kontekster**, så deadline er fortsat den
eneste akse for alt det der *er* handlingsklart, og **ingen ugentlig gennemgang**, som er GTD's
nøglevane. Kontekstaksen er den mest indgribende af de tre: den ville omgøre designdokumentets
afsnit 2 frem for blot at lægge et felt til. Revisionsloggen ovenfor er fundamentet under
gennemgangen, så de to hænger sammen.

### Allerede planlagt i designdokumentet

ADO-import, mentions-indbakke, baggrundssync med tray og notifikationer, livscyklus og arkiv, og
pakning til en self-contained exe. Se afsnit 9. **Jira-importen er ude af listen** — den er leveret
som skive 11 og står i Færdigt-tabellen.

## Sådan køres en skive

Mønstret der har virket gennem tolv skiver (0–11):

1. Skriv en plan i `docs/plans/YYYY-MM-DD-slice-N-navn.md` med opgaver på 2–5 minutter, komplet
   kode, eksakte kommandoer og forventet output.
2. Kør én opgave ad gangen med en frisk subagent. Giv den **hele** opgaveteksten frem for at
   bede den læse planen, plus maskinens fælder fra `CLAUDE.md`.
3. Hver opgave slutter med sin egen commit. Det er derfor to agenter, der døde midt i arbejdet,
   kunne samles op frem for at gå tabt.
4. Kræv at vagt-tests ses fejle, og at rapporten indeholder fejlteksten.
5. Verificér tallene selv bagefter. Rapporter har taget fejl om grennavne og filnavne uden at
   tage fejl om indholdet.

## Næste måling — den kan kun du foretage

**ADO-mentions skulle verificeres i skive 11 og blev det ikke.** Designdokumentets afsnit 10 sagde
"verificér i skive 11, ikke i 12" — altså mens "Test forbindelse" bygges. Skiven kunne ikke:
målingen kræver et kald mod **din egen** ADO-instans med **dit eget** token, og det kan ingen agent
gøre herfra. Det er derfor en opgave til dig, og den bør køres før skive 12 planlægges — hele
mentions-indbakken (skive 13) hviler på svaret.

Azure DevOps har intet "vis mine mentions"-endpoint. Planen fra afsnit 6 er WIQL på
kommentarhistorikken:

```
SELECT [System.Id] FROM WorkItems
WHERE [System.History] Contains Words 'Thomas Hjorth Hansen'
  AND [System.ChangedDate] >= '2026-08-01'
ORDER BY [System.ChangedDate] DESC
```

Kør den mod `POST https://<server>/<collection>/<projekt>/_apis/wit/wiql?api-version=…` og se efter
tre ting: **at `Contains Words` overhovedet er tilladt på `System.History`** (den er indekseret
særskilt, og nogle serverudgaver afviser den), **hvilken `api-version` din serverudgave tager** — den
er stadig ukendt og afgør endpoints hele vejen — og **om et hit kan føres tilbage til den konkrete
kommentar**, for WIQL svarer med work item-id'er, ikke med kommentarer, så
`GET /_apis/wit/workItems/{id}/comments` skal derefter kunne matches på mention-markup.

**Læg aldrig tokenet direkte i kommandolinjen.** Sæt det i en miljøvariabel først og referér den —
ellers følger det med i fejlbeskeder, historik og enhver kopiering af kommandoen:

```powershell
$env:ADO_PAT = '<indsæt her, ikke i historikken>'
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$env:ADO_PAT"))
curl.exe -s -H "Authorization: Basic $auth" -H "Content-Type: application/json" ...
```

`curl` er et alias for `Invoke-WebRequest` i PowerShell 5.1 og tager helt andre flag, så kald
`curl.exe` eksplicit. Skriv svaret ind i designdokumentets afsnit 6 og 10, **også hvis planen ikke
holder** — især hvis den ikke holder.

## Åbne spørgsmål

- **Jira-versionen er afklaret: Data Center 10.3.24** (målt 2026-08-18), og skive 11 er bygget mod
  den. Det låste REST v2 med wiki-markup — ikke Cloud'ens ADF — og bekræftede at PAT som Bearer er
  muligt. **ADO Server-versionen er stadig ukendt**; "Test forbindelse" er bygget til at afklare den,
  og målingen ovenfor er første lejlighed.
- **Kravet til skive 11 var besluttet, mapningen med, og begge er nu bygget.** En ventende
  Jira-status kan komme med i importen bag en indstilling, default fra, og importeres **som
  `WaitingFor`** — den lander i "Venter på", ikke i deadline-sektionerne. Indstillingen er "disse
  Jira-statusser betyder ventende", ikke et filter. **Den sidste åbne designbeslutning er afgjort:**
  `Status` fødes fra kilden ved import og er derefter lokal, som `Title` og `Requester`, så en sag der
  forlader den ventende status i Jira forlader **ikke** automatisk "Venter på" hos dig. Serveren
  afgør det, ikke klienten — derfor bærer `JiraImportRow` intet `isWaiting`. Designdokumentets
  afsnit 4a.
- **Vagten har tre uafgjorte ender, alle tre bevidste.** *(1)* **Ingen minder dig om at slukke** —
  der er kun kontakten, ingen slutdato, fordi en slutdato ville kræve noget der kører ved midnat, og
  skive 9 undgik netop det ved at gøre udskudtheden beregnet. Vi valgte **synlighed** frem for
  automatik: import-skærmen siger med ord at vagten er slået til. *(2)* **Importerede pulje-sager
  bliver liggende**, når en kollega tager sagen: `Status` er lokal efter import — det er det rigtige
  design, men puljen churner mere end egne sager, så konsekvensen rammer hårdere her. En afstemning
  kræver skive 14's sync. *(3)* **`alreadyImported` gælder på tværs af vagtuger**, fordi dedup er
  `SourceId` + `ExternalKey` uden hensyn til hvornår. Formentlig rigtigt — opgaven ligger jo allerede
  på listen — men **uprøvet i brug**. Designdokumentets afsnit 4a.
- **Puljens størrelse hviler på to slags fakta.** **2** sager målt 2026-08-19, og **op til 10**
  oplyst af brugeren som en **procesgrænse** — den holder fordi rotationen kører, ikke fordi noget
  håndhæver den. Koden afhænger ikke af nogen af dem; pagineringen tager vilkårlig størrelse.
- **Ingen test kalder den rigtige Jira.** `JiraTaskSource` måles mod en falsk instans på loopback, og
  den eneste vagt på den rigtige er, at intet i repoet må navngive den. Offsetformen `+0200` og
  changelog-formen er derfor afskrevet fra en måling 2026-08-18, ikke fra en løbende test — en
  Jira-opgradering ville vise sig i brug, ikke i `dotnet test`.
