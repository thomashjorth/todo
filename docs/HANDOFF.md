# Hvor projektet står

Sidst opdateret: 2026-09-01.

Konventioner og maskinens fælder: `CLAUDE.md` i roden — auto-indlæst, læs den først.
Design, datamodel og skiverækkefølge: `docs/plans/2026-08-13-todo-app-design.md`.
Sådan bruges appen: `README.md`.

**Testtal:** Core **186**, Api **399**, E2E **83**, Vitest **358** — alle grønne, målt med
`Check.cmd` 2026-09-08. Kun Api flyttede sig, med én: sproggennemgangen lagde `SourceLanguageTests`
til med to påstande og slettede den ene igen, da efterslæbslisten var tom. Fordelingen og
begrundelsen står i `CLAUDE.md`s "Testtal". `Check.cmd` kører dem i den rækkefølge der er bærende.

**De fire tal var sande i forvejen, og det er værd at sige efter sidste gang.** Baseline'en
181/323/81/330 stod ens i denne fil og i `CLAUDE.md`, så de fire stigninger kan kun betyde nye
tests — ikke et regnskab der driver. Sidste gang stod E2E på **73** her og **75** i `CLAUDE.md`
mens sandheden var **77**, og gangen før stod de tre tal på 58, 59 og 61.

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

Og **den ugentlige gennemgang** er leveret 2026-08-28: en sjette skærm på `/review` med `Alt+G`, som
fører dig gennem fem grupper i GTD's rækkefølge — overskredet, venter på andre, starter i denne uge,
uden deadline, måske — og slutter med et tryk der siger "nu er jeg igennem". Ugedag og klokkeslæt er
indstillinger (standard fredag 15:00), og en markør på navigationslinket siger at ugens gennemgang
mangler. Designet er `docs/plans/2026-08-27-weekly-review-design.md` med de ti beslutninger i afsnit 0,
leveranceplanen `docs/plans/2026-08-27-weekly-review.md`. **Uden skivenummer**, af samme grund som
Swagger-linket og de otte andre: numrene er mærkater og ikke en rækkefølge længere, siden skive 16 blev
leveret før 13, 14 og 15.

Fire ting at kende, hvis du rører den:

- **Forfaldet er beregnet, ikke udløst.** Samme regel som udskudtheden — intet kører ved midnat, og
  appen tåler at have været lukket i tre uger. Kadencen er **fast ugedag, ikke syv dage**, og gættet
  falder den anden vej: en gennemgang taget tirsdag tilfredsstiller den igangværende periode, men køber
  ikke en uge.
- **Skærmen har ingen egen kopi af listen, og det er hele designet.** Rækkerne er `li[appTaskRow]` og
  læser `TaskStore`, så en beslutning flytter rækken ud af dens gruppe uden en linje ekstra.
  `GET /api/review` bærer derfor kun forfaldstilstanden. Vagten er
  `Deciding_from_the_review_moves_the_row_out_of_its_group`, set fælde med en egen kopi.
- **`showSomeday` er ikke længere en indlæsningsparameter**, men et rent visningsfilter: gennemgangen
  skal have Måske-rækkerne uanset fluebenet. `showCompleted` er uændret, fordi Fuldført vokser for
  evigt. Omlægningen fjernede desuden halvdelen af den reload-race `CLAUDE.md` advarer mod.
- **`lastCompletedAt` har sin egen rute** og går aldrig gennem `PUT /api/settings`, som er en fuld
  erstatning der ville rydde den ved enhver anden gemning. Samme grund som tokenernes ruter.

Og **importen afstemmer nu det den allerede har hentet**, leveret 2026-09-01: en opgave der er åben
her, men hvis nøgle faldt ud af dagens hentning, slås op i kilden, og forhåndsvisningen foreslår at
**lukke** den hvis sagen er løst *eller tildelt en anden* — og at **genåbne** en opgave der er færdig
her mens kilden siger andet. Det omgør fravalget i
`docs/plans/2026-08-24-import-closure-design.md`s afsnit 9; designet er
`docs/plans/2026-08-31-source-reconciliation-design.md` og leveranceplanen ligger ved siden af.
Ingen migrering, ingen ny datamodel og intet nyt endpoint: `ITaskSource` fik **én** metode,
`ReconcileAsync`, og svarene blev til almindelige forhåndsvisningsrækker med nye felter.

