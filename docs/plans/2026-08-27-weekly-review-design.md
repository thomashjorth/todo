# Design: ugentlig gennemgang

Skrevet 2026-08-27. **Design, ikke en leveranceplan** — opgaverne skrives efter at dette er
godkendt og committet.

Lukker det GTD-hul designdokumentets afsnit 11 kalder "ingen støtte til ugentlig gennemgang, som er
GTD's nøglevane". Den var et af tre punkter under "Ønsket, men ikke placeret", og brugeren valgte den
2026-08-27 frem for projekter, kontekster, revisionsloggen og "sådan er den tænkt"-siden.

**Den får ikke et skivenummer.** Præcedensen er overvældende — Swagger-linket, vagt-statusserne,
uddelegering, accordion på indstillingssiden, søgning, side by side, sektionsovergange og den
redigerbare titel er alle leveret uden for skiverne. Og argumentet der ellers plejede at afgøre det,
nemlig at et nummer skubber 13–16 med, er **dødt**: skive 16 er leveret før 13, 14 og 15, så numrene
er mærkater og ikke en rækkefølge længere.

## 0. Beslutninger, med hvem og hvornår

| # | Beslutning | Hvem, hvornår |
| --- | --- | --- |
| 1 | Accordion i gennemgangsrækkefølge frem for et ført flow med "næste" eller én tæt skærm | Brugeren, 2026-08-27 |
| 2 | Fem grupper, alle fremadrettede. **Intet** tilbageblik, altså ingen "fuldført siden sidst" og ingen "i gang" | Brugeren, 2026-08-27 |
| 3 | Ugedag og klokkeslæt er indstillinger, og gennemgangen kan altid startes manuelt | Brugeren, 2026-08-27 |
| 4 | En eksplicit "gennemgang færdig"-knap skriver tidspunktet, frem for at et besøg gør det | Brugeren, 2026-08-27 |
| 5 | Påmindelsen er en markør på navigationslinket, ikke en linje på opgavelisten, ikke begge, ikke health-linjen | Brugeren, 2026-08-27 |
| 6 | Rækkerne er `li[appTaskRow]` med hele detaljepanelet, frem for lette rækker eller en læseliste | Brugeren, 2026-08-27 |
| 7 | Standarden er fredag 15:00 | Claude, 2026-08-27 — Allens egen anbefaling, og det er en indstilling brugeren kan flytte |
| 8 | De tre værdier bor i `Setting`. **Ingen migrering** | Claude, 2026-08-27 — se afsnit 3 |
| 9 | `showSomeday` bliver et visningsfilter frem for en indlæsningsparameter | Claude, 2026-08-27 — se afsnit 4 |
| 10 | Ingen skivenummer | Claude, 2026-08-27 — se ovenfor |

## 1. Hvad den er, og hvorfor den ikke er en femte liste

En sjette skærm på `/review` med `Alt+G`, som fører dig gennem fem grupper i GTD's rækkefølge og
slutter med et tryk der siger "nu er jeg igennem".

Designdokumentets egen advarsel om "sådan er den tænkt"-siden gælder her: prosa om GTD uden en
rækkefølge der betyder noget bliver bare en femte liste. Fire ting adskiller gennemgangen fra
opgavelisten, og de er hele begrundelsen for at skærmen findes:

1. **Rækkefølgen er en beslutningsrækkefølge, ikke en tidsakse.** Opgavelisten sorterer efter
   deadline. Gennemgangen går: brudte tilsagn → hvad andre skylder dig → hvad kommer tilbage → den
   brede bunke → det du ikke har taget stilling til. Hver gruppe er ét spørgsmål.
2. **Den viser det opgavelisten *skjuler*.** Måske ligger bag et flueben, og Udskudt er blandet ind i
   deadline-sektionerne som en beregnet bucket. I gennemgangen står de side om side med resten, fordi
   det er dem en gennemgang findes for.
3. **Højst én gruppe åben.** Du læser ét spørgsmål ad gangen frem for at skanne en side.
4. **Der er en afslutning.** Opgavelisten er aldrig færdig; gennemgangen er, og trykket er det der
   gør den til en vane frem for en skærm.

Skærmen er `block xl:max-w-2xl` som de fire andre ikke-liste-skærme — **ikke** to spalter. Rækkerne
folder ud inline, som på den smalle opgaveliste.

De fem grupper, i rækkefølge:

