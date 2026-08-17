# Hvor projektet står

Sidst opdateret: 2026-08-17

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

Uden for skiverne: app-ikon og titel, `Todo.cmd`-launcher, omstrukturering til feature-mapper,
testdata-builders, og `ApiTest`/`BrowserTest`-basisklasser.

Uden for skiverne, fundet ved et tilfælde: **`TaskStore` kunne lade et forsinket ældre load
overskrive den nyeste liste.** `setShowCompleted` og `setShowSomeday` sætter hver sit signal og
kalder derefter `load()`, så to genindlæsninger kan være i luften på én gang uden noget der
ordner svarene. Ankom det ældste sidst, stod listen forkert indtil noget andet udløste en ny
indlæsning — slås begge kontakter hurtigt efter hinanden, kunne "Måske"-sektionen forsvinde.
Fundet fordi `ContrastTests` flakkede (7–9 s frem for 2 s), ikke fordi nogen ledte efter det.
`load()` har nu en sekvenstæller, og regressionstesten
`should not let a slow earlier load overwrite a newer list` blev set fejle først.

## Tilbage

Skive 8 er færdig, og ingenting er planlagt efter den. Punkterne nedenfor kan tages i vilkårlig
rækkefølge — **med én undtagelse**, som står først, fordi den er den eneste der koster noget at
udskyde.

**`long` som id — besluttet, planlagt, og bevidst udskudt 2026-08-17.** `Guid` v4 erstattes af
`long` på `TaskItem`, `SubTask` og `UserAlias`. Planen ligger **færdig** i
`docs/plans/2026-08-17-long-ids.md`, med migreringen målt igennem mod rigtig SQLite —
inklusive beviset for at en `CAST` af Guid-strenge ødelægger data, og opskriften der ikke gør.
Den blev skrevet som skive 8 og derefter lagt til side; Alt-genvejene overtog nummeret.

**Bemærk premisset:** branchen gik ikke fra GUID til `long` — den gik fra tilfældig v4 til
tidsordnet UUIDv7 (`Guid.CreateVersion7()`, .NET 9+). Argumentet her er SQLite-specifikt og
ergonomisk, ikke at følge en trend. Fragmentering betyder intet ved denne størrelse.

**Og bemærk prisen:** den **bliver dyrere for hver skive der lægges imellem**, fordi hver ny
skive tilføjer kode og tests der rører id'er. Aftrykket er målt til at være mindre end tidligere
antaget — builderne rører slet ikke `Guid`, og 11 steder i tests, ikke "næsten hver test" — men
det tal vokser. Udskydes den igen, er det værd at spørge hvorfor.

### Kan ligge stille

**Revisionslog med trends.** En hændelseslog ved siden af opgaverne — hvad ændrede sig hvornår
— der kan bære "hvor mange lukker jeg om ugen" og "hvor længe ligger noget i Venter på". Den er
også fundamentet for GTD's ugentlige gennemgang, som appen slet ikke understøtter. Største af
alle punkterne. Skriver hele tiden, sletter aldrig — en anden slags tabel end resten.

**"Sådan er den tænkt"-side.** Beskriver brugen i GTD-termer. Skrives som markdown-filer pr.
sprog og renderes med kæden fra skive 4 — prosa hører ikke hjemme i oversættelsesnøgler.
**Skal også sige hvad værktøjet ikke gør**, ellers lover den GTD og leverer en deadline-liste.
Materialet er designdokumentets afsnit 11.

**Swagger-link på health-linjen.** Klik på "API: ok" åbner API-dokumentationen. Kræver en
UI-pakke (Scalar eller Swashbuckles), da .NET 10 ikke har en indbygget. Linket **skal** gennem
`/api/system/open-link` — ellers navigerer Photino-vinduet væk fra appen uden vej tilbage.
Endpointet findes allerede fra skive 4; tilføj `http`/`https` er nok, de er hvidlistet.

### Allerede planlagt i designdokumentet

Jira-import, ADO-import, mentions-indbakke, baggrundssync med tray og notifikationer,
livscyklus og arkiv, og pakning til en self-contained exe. Se afsnit 9.

## Sådan køres en skive

Mønstret der har virket gennem ni skiver:

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
- **Serverversioner** for Jira Data Center og ADO Server afgør endpoints og API-versioner.
  "Test forbindelse" i indstillingerne er bygget til at afklare det.
