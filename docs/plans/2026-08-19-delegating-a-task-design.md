# Uddelegering af en opgave — design

Besluttet 2026-08-19 sammen med brugeren. Planen med opgaver skrives særskilt.

## Kravet

Man skal kunne **uddelegere en opgave** — give den videre til en kollega og selv holde øje med den.

## Beslutning 1: det er en genvej til en tilstand der findes

En opgave med status `WaitingFor` og et navn i `WaitingOn` **er** en uddelegeret opgave. Der kommer
derfor **ingen ny status**, intet `Delegated`-felt og **ingen migrering** — af samme grund som
udskudtheden i skive 9 blev beregnet frem for gemt: en tilstand der kan udtrykkes med det der findes,
skal ikke have sin egen kolonne.

**Backenden skal ikke ændres for selve uddelegeringen.** Målt 2026-08-19: `TaskEndpoints` sætter
`WaitingSince = clock.UtcNow`, når status flytter **til** `WaitingFor`, og kun ved selve flytningen —
kommentaren i koden siger det udtrykkeligt, så en senere redigering af noget andet nulstiller ikke
dagtællingen. Uddelegeringen får altså uret gratis, og `waitingDays` regner sig selv ud.

Det eneste backenden får, er **listen af navne** (beslutning 3).

## Beslutning 2: kun bogføring

**Ingen mail, ingen besked til den anden, ingen tilbageskrivning til Jira.** Uddelegerer du en
Jira-sag, skifter den **ikke** assignee i Jira.

Det er en videreførelse af designdokumentets afsnit 2, hvor deling, samarbejde og tilbageskrivning til
Jira/ADO er uden for scope — ikke en ny beslutning, men den skal stå her, fordi ordet "uddelegere"
inviterer til at tro noget andet.

**Og det skal stå i UI'et.** Ellers tror man, at man har handlet, hvor man kun har bogført. Det er den
eneste del af denne leverance der er en påstand om brugerens forventning frem for om koden.

## Beslutning 3: navnene bor som JSON i `Setting`

En nøgle — `delegates` — der følger `jira.waitingStatuses` og `jira.dutyStatuses`, som allerede er
JSON-lister i den tabel. Dedup sker i handleren, som de gør.

**Alternativet var en tabel som `UserAlias`**, og designdokumentets begrundelse for *den* var et unikt
indeks til versalufølsom dedup. Men målt i skive 2: `RetroEndpoints` deduper alligevel **i handleren**
før den skriver — og indekset tvang den til at gemme **to gange**, fordi SQLite tjekker det pr.
statement, så de gamle rækker skal være væk, før navne der kun afviger i versalitet kan skrives
tilbage. Indekset købte altså en regel handleren håndhæver i forvejen, til prisen af en særhed. Og en
tabel koster en migrering; de to seneste i dette repo skulle skrives i hånden, og den ene var direkte
farlig (en `CAST` af Guid-strenge ville have flettet rækker sammen).

**`UserAlias` må ikke genbruges.** Aliaserne betyder **"hvad der er mit"** i retro-importen, og de
findes kun fordi retro-importen findes. Delegerede er andre mennesker. At genbruge tabellen ville være
en betydningsfejl forklædt som sparsommelighed — samme klasse som at blande `WaitingOn` og
`Requester`, som designdokumentets afsnit 4 er eksplicit om at holde adskilt: den ene er hvem **du**
venter på, den anden hvem der bad **dig**.

## Beslutning 4: listen er forslag, ikke et krav

**Det er designets vigtigste beslutning**, fordi den værner om noget der virker i dag.

To grunde til at navnet fortsat skal kunne skrives frit:

- Du venter nogle gange på **en der ikke er på listen** — en kunde, en supportmedarbejder hos en
  leverandør. Et strengt valg ville gøre den tilstand uopnåelig uden først at gå i indstillingerne.
- **"Venter på" uden et navn er en gyldig tilstand i dag.** Du venter på et svar fra et sted, ikke en
  person. Gør vi navnet obligatorisk, brækker vi noget der virker.

Det eksisterende `waitingOn`-tekstfelt bliver derfor ved at være et tekstfelt. **Ingen felter fjernes.**

## Beslutning 5: vælgeren spørger, feltet svarer

**Vælger du "Venter på" i statusvælgeren, får hvem-feltet fokus.** Ikke en dialog, ikke en ny skærm —
det er samme konvention som Alt-genvejene fra skive 8: en handling flytter fokusringen, fordi Windows
gør det.

