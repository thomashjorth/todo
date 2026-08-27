# Hvor projektet står

Sidst opdateret: 2026-08-27.

Konventioner og maskinens fælder: `CLAUDE.md` i roden — auto-indlæst, læs den først.
Design, datamodel og skiverækkefølge: `docs/plans/2026-08-13-todo-app-design.md`.
Sådan bruges appen: `README.md`.

**Testtal:** Core **174**, Api **310**, E2E **72**, Vitest **303** — alle grønne, målt 2026-08-27.
E2E og Vitest steg med tre, da titlen blev redigerbar; Core og Api står stille, fordi backenden kunne
det i forvejen. Før det faldt tre af tallene, da autostart blev fjernet (tolv tests slettet med
vilje), og to steg igen med sektionsovergangene (tretten lagt til). Fordelingen for alle tre står i
`CLAUDE.md`s "Testtal".
`Check.cmd` kører dem i den rækkefølge der er bærende. Bemærk at E2E-tallet her stod på **58** og i
`CLAUDE.md` på **59**, mens sandheden før genvejslagene var **61** — to tests var lagt til uden at
nogen rettede tallet. Begge steder er rettet nu; se `CLAUDE.md`s "Testtal" for hvorfor det står
skrevet frem for bare overskrevet.

## Færdigt

Skive 0–16 er leveret. Hver har en plan i `docs/plans/` med målinger, beslutninger og det der blev
rettet undervejs — de filer er detaljen, og de forældes ikke, så de er ikke gengivet her.

| Skive | Hvad den gav |
| --- | --- |
| 0–1 | Photino + Kestrel + Angular i ét vindue, kontraktgenerering, drift- og friskhedsvagter. Opgaver med deadline, note, status, underopgaver, SQLite med migrationer |
| 2–5 | Retro-import fra indsat CSV. Indstillingsside og lokalisering (da/en). Noter i CommonMark, renderet. "Venter på" og "Måske" som statusser |
| 6–8 | TypeScript strict. WCAG AA i begge temaer med en kontrastvagt der måler i browseren. Alt-genvejssystemet |
| 9–10 | Startdato (`DeferUntil`), beregnet frem for gemt. `long` som id, med en håndskreven migrering |
| 11–12 | **Jira-import** (DC 10.3.24) og **ADO-import** (Server, api-version 7.1). `ITaskSource` fik sin anden implementation, og hvor den skar forkert står i skive 12's plan |
| 16 | **Pakning:** `Publish.cmd` giver to filer, exe'en og dens ikon, og prøver exe'en bagefter. Skiven gav også autostart som en indstilling; den blev fjernet igen 2026-08-25 på brugerens ønske |

Uden for skiverne: app-ikon og `Todo.cmd`, feature-mapper, testdata-builders, `ApiTest`/`BrowserTest`,
link til API-dokumentationen på health-linjen, Jiras vagt-statusser, uddelegering, accordion på
indstillingssiden, formaterings- og linjeskiftsvagter, søgning i titel og note med `Alt+K`, og mest
presserende først inde i hver sektion: i-gang-opgaver løftet øverst af klienten, og under dem
serverens rækkefølge — deadline, derefter startdato. Og **side by side fra `xl`** (≥ 1280 px
vinduesbredde): listen i venstre spalte, detaljerne i højre, med auto-valg af den første opgave på
skærmen — planen er `docs/plans/2026-08-21-side-by-side-design.md`, og de fem beslutninger står der
med deres begrundelser. Og **importens forslag om at lukke en løst sag**: står en hentet ADO- eller
Jira-sag i en status du kalder færdig, mens opgaven stadig er åben her, tilbyder importen at lukke den
— planen er `docs/plans/2026-08-24-import-closure-design.md`. Og **to genvejslag oven på Alt-laget**:
`Alt+1`–`9` vælger den n'te valgbare række på listen, og `Alt+Shift+bogstav` går direkte til et af
detaljepanelets otte felter — planen er `docs/plans/2026-08-24-keyboard-shortcuts-design.md`, og de fem
beslutninger står i dens afsnit 2. Og **animationer når en opgave skifter sektion**: rækken morfes til
sin nye plads med View Transitions, som overlever at `<li>`'en destrueres undervejs — planen er
`docs/plans/2026-08-25-section-transitions-design.md`. Virker i **begge** udgaver af layoutet, men læs
afsnit 8 og 8b før du rører noget: `::view-transition`-træet ligger i top-laget og klippes ikke af den
rullende spalte, så side om side krævede en nestet gruppe plus **appens ene rigtige CSS-regel** for
ikke at male oven på health-linjen. Fjernes en af mekanikkens tre dele, er bleedet tilbage, og
animationen ser stadig rigtig ud.