| Gruppe | Spørgsmålet | Kilden |
| --- | --- | --- |
| Overskredet | Hvad har jeg brudt, og hvad gør jeg med det? | `DeadlineBucket.Overdue` |
| Venter på, ældste først | Hvad skylder andre mig, og hvad er rådnet? | `TodoStatus.WaitingFor`, sorteret på `WaitingSince` |
| Starter i denne uge | Hvad kommer tilbage til mig? | `DeadlineBucket.Deferred` med `DeferUntil` inden for syv dage |
| Uden deadline | Fortjener hver af dem stadig at ligge her? | `DeadlineBucket.NoDeadline` |
| Måske | Skal noget af det aktiveres, eller er det stadig ikke et tilsagn? | `TodoStatus.Someday` |

**Udskudt er filtreret til syv dage frem for at vise alt**, fordi GTD's tickler viser noget når det
nærmer sig og ikke hele tiden. Noget der ligger tre uger ude bliver set på den gennemgang hvor det er
nået tættere på. Ingen opgave falder mellem to stole af det: en udskudt opgave står i Udskudt indtil
den dag den starter, og derefter i sin deadline-bucket.

## 2. Hvornår den er forfalden — beregnet, ikke udløst

Ingen timer, intet baggrundsjob, ingenting der kører ved midnat. Samme regel som udskudtheden i
skive 9:

```
forfalden     = seneste (ugedag, klokkeslæt) der ligger før nu, i lokal tid
er_forfalden  = sidsteGennemgang == null || sidsteGennemgang < forfalden
```

Det overlever at appen har været lukket i tre uger — appen har ingen tray og ingen baggrundssync,
skive 14 er uleveret, så et tidspunkt der "fyrer" fredag 15:00 rammer ingenting hvis vinduet er
lukket. Og "start den når du har lyst" er det trivielle tilfælde: ruten er altid åben, markøren er
bare ikke der.

**Kadencen er fast ugedag, ikke syv dage — og gættet falder den anden vej.** Tager du en gennemgang
tirsdag, tilfredsstiller den den *igangværende* periode, så markøren forsvinder. Når fredag 15:00 så
kommer, er `forfalden` flyttet frem, tirsdag ligger før den, og markøren kommer igen. Det er meningen:
du har valgt en **ugedag**, og en "syv dage siden sidst"-regel ville lade vanen glide en dag frem hver
uge, indtil ugedagen du valgte ikke betyder noget længere.

**Tidszonen er fælden, og den skal skrives ned.** "Fredag 15:00" er lokal tid; `lastCompletedAt` er
UTC efter konventionen om at tidsstempler er `DateTime` i UTC og aldrig `DateTimeOffset`.
Sammenligningen skal derfor ske i **én** ramme: `forfalden` bygges lokalt af ugedag og klokkeslæt og
konverteres til UTC.

`IClock` kan ikke svare på "er klokken 15 endnu". Asymmetrien findes i forvejen og er værd at kende:
`UtcNow` er UTC, mens `Today` er **lokal** (`DateOnly.FromDateTime(DateTime.Now)` i `SystemClock`) —
men `Today` har intet klokkeslæt. Interfacet får derfor **`DateTime LocalNow`**, ét medlem, og
`FixedClock` får et valgfrit klokkeslæt så de eksisterende kaldesteder bliver ved at compilere.
Alternativet — at udlede lokal tid af `UtcNow` gennem `TimeZoneInfo.Local` — ville gøre testene
afhængige af maskinens tidszone, og en vagt der fejler af den forkerte grund er værre end ingen vagt.

**Aldrig gennemgået = forfalden med det samme.** En frisk database siger "du har ikke været igennem"
på dag ét. Det er valgt frem for glemt: et tryk rydder den, og alternativet er en app der tier om
vanen indtil en vilkårlig fremtidig fredag.

## 3. Data og kontrakt — tre rækker, ingen migrering

Tre nye nøgler i `Setting`, og **ingen ny tabel og ingen migrering**:

| Nøgle | Form | Standard |
| --- | --- | --- |
| `review.weekday` | `DayOfWeek` som tal, `0`–`6` | `5` (fredag) |
| `review.timeOfDay` | `HH:mm`, invariant | `15:00` |
| `review.lastCompletedAt` | UTC ISO-8601 | ingen række = aldrig |