> **Rettet efter kørslen, 2026-08-20.** Sætningen "feltet findes allerede i detaljepanelet, og det
> bliver det næste du står i" var **for optimistisk, og på to måder** — den beskrev en simpel
> `focus()` på et element der stod klar, og det gør det ikke.
>
> **Feltet findes først efter en serverrundtur.** `@if` hænger på den **genindlæste** opgaves status,
> og statussen skifter først når `PUT`'en er svaret og listen hentet igen. Målt: lige efter
> `(change)` findes `waiting-on-input` ikke i DOM'en.
>
> **Og rækken destrueres undervejs.** Genindlæsningen flytter opgaven ud af sin deadline-sektion og
> ind i "Venter på"-sektionen — to forskellige `@for`-blokke — så `<li>`'en og komponentinstansen med
> den forsvinder, og en frisk renderer feltet. Et flag holdt i rækken var derfor **altid falsk**, når
> feltet endelig fandtes.
>
> Intentionen bor derfor i `TaskStore.askingWho` (`signal<number | null>`) frem for i rækken: rækken
> der spørger, er ikke rækken der svarer. Ingen skabelon læser signalet, så en skrivning fra en effekt
> kan ikke slås med change detection. **Det er ny UI-intention i datastoren**, og den hører skrevet
> ned med begrundelsen — en store der holder "hvem skal have fokus" ser ellers ud som et lag der er
> lækket, frem for som det ene sted der overlever at rækken bliver bygget om.
>
> Konsekvensen for en E2E-rejse: vent på feltet frem for at antage det, og **opløs locatoren igen**
> bagefter — den skal pege på `waiting-section`, ikke på den `<li>` der blev klikket.

**Forslagene hænger på feltet som en `<datalist>`.** Ét HTML-element, tastaturtilgængeligt gratis, og
appen bruger i forvejen native kontroller — sprogvælgeren er et `<select>`, og `<body>` har
`scheme-light-dark` netop for at de følger farvetemaet.

**Prisen skal siges højt: `<datalist>`s popup kan `ContrastTests` ikke måle**, fordi den er browserens
eget chrome og ikke DOM. Det er samme handel som sprogvælgeren allerede laver. Alternativet — en egen
dropdown — ville koste en komponent, en fokusfælde og fire nye farveflader for at vinde noget der ikke
er i vejen.

## Beslutning 6: indstillingssiden grupperes

Målt 2026-08-19, og uordenen er konkret: **sproget har hverken overskrift eller `<section>`** og ligger
løst under sidetitlen, mens Jira er en rigtig `<section>` med `<h3>`, og retro-aliaserne er en **bar
`<h3>` uden section**. Tre grupper, tre forskellige strukturer, og niveauerne springer.

Fire ligestillede grupper, hver en `<section>` med en `<h3>`, i denne rækkefølge:

1. **Sprog**
2. **Uddelegering**
3. **Jira-import**
4. **Retro-import**

Rækkefølgen er **dine egne indstillinger først, kilderne sidst**, fordi kilderne sættes op én gang og
sproget og uddelegeringen er det man rører.

**Retro-aliaserne hører hos retro**, selvom de også er en navneliste. De betyder "hvad der er mit" i en
importeret CSV og findes kun fordi importen findes. At lægge dem sammen med de delegerede ville gøre
siden pænere og betydningen sløret.

**`<h4>`-niveauet inde i Jira-gruppen bliver.** De to statuslister er underafsnit af Jira, ikke
ligestillede grupper.

**Hver gruppe beholder sin egen fejllinje** — `settings-error`, `jira-error`, `alias-error`.

> **Rettet efter kørslen, 2026-08-20. Kendt hul.** "Det er allerede rigtigt: en fejl i tokenet hører
> ikke ved sproget" er **falsk**, og netop tokenet er modeksemplet. Målt: `settings-error` er
> `SettingsStore.error`, og den skrives af `setToken`, `clearToken` **og** hver `save(…)` — altså også
> basisURL'en, projektnøglen, de to statuslister og de to kontakter. Alle de fejl lander i den linje,
> og linjen står nu inde i **sprog**gruppen, ved siden af sprogvælgeren. Grupperingen gjorde det
> tydeligere frem for at rette det: før lå linjen løst under sidetitlen, hvor den ikke hørte til noget.
>
> Kun uddelegeringen fik sin egen (`delegatesError`, samme opdeling som `RetroStore` har for
> aliaserne), og det var med vilje: én linje vist to steder ville trykke hver afvisning to gange.
>
> **Hvad der ikke er sandt om hullet:** at det ikke kunne flyttes uden at brække en test. De to
> påstande om `settings-error` i `settings.spec.ts` (`should keep a refused token in the field so it
> can be corrected` og `toBeNull`-linjen i uddelegeringens fejltest) slår begge op på **testid alene**
> og er ikke afgrænset til en gruppe — så en flytning til Jira-gruppen ville lade dem stå grønne.
> Hullet er altså åbent fordi ingen har flyttet linjen, ikke fordi noget holder den fast. En rettelse
> er enten en `jira`-egen fejlsignal ved siden af `error`, eller at `settings-error` flyttes ned i
> Jira-gruppen og sproget får sin egen.