Og **titlen kan redigeres** fra 2026-08-27: et felt øverst i detaljepanelet med `Alt+Shift+I`, som
også giver højre spalte side om side en overskrift. Planen er
`docs/plans/2026-08-27-editing-a-title-design.md`, og den er værd at læse for **hvad den ikke gjorde**:
backenden og storen kunne det i forvejen, så leverancen var ét felt, én metode og to nøgler. To ting
at kende, hvis du rører feltet — en tom titel **ruller den gamle tilbage** frem for at vise en fejl
(DOM-skrivningen i `saveTitle` er bærende, samme fælde som `[checked]`), og gemningen må **ikke** gå
gennem den generiske `save()`, som ville sende `title: undefined` og få serveren til at afvise tavst.
Planens afsnit 5 bærer desuden en rettelse af en påstand i `CLAUDE.md`, som kostede en
fejlforudsigelse.

## Næste skridt

**Først, og ikke en skive: statusvælgerens popup har ikke mørkt tema.** Brugeren viste 2026-08-27 et
skærmbillede, hvor appen står i mørkt tema mens `<select>`-popup'en males hvid, og bad om at få det
rettet efter titlen. Fire ting er målt, så de ikke gættes igen: `scheme-light-dark` **er** på `<body>`
i `src/Todo.Web/src/index.html`, så årsagen er ikke en manglende `color-scheme`; der er **ingen**
manuel tema-toggle, så Tailwinds `dark:` er OS-indstillingen, og `color-scheme: light dark` *burde*
derfor resolve til dark; det peger på **WebView2's native popup-chrome** frem for på CSS'en; og
popup'en er ikke DOM, så `ContrastTests` kan ikke måle den — samme handel som `<datalist>`, som står
dokumenteret i `task-detail.html`. Mål i **Photino-vinduet**, ikke i en browser: de to runtimes har
hver sin popup-implementering.

**Skive 13, mentions-indbakken**, er den næste nummererede. Kravene står i designdokumentets afsnit 9,
punkt 13 — læs dem der frem for her. Tre ting er værd at have i baghovedet, før planen skrives:

- Et **pull request er ikke et work item**, så de to slags omtaler kommer fra hver sin kilde og skal
  mødes i én indbakke.
- **PR-tilstanden er en tredje rundtur** og kan derfor ikke ligge i WIQL'en.
- **Aldersgrænsen skal genbruge `ado.defaultDeadlineDays`' præcedens felt for felt**: ikke-nullable
  `int`, `default:` i kontrakten, standarden i **læselaget**, ingen række gemt for standardværdien.
  Læs `AdoDefaults` og `AdoSettingsReader` først — ellers vælter de tre tests der påstår om hele
  `Settings`-tabellen.

**To beslutninger mangler, og de skal træffes eksplicit frem for gættes:**

1. Skal et **`abandoned`** pull request filtreres væk på lige fod med `completed`? En forladt
   gennemgang kan stadig kræve et svar, så det er ikke en aflæsning.
2. `0` dage betyder **ingen grænse** (afgjort 2026-08-21) — men det er kun halvdelen. Skal en
   hjælpetekst sige det, må den ikke sige "0 betyder ingen omtaler", som er det stik modsatte.

Skive **14** (baggrundssync, tray, notifikationer) er stadig uleveret, og den hænger sammen med det
der blev fjernet 2026-08-25: appen kunne startes ved login, men der er **ingen tray**, så den åbnede
et vindue i ansigtet på brugeren. Skal noget lignende komme igen, er tray'en det der gør det til det
det skal være.
Skive 14 bærer også `Ext*`-felterne, `TitleOverridden` og `LastSyncedAt` — de er ikke glemt, de
beskytter mod noget der ikke findes endnu, og en vagt på dem kunne ikke bringes til at fejle i dag.

Skive **15** er livscyklus og arkiv.

## Målinger kun du kan lave

**De to genvejslag i Photino-vinduet — ingen af de to målinger nedenfor er lavet.** Playwright trykker
`Alt+Shift+D` i Chromium, og suiten er grøn, men det siger intet om dit tastatur i det rigtige vindue.
Begge står derfor **åbne**, og ingen har prøvet dem:

1. **Giver `Alt+Shift+D` `event.key === "D"`?** Opslaget er `event.key.toLowerCase()`, så `"D"` er
   nok. Er svaret et andet — et dødt tegn, eller `event.key` som noget helt tredje — skal opslaget
   bygges på `event.code` i stedet. Det er en rettelse i `app.ts` og `shortcuts/shortcut-key.ts`
   (opgave 4 i `docs/plans/2026-08-24-keyboard-shortcuts.md`), **ikke** et nyt design.