`lastCompletedAt` er strengt taget ikke et *valg* og hører efter bogen i designdokumentets
`SyncState`. Men den findes ikke (skive 14), og `Setting` bærer allerede tokens, som heller ikke er
valg — nøgle/værdi-tabellen er en generel lagring og ikke kun brugerpræferencer. En håndskreven
migrering for tre nøgle/værdi-rækker er ikke prisen værd.

**De to første skal følge `AdoDefaults`-præcedensen felt for felt, og hvis de ikke gør, vælter en test
der intet har med gennemgang at gøre.** `SettingsEndpointsTests` påstår `Assert.Empty(StoredSettings())`
— altså at **hele** `Settings`-tabellen er tom efter en `PUT /api/settings` der kun beder om
standardværdier. Så:

- Standarden ét sted, i en `ReviewDefaults`-klasse, læst i **læselaget** (`ReviewSettingsReader`, som
  `AdoSettingsReader`).
- Skriveren gemmer en række **kun** når anmodningen beder om noget andet end standarden.
- Kontrakten bærer `default: 5` og `default: "15:00"`, fordi wiren ikke kan skelne et fraværende felt
  fra `0` — og `0` **er** søndag, altså en gyldig ugedag. Nøjagtigt samme fælde som
  `adoDefaultDeadlineDays`, hvor `0` betyder "slået fra" og derfor skal bevares.
- En ulæselig eller uden-for-interval-værdi læses som standarden, samme regel som `SettingList.Read`
  giver korrupt JSON: en ulæselig indstilling må ikke stoppe appen fra at åbne.

**`lastCompletedAt` går derimod *ikke* gennem `PUT /api/settings`, og det er hvad der redder den
test.** Den er en *handling*, ikke et valg:

```
POST /api/review/complete   → skriver clock.UtcNow, svarer den nye tilstand
GET  /api/review            → { due, lastCompletedAt, nextDueAt }
```

Gik den gennem indstillingssvaret, ville fuld-erstatning-semantikken rydde den hver gang du skiftede
sprog — præcis den fælde `TaskStore.update` og Jira-tokenet hver har sin egen løsning på.

**`GET /api/review` bærer kun forfaldstilstanden, ikke grupperne.** Det var et forkert gæt undervejs,
og målingen der rettede det er værd at kende: `sections` på opgavelisten er en **klient**-computed
over `placeTasks`, og serveren bidrager kun med `bucket` pr. opgave. Vigtigere endnu injicerer
`TaskRow` og `TaskDetail` **`TaskStore` direkte**, så en gennemgang med sin egen kopi af listen ville
blive forældet i det øjeblik du gemte noget — og rækken ville ikke forlade sin gruppe, som er hele
pointen i afsnit 4. `nextDueAt` er med, så markøren ikke skal regne, og så en test kan påstå om
grænsen frem for kun om siden af den.

## 4. Skærmen, og den ene forudsætning den har

Fem `section[appReviewSection]` — attributvælger som `li[appTaskRow]` og `section[appSettingsSection]`,
så `<section>` er værten og beholder sit `data-testid`, og skillelinjen falder mellem søskende.
Foldningen er `@if`, ikke `hidden`, af samme grund som på indstillingssiden. Hver overskrift bærer sit
antal: `Overskredet (3)`.

**Afvigelse fra indstillingssiden, med vilje:** gennemgangen ankommer med **den første ikke-tomme
gruppe åben** frem for med nul åbne. Indstillingssiden er et opslagsværk, du går til for at ændre én
ting; gennemgangen er en sekvens, og at skulle klikke for at komme i gang er et trin uden formål. Er
alle fem tomme, er nul åben, og skærmen siger det frem for at vise ingenting — samme krav som
retro-importens "ingen af dem er dine".

En tom gruppe beholder sin overskrift og sit antal `(0)`, og kan stadig åbnes: en kontrol der ikke kan
betjenes er en død kontrol. Åbnet siger den at der intet er at se.

Rækkerne er `li[appTaskRow]` med hele detaljepanelet, altså de ni `Alt+Shift`-felter, status, datoer,
titel, note, underopgaver og sletning. Det giver to ting gratis, og den anden er den vigtigste:
fordi `TaskStore` genindlæser efter et gem, **forlader en række sin gruppe i samme øjeblik du
beslutter om den** — aktiverer du en Måske-opgave, forsvinder den fra Måske-gruppen, og
sektionsovergangene fra 2026-08-25 animerer det uden en linje ekstra.