**Ingen `data-testid` ændres.** Hver E2E- og Vitest-påstand hænger på dem, så en omstrukturering der
omdøber dem, ville se ud som en fejl i tests frem for i markup.

## Vagterne

Fem, og den tredje er den vigtigste.

1. Listen overlever en rundtur gennem `PUT /api/settings` (Api). Bemærk at `PUT` er en **fuld
   erstatning** der læser et fraværende felt som *ryd*, så `SettingsStore.save` skal bære det nye felt
   med i sit `current`-objekt — samme fælde der tabte en gemt `DeferUntil` i skive 9.
2. Vælger man "Venter på", får feltet fokus (Vitest).
3. **"Venter på" *uden* et navn virker fortsat.** Det er tilstanden beslutning 4 værner om, og uden en
   påstand om den er der intet der stopper nogen fra at gøre navnet obligatorisk næste gang.
4. Et navn der **ikke** står på listen, kan stadig skrives og gemmes (Vitest).
5. Den nye gruppes farver måles i **begge** temaer, inklusive **tom liste** og **liste med rækker** —
   to `@if`-grene, og en gren er umålt indtil fixturet har noget i den tilstand og rejsen åbner den.

Alle fem er leveret, og der kom **en sjette** til som designet ikke havde: **rejsen hele vejen**, hvor
navnet lægges på listen i indstillingerne og findes igen på opgaven **efter en genindlæsning**. Den er
den eneste vagt der måler de to halvdele sammen — indstillingen og opgavelisten er ellers to skærme
med hver sin store, og hver af de fem ovenfor måler kun sin egen side af snittet.

Og de tre nye farvegrene blev **tre**, ikke to: ved siden af den tomme liste og rækkerne blev
uddelegeringens **egen røde linje** (`delegates-error`) målt, fordi den kan provokeres gennem feltet
med et navn der kun afviger i versalitet — serveren afviser det, og det er samme greb som
aliaslistens.

## Testtal

Før leverancen: Core **90**, Api **187**, E2E **34**, Vitest **186** — alle grønne på `main` (`e6be619`).
Efter: Core **103**, Api **191**, E2E **35**, Vitest **198**. Fordelingen står i `CLAUDE.md`s
testtalsafsnit.

## Hvad der bevidst ikke blev gjort

Skrevet ned her, så det ikke ser ud som huller nogen glemte:

- **Ingen besked til den anden.** Ingen mail, ingen notifikation. Uddelegering er bogføring for din
  egen skyld, og appen har hverken en udgående kanal eller en adresse på nogen.
- **Ingen tilbageskrivning til Jira.** En uddelegeret Jira-sag skifter **ikke** assignee i Jira.
  Beslutning 2, og UI'et siger det med ord (`settings.delegates.hint`), fordi ordet "uddelegere"
  inviterer til at tro noget andet.
- **`UserAlias` blev ikke genbrugt.** Aliaserne betyder "hvad der er **mit**" i retro-importen; de
  delegerede er andre mennesker. At lægge dem i samme tabel ville være en betydningsfejl forklædt som
  sparsommelighed — samme klasse som at blande `WaitingOn` og `Requester`.

## Hvad der bliver utestet, og det skal stå her

- **`<datalist>`s popup.** Browserens chrome; hverken farve eller tastaturadfærd er målbar fra
  Playwright. Den følger `scheme-light-dark` som sprogvælgeren.
- **Om forslagene faktisk hjælper.** Uddelegering er hurtigere fordi man ikke skriver navnet; at det
  *føles* hurtigere kan ingen test sige.
- **At uddelegering ikke rører Jira.** Der er ingen adfærd at vagte — fraværet af et kald kan påstås
  (`Assert.Empty` på en opsnappet rute), og det er værd at gøre, hvis nogen senere tror det modsatte.
  **Stadig ikke gjort efter leverancen.**
- **Formateringen.** Der findes **ingen** vagt på prettier, og leverancen efterlod fire afvigelser i
  tre filer, som først blev fundet i sidste opgave ved at køre `--check` i hånden med
  `--end-of-line crlf`. En vagt ville koste en test og lukke hullet permanent; den blev ikke skrevet
  her, fordi den ikke er om uddelegering.
- **Om `settings-error` står ved den rigtige gruppe.** Se det kendte hul under beslutning 6: ingen test
  påstår hvilken gruppe linjen bor i, så en flytning — rigtig eller forkert — er usynlig for suiten.