Fire ting at kende, hvis du rører afstemningen:

- **Kun de nøgler der er åbne her og manglede i hentningen bliver spurgt om** — en håndfuld, ikke
  arkivet. En opgave der er færdig her afstemmes aldrig.
- **De to kilder spørger forskelligt, og det er ikke en inkonsekvens.** ADO læser nøglerne i ét
  batch-kald; Jira går pr. nøgle, fordi `key IN (…)` afvises på en nøgle den ikke kender — altså
  præcis den slettede sag. **ADO's halvdel er målt 2026-09-11 og holder** — se
  `docs/plans/2026-09-14-closing-a-deleted-item-design.md` afsnit 1. Jiras præmis er stadig umålt;
  se "Målinger kun du kan lave".
- **Blev ingen af flere nøgler genkendt, vises én notits frem for en række pr. nøgle.** Jira DC
  skjuler en sag du ikke må browse bag 404, så en mistet rettighed ligner en massesletning. Om den
  grænse nogensinde udløser i praksis afhænger af den tredje måling nedenfor.
- **Lukkegrunden rejser med på lukkerækken**, fordi serveren tager beslutningen igen: en sag der er
  overdraget står i en åben tilstand, så en ubetinget færdig-tjek ville springe hver eneste
  overdragelse over og tælle den i `skipped`. Prisen er at gen-beslutningen er svagere for
  `unassigned` end for `resolved` — ejerskab er et faktum kun kilden kan afgøre, og importen ringer
  ikke ud.

Og **`color-scheme` er flyttet til `<html>`** 2026-08-27, med to eksplicitte klasser — `scheme-light` og
`dark:scheme-dark`. På `<body>` nedarvedes egenskaben til hele siden, men **roden** blev stående på
`normal`, og browseren maler popup'en, rullebjælkerne og lærredet efter rodens skema. Det rettede
rullebjælkerne og lærredet.

**Men den hvide `<select>`-popup var en anden fejl, og de to skal blive ved at være adskilt — at folde
dem sammen kostede to forgæves rettelser.** `color-scheme` når slet ikke popup'en i WebView2, målt to
gange: hverken den resolverede `light dark` eller et eksplicit `dark` gjorde den mørk, og den kørende
app blev verificeret til faktisk at servere hver rettelse. Årsagen var, at popup'en males af
**select'ens egen baggrund**, som var `rgba(0,0,0,0)` mens `color` var `gray-100` — WebView2 falder
tilbage på hvid, og den lyse tekst landede der. Statusvælgeren har nu `bg-gray-50 dark:bg-gray-800`,
panelets egne farver, så siden ser uændret ud. Sprogvælgeren på indstillingssiden havde mønsteret i
forvejen og har aldrig haft fejlen — den var det arbejdende eksempel.

Begrundelsen med tal står i `CLAUDE.md`s stylingafsnit. Vagten er `ColorSchemeTests` med **to**
påstande, én pr. fejl: rodens *brugte* skema gennem systemfarven `Canvas`, og at hver `<select>` maler
en uigennemsigtig baggrund. Ingen af dem kan se popup'en selv — den er browserens eget vindue og kommer
ikke med i et screenshot — så de måler det popup'en læser.

## Næste skridt

**Planlagt og ikke bygget: en slettet sag skal kunne lukkes fra importen.** Afstemningen
*rapporterer* allerede en sag kilden ikke kender, men tilbyder ingenting — rækken står grå og uden
flueben, så den skal findes i hånden. Designet er
`docs/plans/2026-09-14-closing-a-deleted-item-design.md` og leveranceplanen ligger ved siden af;
syv opgaver, begge kilder, ingen migrering. **Bemærk prisen i designets afsnit 6:** Done-filteret
står, så den viser den næste sag der slettes mens opgaven er åben — ikke de to der allerede er
lukket her.

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

**Hjælperen og "Forbindelser" (leverance 4 og 5).** Kæden er målt i E2E med den rigtige vært, den
rigtige hjælper og en falsk Jira, men alt på loopback — det følgende kræver din PC, dine tokens og
NAS'en:

1. **Hav dine Jira- og ADO-tokens klar før første `Todo.cmd` efter fletningen.** Migreringen
   `TokensLeave` sletter dem fra den lokale database, og fra da af ligger de kun i hjælperen.