Grupperne læser `TaskStore`s **rå** liste, ikke `matching()`: en søgning fra opgavelisten må ikke
snævre en gennemgang, og der er intet søgefelt her.

**Forudsætningen, og den er truffet frem for gættet: `showSomeday` skal holde op med at være en
indlæsningsparameter og blive et rent visningsfilter.** Gennemgangen skal have Måske-rækkerne uanset
fluebenets stand, og de tre andre veje er alle værre:

- At tænde fluebenet på vej ind ændrer brugerens liste bag ryggen.
- En egen kopi af listen bliver forældet ved hvert gem, jf. afsnit 3.
- En versionstæller på storen som gennemgangen `effect`er på, er en eventbus i forklædning plus en
  rundtur ekstra pr. beslutning.

Omlægningen er lille og **fjerner** halvdelen af den race `CLAUDE.md` advarer mod, fordi klienten
**allerede** gater visningen: `someday()` i `task-list.ts` er `showSomeday() ? somedayTasks() : []`.
Serverfiltret er altså kun til for datamængden — og Måske er en kurateret, afgrænset liste, mens
Fuldført vokser for evigt. **`showCompleted` bliver derfor uændret en indlæsningsparameter**, og
asymmetrien er begrundelsen frem for en inkonsekvens.

Prisen er målt: **to** Vitest i `task-store.spec.ts` rører `setShowSomeday` —
`should ask the API for parked tasks once they are shown`, som *er* indlæsningsparameteren og skal
skrives om til en påstand om at parkerede opgaver kommer med uden kontakten, og race-vagten
`should not let a slow earlier load overwrite a newer list`, som bruger begge kontakter for at få to
indlæsninger i luften. **Vagten skal blive** — `update`, `remove` og `load` kan stadig race — omskrevet
til to `setShowCompleted`-kald.

## 5. Indstillingerne, navigationen og markøren

**En sjette accordion-gruppe på indstillingssiden: "Gennemgang".** Ugedagen er et `<select>` med syv
optioner, klokkeslættet et `<input type="time">`. Samme mønster som resten af siden: native felter med
`[value]` og `(change)`, ingen Angular forms, gem pr. felt.

To målte krav følger med:

- **`<select>`'en skal male sin egen baggrund** — her `bg-white dark:bg-gray-800`. **Rettet
  2026-08-27, efter at leveranceplanen målte det:** afsnittet sagde først `bg-gray-50
  dark:bg-gray-800` og kaldte det "panelets egne farver", og det er det forkerte par for *denne*
  vælger. Reglen er ikke ét fast par, men at male en uigennemsigtig baggrund der matcher det man
  **står på**: sprogvælgeren på indstillingssiden er `bg-white dark:bg-gray-800` (sidens baggrund),
  statusvælgeren i detaljepanelet er `bg-gray-50 dark:bg-gray-800` (panelets). Ugedagsvælgeren står
  på indstillingssiden ved siden af sprogvælgeren og tager derfor dens par.
  Det er appens **tredje** `<select>`, og de to første er hele historien om den hvide popup:
  sprogvælgeren havde mønsteret fra starten og har aldrig fejlet, statusvælgeren manglede det og malede
  en hvid popup under et mørkt tema, fordi WebView2 falder tilbage på hvid for en transparent baggrund.
  Optionerne får **ingen** klasser. `ColorSchemeTests`' anden `[Theory]` itererer alle `<select>`, så
  tallet flytter sig ikke — men glemmes klassen, fejler den.
- **`type="time"`s vælger-popup er browserens eget vindue, og den kan ikke måles her.** Samme klasse
  som `<select>`-popup'en, hvor `color-scheme` blev udelukket to gange. Den står derfor i afsnit 8 som
  en måling kun brugeren kan lave, med faldbacket navngivet: to `<select>` for time og minut, altså det
  beviste mønster. Et forkert farvet vælgervindue er kosmetisk og ikke brækket, så det er ikke værd at
  undgå den rigtige HTML-kontrol for. Målt i forvejen: **`'time'` står allerede i kontrastvagtens
  hvidliste** over felttyper der maler deres værdi, så feltets værdi bliver målt uden en ændring.