2. **Stjæler Windows' layoutskift på `Alt+Shift` kombinationen?** Skiftet udløses på *slip* uden et
   bogstav, så `Alt+Shift+D` bør være fri — men "bør" er ikke en måling. Og et grønt svar er **svagt**,
   hvis maskinen kun har ét tastaturlayout installeret: så sker der ingenting under alle omstændigheder,
   og målingen siger intet om en maskine med to.

Sådan gøres det: start appen med `Todo.cmd`, prøv de otte `Alt+Shift`-bogstaver på en åben opgave
(`D S O N T V U L`) og de ni `Alt+ciffer` på listen, og skriv resultatet her. Cifrene kan i øvrigt
**kun** prøves her — Chrome binder `Alt+1`–`8` til faneskift og `Alt+9` til sidste fane — så en
browser siger ingenting om dem.

**Sætter din Jira en resolution, når en sag flyttes til en færdig-status?** Åbn en løst sag og se om
resolutionsfeltet er udfyldt. Gør den det, forsvinder sagen ud af JQL'en, og `jira.doneStatuses`, den
fjerde rolle og de tilhørende sprognøgler er en gren der er **død hos dig** — funktionen virker kun for
ADO. Gør den det ikke, virker begge. Målingen ændrer ikke koden, men den ændrer hvad du kan forvente,
og den er ikke lavet: skive 12's beslutning var at holde sig inden for det forespørgslerne allerede
henter.

**Side by side i det rigtige vindue.** `xl:h-screen` er `100vh`, og Playwright på 1400 px måler et
*browser*-viewport. Om `100vh` er vinduets klienthøjde i Photino/WebView2 — og om to spalter faktisk
ser rigtige ud i et maksimeret vindue på din Full HD-skærm — kan ingen agent måle her. Start appen,
maksimér, og se om højre spaltes rulning slutter ved vinduets kant frem for at stikke ud. Er den
forkert, er det `xl:h-screen` i `app.html` der skal skiftes, ikke spalterne.

De tre nedenfor er skive 12's. De kræver din egen instans og dit eget token, så ingen agent kan køre dem.
Opskriften — variabler, kommandoer og fælderne — står i skive 12's plan under "Måling 0". **Læg aldrig
tokenet i kommandolinjen**; sæt det i `$env:ADO_PAT` først og referér det.

- **0e — én rigtig beskrivelse. Denne blokerer noget.** Måling 0b printede med vilje kun feltnavne, så
  ingen har set instansens rigtige rich text. Derfor er HTML → markdown udskudt: `AdoTaskSource` giver
  ADO's HTML videre uændret, mens kontraktens `note`-beskrivelse siger *"converted to CommonMark"* —
  en sætning der **endnu ikke er sand**, og som skal indfries eller rettes. Skive 13 skal have
  **samme** konverter til kommentar-HTML, så én konverter bygget mod to målte prøver slår to bygget
  mod nul.
- **0f — hvilken form har `System.CreatedBy`?** Et objekt med `displayName`, eller den ældre streng
  `Navn <adresse>`? `AdoTaskSource` læser **begge** frem for at gætte, og begge er dækket af en test —
  men den ene test beskriver noget serveren aldrig sender, og målingen siger hvilken.
- **0g — bærer `_apis/wit/workitemtypes` en `states`-liste?** Indtil det er målt, hentes
  tilstandsnavnene af **dine egne sager**, og prisen er brugervendt: en tilstand ingen af dine sager
  står i lige nu kan ikke vælges som ventende, så `Blocked` kan ikke markeres på en dag hvor intet er
  blokeret.

## Åbne spørgsmål

- **Ingen test kalder den rigtige Jira eller ADO.** Begge kilder måles mod en falsk instans på
  loopback, og den eneste vagt på den rigtige er `NoRealInstanceTests`: intet i repoet må navngive
  den. Målingerne fra 2026-08-18 og -08-20 er derfor **afskrevet**, ikke løbende — en
  serveropgradering ville vise sig i brug og ikke i `dotnet test`.
- **To ejede afvigelser fra skive 12** er kendte og ikke lukket: `note` er rå HTML (0e ovenfor), og
  tilstandslisten kommer af dine egne sager (0g). Begge står skrevet, hvor koden gør det.
