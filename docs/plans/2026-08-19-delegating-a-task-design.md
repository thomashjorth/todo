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
feltet findes allerede i detaljepanelet, og det bliver det næste du står i. Det er samme konvention som
Alt-genvejene fra skive 8: en handling flytter fokusringen, fordi Windows gør det.

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

**Hver gruppe beholder sin egen fejllinje** — `settings-error`, `jira-error`, `alias-error`. Det er
allerede rigtigt: en fejl i tokenet hører ikke ved sproget.

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

## Testtal før leverancen

Core **90**, Api **187**, E2E **34**, Vitest **186** — alle grønne på `main` (`e6be619`).

## Hvad der bliver utestet, og det skal stå her

- **`<datalist>`s popup.** Browserens chrome; hverken farve eller tastaturadfærd er målbar fra
  Playwright. Den følger `scheme-light-dark` som sprogvælgeren.
- **Om forslagene faktisk hjælper.** Uddelegering er hurtigere fordi man ikke skriver navnet; at det
  *føles* hurtigere kan ingen test sige.
- **At uddelegering ikke rører Jira.** Der er ingen adfærd at vagte — fraværet af et kald kan påstås
  (`Assert.Empty` på en opsnappet rute), og det er værd at gøre, hvis nogen senere tror det modsatte.