2. **Lokalt:** start `Helper.cmd` og `Todo.cmd`, og åbn Indstillinger → Forbindelser. Vises
   hjælperens side i gruppen? Skriv `http://127.0.0.1:5180`, lav en nøgle, kør `SetHelperKey.cmd`
   med hash'en, og genstart `Todo.cmd`. Siger siden "Forbundet til serveren."?
3. **Tokens:** skriv Jira- og ADO-adresser og tokens på siden, og prøv "Test forbindelse" og en
   import på begge skærme. Virker AI'en (hjælperen kalder Ollama på `127.0.0.1:11434`)?
4. **Fra NAS'en:** spørger Edge om Local Network Access, når NAS'ens side viser hjælperens? Det kan
   E2E ikke se, fordi begge ender er loopback dér. Svar ja, og sig hvad der stod.
5. **`frame-ancestors`:** hjælperen tillader kun at blive vist af serverens origin, når den kender den.
   Når du NAS'en på en anden adresse end den hjælperen har (LAN-IP mod Tailscale-navn, eller fra
   telefonen), bliver rammen blokeret, og gruppen viser sætningen og knappen i stedet. Sig hvilke
   adresser du bruger, så kan reglen tage dem med.
6. **Hjælperen starter ikke med Windows** — det er ikke bygget. Indtil da er det `Helper.cmd`.
7. **Flytningen:** `ExportData.cmd` og `docs/NAS.md`, afsnittet "Flyt dine data".

**Web-udgavens første leverance (2.0.0, leverance 1) — Edges app-vindue i stedet for Photino.** Intet
af det følgende kan en agent måle, fordi det kræver et vindue på dit skrivebord:

**NAS'en (leverance 3):** CI blev grøn 2026-10-26 for første gang, og `ghcr.io/thomashjorth/todo:latest`
kan hentes uden login. Følg `docs/NAS.md`. Kun du kan sætte containeren og den planlagte opgave op i
DSM, og kun du kan se om `id`'s tal og ejeren af `data`-mappen passer, og om DSM har
`docker compose` eller `docker-compose`.

0. **Kør `SetPassword.cmd` én gang** (leverance 2). Den beder om passwordet to gange og gemmer kun
   hashen. Uden den nægter `Todo.cmd` at starte og siger hvorfor. Prøv også at logge ind med et
   forkert password, og at et password med æøå virker — hashen er målt, men ikke gennem din tast.
1. **Luk den gamle app først.** Kører den Photino-udgave du har åben nu, fra `bin\Release`, nægter
   `Todo.cmd` at bygge og siger det med pid. Målt 2026-10-26: uden den besked fejler bygningen med
   en MSB3027 om låste filer.
2. **Åbner `Todo.cmd` et Edge-vindue uden adresselinje på `http://127.0.0.1:5180/`, i 480×1000?**
3. **Stopper værten når du lukker konsolvinduet?** Antaget, ikke målt: `dotnet run` starter appen
   som et barnebarn, og Windows sender lukningen til alle processer i konsollen. Tjek bagefter med
   `Get-NetTCPConnection -LocalPort 5180 -State Listen` — svarer den noget, kører værten stadig.
4. **Åbner et andet dobbeltklik et nyt vindue på den kørende vært** frem for at fejle?
5. **Åbner "Åbn sagen", et betydningshit og et link i en note en fane i Edge**, mens app-vinduet
   bliver stående? Og giver et `file:`-link i en note sætningen "Kun web- og mail-links kan åbnes."?
6. **Genvejene i app-vinduet:** de ti `Alt`-bogstaver, `Alt+1`–`9` på listen og de ni `Alt+Shift`
   i panelet. Chrome binder `Alt+1`–`8` til faneskift i en almindelig fane; om app-vinduet gør det
   samme, er umålt. Målingerne nedenfor, der nævner Photino-vinduet, gælder nu app-vinduet.

**Bliver `type="time"`s vælger-popup mørk i Photino-vinduet?** Ny 2026-08-28 med gennemgangens
indstillinger. Åbn Indstillinger → Gennemgang under et mørkt tema og klik på urikonet. Er popup'en
hvid, er det samme klasse af fejl som statusvælgerens, og faldbacket er navngivet i designets afsnit
5: to `<select>` for time og minut, altså det mønster der nu er bevist tre steder. **Bemærk hvad der
*ikke* er årsagen, hvis det sker** — `color-scheme` er udelukket to gange på præcis den slags popup,
så mål feltets egen baggrund frem for at flytte `color-scheme` igen. Feltets *værdi* er derimod
allerede dækket: `'time'` stod i forvejen i kontrastvagtens hvidliste.