**Navigationen får et sjette link, `nav-review`, med `Alt+G`.** Bogstavet er frit: alt-lagets optagne
er `O I J A S N K V M`, og Chrome på Windows binder intet `Alt+G`.

Markøren er et tekstglyf `●` i `text-amber-700 dark:text-amber-300` — ventesektionens par, som består
i forvejen — med `aria-hidden="true"`, plus en `<span class="sr-only">` der bærer ordet. **Tekstglyf
frem for inline-SVG**, af samme grund som accordion-chevronen: en SVG uden titel bidrager intet til det
tilgængelige navn, så mutationen der fjerner `aria-hidden` ville fælde **ingenting**, og vagten kunne
ikke skrives.

Konsekvensen er at linkets tilgængelige navn **skifter** når gennemgangen er forfalden — "Gennemgang
forfalden" — og det er med vilje: en farvet prik alene er ikke en forskel efter WCAG.
Substrings-matchningen i `GetByRole(..., Name)` redder de eksisterende locators, men en test der
påstår om *ikke* forfalden skal have `Exact = true`.

Tre tal flytter sig, og alle tre er nedskrevne konstanter frem for beregnede:

- `KeyboardJourneyTests.BadgeCount` 9 → 10, som bærer **tre** assertions: badge-optællingen på den
  tomme liste, kollisionsvagten `Every_shortcut_letter_on_screen_is_its_own`, og den såede total, hvor
  `BadgeCount` er et led i aritmetikken.
- `ContrastTests`' bare `25` → 26.

Og **`Alt+G` får sin egen anslags-test**, af samme grund som `Alt+Shift+I` fik en i
titel-leverancen: maskineriet er delt og dækket, men et **nyt** bogstav har en risiko maskineriet ikke
kan gå god for, nemlig om browseren spiser kombinationen. Jf. `Alt+D`, `Alt+E` og `Alt+F`, som Chrome
tager.

## 6. Vagter, målt frem for talt op

Den skarpeste først, fordi de hver især vagter en *beslutning* frem for en linje.

| Vagt | Mutationen der skal fælde den |
| --- | --- |
| `A_review_taken_mid_week_satisfies_this_period_but_not_the_next` | Skift kadencen til "syv dage siden sidst". Det er hele afsnit 2's beslutning |
| `The_boundary_is_local_time_rather_than_UTC` | Byg `forfalden` af `UtcNow` |
| `Never_reviewed_is_due_at_once` | Læs `null` som "ikke forfalden" |
| `nextDueAt_is_the_next_occurrence_rather_than_the_last` | Svar `forfalden` frem for den næste |
| `An_absent_weekday_field_binds_to_Friday_rather_than_Sunday` | Fjern `default: 5` fra kontrakten |
| `Saving_the_default_weekday_stores_no_row` | Gem ubetinget |
| `A_deliberate_Sunday_is_preserved` | Fold `0` ind i standarden på læsevejen |
| `Completing_the_review_survives_a_later_settings_save` | Læg `lastCompletedAt` på `SettingsRequest` |
| `Deciding_from_the_review_moves_the_row_out_of_its_group` | Giv gennemgangen sin egen kopi af listen |
| `The_review_ignores_an_active_search` | Læs `matching()` frem for den rå liste |
| `Parked_tasks_are_there_without_the_switch` | Rul `showSomeday` tilbage til en indlæsningsparameter |
| `Only_the_first_non_empty_group_is_open_on_arrival` | Ankom med nul åbne, som indstillingssiden |
| `A_deferred_task_three_weeks_out_is_not_in_this_week` | Udvid Udskudt-gruppen til hele bucket'en |
| `Alt_G_follows_the_review_link` | Skift bogstavet i skabelonen |

**To assertions kan ikke laves naivt, og begge er af en klasse `CLAUDE.md` har mødt før:**

1. **Tidszonevagten skal have en klokke hvor `LocalNow` og `UtcNow` har en kendt, ikke-nul forskel.**
   Bygges den på maskinens egen zone, består den på en UTC-maskine uanset implementeringen — altså en
   assertion der ikke *kan* fejle, og præcis den fælde afsnittet "Testdisciplin" nævner fire gange.
   Det er også hele grunden til at `FixedClock` får et eksplicit `LocalNow` frem for at udlede det.