- **Vagt-rotationen har tre uafgjorte ender, alle tre bevidste.** Ingen minder dig om at slukke —
  der er kun kontakten, fordi en slutdato ville kræve noget der kører ved midnat. Importerede
  pulje-sager bliver liggende når en kollega tager sagen, fordi `Status` er lokal efter import.
  Og `alreadyImported` gælder på tværs af vagtuger. Designdokumentets afsnit 4a.
- **To latente fejl i det manuelle `[checked]`-mønster.** `jira-on-duty` og `ado-include-waiting` kan
  vise et flueben serveren afviste. Det skete én gang for alvor — i autostart-fluebenet, som er
  fjernet igen — og rettelsen fulgte funktionen ud. De fejler kun hvis
  serveren afviser, og ingen af dem har en kodet grund til det i dag. Rører du en af dem, så skriv
  elementet tilbage fra signalet. Signal forms blev overvejet og **fravalgt** 2026-08-21 — se
  `CLAUDE.md`.
- **Dansk tilbage i koden.** Sproget er engelsk i C#-kilden, scripterne og `README.md`. Endnu ikke
  omskrevet: test-kommentarer og testdata (~230 linjer), frontend-kommentarer (~215) og
  dokumentationen (~4.300). De to første er en overkommelig omgang; den sidste er en anden
  størrelsesorden. Tallene er en **nedre grænse** — de tæller linjer med `æøå`, og meget dansk har
  ingen af dem.
- **`src/Todo.Web/README.md` er Angulars standardfil** og anbefaler `ng serve`/`ng test`, som ikke er
  den her app's kommandoer. Harmløs, men vildledende.
- **E2E-suiten er set flakke én gang** (2026-08-21): 46 af 47 på en kørsel, alle 47 på den næste og
  de to derefter, med samme kode. Testen blev **ikke** identificeret — udskriften var filtreret, og
  navnet gik tabt. Det står her, så en rød E2E der bliver grøn af sig selv ikke koster en time; sker
  det igen, så gem hele udskriften frem for at filtrere den, og noter navnet her.

## Sådan køres en skive

Mønstret der har virket gennem sytten skiver:

1. **Mål før du planlægger.** De dyreste fund er kommet af at måle ét lag længere ude end planen
   tænkte — hvad generatoren skriver, hvad NSwag udsender, hvad `marked` renderer, hvad en publish
   faktisk lægger på disken.
2. Skriv planen i `docs/plans/YYYY-MM-DD-slice-N-navn.md` med opgaver på 2–5 minutter, komplet kode,
   eksakte kommandoer og forventet output.
3. Kør én opgave ad gangen. Giv **hele** opgaveteksten frem for at bede om at planen læses, plus
   maskinens fælder. Sig eksplicit at planen kan være forkert: over fem leverancer fandt
   gennemgangene omkring hundrede planfejl og nul udførelsesfejl.
4. Hver opgave slutter med sin egen commit.
5. **Kræv at hver vagt ses fejle**, med den ordrette fejltekst i rapporten. En mutation der fælder
   ingenting er et fund, ikke en formalitet — skriv ned hvorfor assertionen ikke er en vagt frem for
   at tilføje en test for syns skyld.
6. Verificér tallene selv bagefter. Rapporter har taget fejl om grennavne og filnavne uden at tage
   fejl om indholdet.

## Ønsket, men ikke placeret

Kan ligge stille; ingen af dem bliver dyrere af at vente. (Det blev målt engang: `long` som id stod
her som "bliver dyrere" gennem tre leverancer, og aftrykket voksede med cirka ét sted per leverance.
**Mål taksten**, før noget får forrang med det argument.)

- **Revisionslog med trends.** En hændelseslog ved siden af opgaverne — hvad ændrede sig hvornår —
  der kan bære "hvor mange lukker jeg om ugen" og "hvor længe ligger noget i Venter på". Største af
  punkterne, og fundamentet under GTD's ugentlige gennemgang. Skriver hele tiden, sletter aldrig.
- **"Sådan er den tænkt"-side.** Brugen i GTD-termer, som markdown pr. sprog renderet med kæden fra
  skive 4 — prosa hører ikke i oversættelsesnøgler. **Skal også sige hvad værktøjet ikke gør**,
  ellers lover den GTD og leverer en deadline-liste. Materialet er designdokumentets afsnit 11.
- **Resten af GTD-hullerne.** Ingen projekter, ingen kontekster, ingen ugentlig gennemgang.
  Kontekstaksen er den mest indgribende: den ville omgøre designdokumentets afsnit 2 frem for at
  lægge et felt til.
*(Animationer side om side stod her i nogle timer 2026-08-25 og er nu leveret — se "Uden for
skiverne". Det der lukkede den var afsnit 8b: nesting plus én CSS-regel, målt frem for gættet.)*