**Virker `Alt+G` i det rigtige vindue?** Dækket af en E2E i Chromium, men et nyt bogstav er den ene
risiko maskineriet ikke kan gå god for. Står sammen med de to genvejslag-målinger nedenfor, som
heller ikke er lavet.

**Er fredag 15:00 den rigtige standard for dig?** Ikke en kodemåling men en brugsmåling: bliver
markøren konsekvent ignoreret til mandag, er ugedagen forkert valgt frem for at vanen er det.
Indstillingen findes netop derfor.

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

**Afstemningens tre præmisser — to er målt 2026-09-11, én står tilbage.** De blev sat op som Task 0
i `docs/plans/2026-08-31-source-reconciliation.md`, og leverancen kørte videre uden svarene. De to
ADO-præmisser er besvaret nu, mod den rigtige instans og med brugerens eget token; opskriften og
svarene står i `docs/plans/2026-09-14-closing-a-deleted-item.md` under Task 0. En falsk instans kan
ikke svare på den sidste: en `FakeJira` der udsender vores egen antagelse måler sig selv.

1. **Svarer Jiras `issue/{key}` 404 eller 403 på en sag du ikke må se?** **Stadig umålt, og nu den
   vigtigste af egen kraft.** Kun 404 læses som "kender den ikke"; alt andet kastes. Er svaret
   **403**, udløser alle-fraværende-grænsen aldrig i praksis, og den er så et sikkerhedsnet mod
   noget der ikke sker — værd at vide, før nogen "forenkler" den væk. Er svaret **404**, er grænsen
   det eneste der står mellem en mistet projektrettighed og en skærm fuld af "kilden kender den
   ikke". Fra og med lukketilbuddet på en slettet sag er den grænse **bærende**: en afstemt række
   kan accepteres, hvor den før kun kunne læses.
