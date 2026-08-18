# Hvor projektet står

Sidst opdateret: 2026-08-18

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
| 8 | Alt-genvejssystemet: hold Alt for at se mærkaterne, og Alt+O/I/S/N/V/M udfører elementets aktiveringshandling — links følges, de to kontakter skifter og tager fokus, feltet får fokus | `2026-08-17-alt-shortcuts.md` |
| 9 | Startdato (`DeferUntil`): en opgave ligger i Udskudt indtil dagen den begynder. Udskudtheden er **beregnet** af dagens dato, ikke gemt som en status, så intet skal køre ved midnat. Overskredet slår Udskudt — se designdokumentets afsnit 10 | `2026-08-17-slice-9-defer-until.md` |
| 10 | `long` som id: `Guid` er væk fra `TaskItem`, `SubTask` og `UserAlias`, og id'et tildeles af SQLite ved indsættelse, så **"opgave 42" kan siges højt**. Migreringen er skrevet i hånden — en `CAST` af Guid-strenge ville have flettet rækker sammen — og en vagt stiller rigtige Guid-rækker op foran den og kræver dem intakte bagefter | `2026-08-17-long-ids.md` |

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

## Tilbage

Skive 10 er færdig, og ingenting er planlagt efter den. Punkterne nedenfor kan tages i vilkårlig
rækkefølge, og **nu uden undtagelser**: `long` som id stod her gennem tre leverancer som det eneste
punkt der kostede noget at udskyde, og den er leveret som skive 10. Der er ikke længere noget i den
kategori — alt herunder koster det samme om et halvt år som i dag.

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

Jira-import, ADO-import, mentions-indbakke, baggrundssync med tray og notifikationer,
livscyklus og arkiv, og pakning til en self-contained exe. Se afsnit 9.

## Sådan køres en skive

Mønstret der har virket gennem elleve skiver:

1. Skriv en plan i `docs/plans/YYYY-MM-DD-slice-N-navn.md` med opgaver på 2–5 minutter, komplet
   kode, eksakte kommandoer og forventet output.
2. Kør én opgave ad gangen med en frisk subagent. Giv den **hele** opgaveteksten frem for at
   bede den læse planen, plus maskinens fælder fra `CLAUDE.md`.
3. Hver opgave slutter med sin egen commit. Det er derfor to agenter, der døde midt i arbejdet,
   kunne samles op frem for at gå tabt.
4. Kræv at vagt-tests ses fejle, og at rapporten indeholder fejlteksten.
5. Verificér tallene selv bagefter. Rapporter har taget fejl om grennavne og filnavne uden at
   tage fejl om indholdet.

## Åbne spørgsmål

- **ADO-mentions** er den mest usikre antagelse i hele designet. Azure DevOps har intet
  "vis mine mentions"-endpoint; planen er WIQL på kommentarhistorik. **Verificér mod jeres egen
  instans, før der bygges noget ovenpå.**
- **Jira-versionen er afklaret: Data Center 10.3.24** (målt 2026-08-18). Det låser REST v2 med
  wiki-markup — ikke Cloud'ens ADF — og bekræfter at PAT som Bearer er muligt. Jira 10 fjernede
  dog forældede REST-endpoints, så skive 11's plan skal måles mod instansen. Se designdokumentets
  afsnit 10. **ADO Server-versionen er stadig ukendt**; "Test forbindelse" er bygget til at
  afklare den.
- **Ét krav til skive 11 er besluttet**, men én beslutning i det er ikke: en ventende Jira-status
  (`Afventer general`, `Afventer PO/FA`) skal kunne komme med i importen bag en indstilling,
  default fra. **Om sådan en sag skal mappes til `WaitingFor` eller blot medtages som `Open`, er
  ikke afgjort** — og det afgør indstillingens form, så det skal besluttes før planen skrives.
  Designdokumentets afsnit 4a.