2. **"Markøren er væk" må ikke måles lige efter klikket.** En Playwright-påstand om at *ingenting*
   ændrede sig kan ikke gøres race-fri ved at polle, og her er formen heldigvis den anden — markøren
   forsvinder, altså en ændring — men vagten skal alligevel vente på en rundtur frem for at læse
   umiddelbart efter trykket, jf. `Clicking_the_selected_row_again_keeps_it_showing`.

Wire-formatet får sin egen påstand på det **rå** JSON — `"weekday"`, `"timeOfDay"`, `"due"`,
`"nextDueAt"` — fordi drift-testen kun sammenligner stier og metoder, og et enum eller en dato
serialiseret forkert er den klasse af fejl der har levet en hel skive her før.

**Og den dyreste konsekvens skal siges højt: gennemgangen er appens *sjette* rute, og `ContrastTests`
er skrevet om fem.** `CLAUDE.md`s sætning "`app.routes.ts` har præcis fem ruter" bliver forældet og
skal rettes i **samme** leverance frem for at stå og lyve. Værre er kravet under den: en `@if`-gren er
umålt indtil fixturet har en opgave i den tilstand **og** rejsen åbner gruppen. Prisen er derfor en
fixture-opgave i hver af de fem grupper, fem klik i vagten, plus den tomme gennemgang som sin egen
skærmtilstand — samme regning som da indstillingssiden blev en accordion og gik fra én tilstand til fem.

**Én ting kan ingen vagt her se:** om `type="time"`s vælger-popup bliver mørk i Photino-vinduet. Se
afsnit 8.

## 7. Hvad den ikke gør, og hvad det kostede at vælge

- **Intet tilbageblik.** Ingen "fuldført siden sidst" og ingen "i gang"-gruppe. Refleksionen hører
  sammen med **revisionsloggen**, som er det rigtige fundament for et trend-spørgsmål: uden en
  hændelseslog kan en gruppe kun tælle `CompletedAt` og kan ikke sige hvad der *skiftede*. Kommer
  loggen, er "fuldført siden sidst" en gruppe mere på denne skærm og ikke en omskrivning.
- **Intet der kører.** Ingen notifikation, intet tray-ikon, ingen påmindelse uden for appen. Det hører
  til skive 14, og indtil da er markøren kun synlig når vinduet er åbnet.
- **Ingen fremdrift og ingen afkrydsning pr. gruppe.** Accordionen er rækkefølgen; et flueben pr.
  gruppe ville være wizard-tilstand ad bagvejen, altså netop det beslutning 1 valgte imod.
- **Ingen historik over gennemgange.** Kun *sidste* tidspunkt gemmes, ikke en liste. En liste er
  revisionsloggens arbejde.
- **`0` er ikke en speciel værdi her**, i modsætning til `adoDefaultDeadlineDays` og
  mentions-aldersgrænsen. Ugedag `0` er søndag, og der er ingen "slået fra"-tilstand: vil du ikke
  mindes om den, kan du ignorere markøren. En "slå påmindelsen fra"-kontakt er udeladt, fordi en
  gennemgang der aldrig mindes om er en gennemgang der ikke sker — men bemærk at det er et *valg*,
  og at det er stedet at rette hvis markøren bliver larmende.

## 8. Målinger kun brugeren kan lave

Skrives ind i `HANDOFF.md` under "Målinger kun du kan lave", ikke som påstande i koden:

1. **Bliver `type="time"`s vælger-popup mørk i Photino-vinduet under et mørkt tema?** Åbn
   indstillingssidens Gennemgang-gruppe og klik på urikonet. Er popup'en hvid, er det samme klasse af
   fejl som statusvælgerens, og faldbacket er to `<select>` for time og minut — det mønster der er
   bevist tre gange nu. Bemærk hvad der **ikke** er årsagen, hvis det sker: `color-scheme` er udelukket
   to gange på præcis den slags popup, så mål select'ens egen baggrund frem for at flytte
   `color-scheme` igen.
2. **Virker `Alt+G` i Photino-vinduet?** Dækket af en E2E i Chromium, men et nyt bogstav er den ene
   risiko maskineriet ikke kan gå god for. Står sammen med de to genvejslag-målinger fra 2026-08-24,
   som heller ikke er lavet.
3. **Er fredag 15:00 den rigtige standard for dig?** Ikke en kodemåling, men en brugsmåling: bliver
   markøren konsekvent ignoreret til mandag, er ugedagen forkert valgt frem for at vanen er forkert.
   Indstillingen findes netop derfor.