De to andre er besvaret og står ikke længere på listen: `&errorPolicy=omit` gør det vi tror, og
`WHERE [System.Id] IN (…)` er gyldig WIQL der. Svarene med tal står i
`docs/plans/2026-09-14-closing-a-deleted-item-design.md` afsnit 1, sammen med et tredje fund ingen
havde spurgt om — et *slettet* id svarer **404 `TF401232`**, ikke den 400 fra 2026-08-20, som
gælder id'er der aldrig har eksisteret.

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
- **E2E-suiten er set flakke to gange, og anden gang har et navn.** Første gang (2026-08-21): 46 af
  47 på en kørsel, alle 47 på den næste og de to derefter, med samme kode; testen blev **ikke**
  identificeret, fordi udskriften var filtreret. Anden gang (2026-09-01): 82 af 83 inde i
  `Check.cmd`, og den røde var
  `DeferUntilJourneyTests.A_task_starting_tomorrow_sits_in_Udskudt_survives_an_edit_and_returns_when_cleared`.
  Den bestod alene umiddelbart efter, og hele suiten bestod 83 af 83 på næste fulde kørsel — samme
  kode, ingen rettelse imellem. **Fejlteksten gik tabt igen**, fordi udskriften var filtreret til
  resultatlinjerne; det er anden gang det koster, så gem hele udskriften. Begge gange var det inde i
  en fuld kørsel og aldrig alene, hvilket er det eneste mønster der er to datapunkter for. Det står
  her, så en rød E2E der bliver grøn af sig selv ikke koster en time.

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
- **Resten af GTD-hullerne.** Ingen projekter og ingen kontekster. Kontekstaksen er den mest
  indgribende: den ville omgøre designdokumentets afsnit 2 frem for at lægge et felt til, og afsnit 11
  rangerer den som det største hul, fordi deadline stadig er den eneste akse for alt handlingsklart.
  *(Den ugentlige gennemgang stod her indtil 2026-08-27 og er leveret 2026-08-28 — se "Uden for
  skiverne". Den var den billigste af de tre, fordi den læste data der allerede fandtes.)*
*(Animationer side om side stod her i nogle timer 2026-08-25 og er nu leveret — se "Uden for
skiverne". Det der lukkede den var afsnit 8b: nesting plus én CSS-regel, målt frem for gættet.)*

### Lokal AI-model

Ønsket 2026-10-22. Seks punkter, der deler de samme præmisser:

- **Modellen er Gemma 4 E4B gennem Ollama** på `http://localhost:11434`. `Todo.cmd` henter den ved
  opstart, hvis den mangler (`scripts\install-ai-model.ps1`). Den udgivne exe gør ikke, og Ollama
  selv installeres ikke af scriptet.
- **Ingen kinesiske modeller.** Appen kører på en arbejds-pc, hvor de er forbudt — heller ikke
  Qwen, DeepSeek, Kimi, GLM eller MiniMax som embedding-model eller via Ollamas cloud. Scriptet
  fjerner ved opstart enhver Ollama-model, der ikke står i dets `-Approved`-liste, så en ny model
  (fx EmbeddingGemma) skal på listen, før appen tager den i brug.
- **Hosten kalder Ollama, siden gør ikke.** ADR 18 forbyder en side at kalde ud, og
  `localhost:11434` er en anden origin end appens egen.
- **Modellen foreslår, brugeren vælger.** Intet gemmes uden et klik — samme mønster som importens
  forslag om at lukke en løst sag.
- **Svarer Ollama ikke, er funktionerne skjult**, og appen virker som før. Ingen fejl i ansigtet.
- **Testene kører mod en falsk Ollama** som rigtig Kestrel (ADR 21), aldrig mod den rigtige model.

Punkterne:

- **Hurtig indtastning i fri tekst.** "Ring til Anna om fakturaen på fredag" i feltet til ny opgave
  bliver til titel, `Requester` og `Deadline`. Modellen svarer i JSON mod et skema. Dagens dato
  kommer fra `IClock` og sendes med, for modellen kender ikke dagen. Den billigste af de seks og den
  der bruges oftest — start her.
- **Del en opgave op.** En knap i detaljepanelet foreslår 3–6 underopgaver ud fra titel og note.
  Brugeren krydser dem af, som rækkerne i retro-importen.
- **Handlingsrettede titler i retro-importen.** En seddel skrevet som en observation ("Vi skal blive
  bedre til at teste") får en handling som foreslået titel. Er `Action Owner` tom, men står navnet i
  teksten, gætter modellen ejeren. Forslaget står ved siden af originalen, og genkendelsen af en
  allerede importeret række bruger stadig originalens `Content` + `Zone` + `Author` + `Created`.
- **Resumé af importerede sager.** En lang Jira- eller ADO-beskrivelse bliver til en note på to
  linjer, og linket til sagen bevares. Resuméet er en lokal berigelse i ADR 14's forstand.
- **Søgning på betydning og dubletter.** Søgningen finder "den med certifikatet", også når ordet ikke
  står i opgaven, og importen advarer, når samme sag allerede findes fra en anden kilde. **Kræver en
  embedding-model ved siden af** (fx EmbeddingGemma), som scriptet ikke installerer i dag. Vektorerne
  gemmes i SQLite pr. opgave og regnes om, når titel eller note ændres.
- **Hjælp til den ugentlige gennemgang.** Et kort overblik øverst på `/review`: hvad blev færdigt i
  ugen, hvad har ventet længst, og hvilke Måske-opgaver er ikke rørt i måneder. `TaskItem` har intet
  `UpdatedAt`, så "ikke rørt" kan uden revisionsloggen kun læses af `CreatedAt` — et argument for at
  tage revisionsloggen først. Kan have brug for 26B A4B frem for E4B; mål det, før det besluttes.

*(Et rykkerudkast til opgaver i Venter på blev foreslået samme dag og fravalgt.)*

**Leveret 2026-10-22, til brugerens gennemgang:** fundamentet og de tre første punkter — hurtig
indtastning, del en opgave op og resumé af importerede sager. Designene er
`docs/plans/2026-10-22-ai-foundation-design.md` og `docs/plans/2026-10-22-three-ai-features-design.md`;
det sidste er **ikke** godkendt afsnit for afsnit, fordi brugeren bad om at gennemgå til sidst. Tre
ting er værd at vide før gennemgangen:

- **Hvert kald tager 4–8 s** varmt på brugerens maskine, og det første efter en kold model ~15 s
  mere. Knappen skriver "Spørger modellen…" så længe.
- **Svarets sprog afgøres af koden, ikke af modellen.** Retro og Azure DevOps er engelsk, Jira er
  dansk (brugerens ord 2026-10-22); kun egne opgaver og hurtig indtastning gættes ud fra teksten
  (`AiLanguage`). Bedt om selv at finde sproget svarede Gemma på norsk og på engelsk til dansk input.
- **Promptkvaliteten måles med `dotnet run --project tests\Todo.AiEval`** mod den rigtige model —
  21 faste cases med forventede svar, en score pr. funktion og tiderne. Den er **ikke** en del af
  `dotnet test` eller `Check.cmd`, fordi suiten aldrig må ramme den rigtige model. Ret en prompt i
  `AiPrompts`, kør den, og sammenlign med forrige kørsel. Sidste kørsel: 21 af 21.

**Anden runde samme dag, også til gennemgang:** de tre resterende funktioner og tags (brugerens
eget ønske midt i leverancen). Designene er `docs/plans/2026-10-22-remaining-ai-features-design.md`
og `docs/plans/2026-10-22-tags-design.md`, ingen af dem godkendt afsnit for afsnit.

- **Brugerens første afprøvning fandt fire ting, alle rettet:** et kald tog 2 s ekstra, fordi
  `localhost` prøver IPv6 først på Windows (nu `127.0.0.1`); modellen blev smidt ud efter 5 minutter
  og kostede ~13 s at hente ind igen (nu `keep_alive` 8 timer og opvarmning ved start); ingen knap i
  hele appen havde en pointer (Tailwind 4, nu `cursor-pointer` på alle og en vagt); og knapperne var
  engelske, fordi **Windows' visningssprog er `en-US`** og appen står på "Følg systemet" — målt på
  WebView2's `--lang=en-US`. Det sidste er en indstilling, ikke en fejl: vælg Dansk.
- **Genveje til alt:** Alt+T Fortolk, Alt+Shift+F underopgaver, Alt+Shift+R resumé, Alt+H
  retro-handlinger, Alt+B betydning, Alt+U ugens overblik, Alt+L tag-filter, Alt+Y tilføj foreslåede
  tags, Alt+Shift+G tags i panelet. Et forslag tager fokus, så Enter accepterer og Escape kasserer —
  undtagen tag-forslaget, som aldrig må tage feltet fra en der skriver videre.
- **Hurtig indtastning laver nu underopgaver** af de trin, linjen selv nævner ("først udkast så
  finpudsning") — brugerens eget eksempel. Aldrig et opfundet trin, og aldrig ét alene.
- **EmbeddingGemma** (`embeddinggemma:latest`, 621 MB) er hentet med brugerens tilladelse og står på
  `-Approved`-listen. Den driver søgning på betydning og "Ligner:" i Jira- og
  ADO-forhåndsvisningen (tærskel 0,62); vektorerne ligger i hukommelsen, målt til 1,4 s pr. 100
  opgaver første gang.
- **Søgning på betydning er ombygget 2026-10-23** efter brugerens dom ("ubrugelig, eller også forstår
  jeg den ikke") og en måling på brugerens egne 44 opgaver gennem den kørende app, kun læsning. Den
  gamle regel fandt det rigtige svar først i 5 af 17 realistiske spørgsmål, den nye i 14: kun
  titler, de tre bedste, bedst mindst 0,30 og hver højst 0,08 efter. Færdige opgaver søges med
  (37 af 44 var færdige), fundene står i en egen sektion "Ligner" frem for at blive blandet ind i
  listen, og "Ingen opgaver ligner søgningen." står der, når intet gør. Se afsnit 7 i
  `docs/plans/2026-10-22-remaining-ai-features-design.md`.
- **Ugens overblik genfortæller ikke længere det fuldførte** — brugerens dom over første udgave var,
  at det punkt var ubrugeligt og forkert gengivet. Det handler nu om det overskredne, det ventende og
  gamle Måske-opgaver, og et tomt emne får en fast sætning.
- **Eval'en har 50 cases** over ni funktioner. Kendte grænser, målt og skrevet ned frem for skjult:
  søgningen finder begreber, ikke viden ("regning" finder ikke "faktura"), og et tag-navn der betyder
  noget andet på engelsk ("Drift") foreslås dårligere.

Tilbage af de oprindelige seks: ingen. Åbne spørgsmål til gennemgangen står i de to designs.
