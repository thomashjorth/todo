# Vagt-statusser fra Jira — besluttet, ikke planlagt endnu

Dette er **kravene og de beslutninger der allerede er truffet**, skrevet ned mens de er friske.
Den egentlige plan med opgaver og kode skrives, når skive 11 er flettet til `main`.

Besluttet 2026-08-19 sammen med brugeren.

## Kravet

Udviklerne skiftes til at have 2nd level support. Har man vagten, skal man kunne **tænde for**, at
sager i en given Jira-status kommer med i importen — **også når de ikke er tildelt en selv**.

Konkret hos os: `Afventer general` er den status en sag står i, når den venter på den generelle
pulje. Skærmbilledet der udløste kravet var `SAAS-6354`, tildelt Flemming, status `Afventer general`
— altså en sag brugeren skulle kunne tage ind i sin vagtuge, men som skive 11's import aldrig ville
se, fordi JQL'en har `assignee = currentUser()`.

## Beslutning 1: det er to indstillinger, ikke én

Skive 11 leverede `jira.waitingStatuses`, og den er en **mapning** — *"disse Jira-statusser betyder
ventende"* — anvendt på sager der i forvejen er tildelt dig.

Vagt-statusserne er noget andet: **"disse statusser skal hentes uanset hvem de er tildelt"**. Det er
en udvidelse af *hvad der hentes*, ikke en oversættelse af *hvad det betyder*.

Slås de sammen til én liste, kan man ikke længere sige *"`Afventer Kunden` betyder ventende, men hent
den ikke fra puljen"* — og det er en helt almindelig ting at ville.

JQL'en går fra

```
project = SAAS AND assignee = currentUser() AND resolution = Unresolved ORDER BY duedate ASC
```

til

```
project = SAAS AND resolution = Unresolved
  AND (assignee = currentUser() OR status IN ("Afventer general"))
  ORDER BY duedate ASC
```

Statusnavnene skal fortsat citeres og valideres, af samme grund som projektnøglen bliver valideret i
skive 11: **et navn fra en indstilling er brugerinput, og JQL har citater og operatorer.**

## Beslutning 2: en vagt-status importeres som `Open`, ikke `WaitingFor`

**Det er den vigtigste beslutning her, og den er kontraintuitiv.**

`Afventer general` betyder "venter på den generelle pulje". Er **du** puljen denne uge, venter sagen
på **dig** — den er handlingsklar, ikke parkeret.

Importeres den som `WaitingFor`, lander den i "Venter på", altså **væk** fra deadline-sektionerne.
Det er stik modsat hensigten: du ville skjule præcis det arbejde du har vagten for.

Derfor: en status der står i vagt-listen **og** er slået til, importeres som `Open`, og `WaitingSince`
sættes **ikke**.

**Det er også grunden til at de to lister skal kunne overlappe frit.** `Afventer general` er
*ventende* når du ikke har vagten, og *handlingsklar* når du har. Det er kontakten der afgør det,
ikke statussen. En implementation der behandler overlap som en fejl, har misforstået kravet.

## Beslutning 3: vagt-rækker henter ikke changeloggen

Følger af beslutning 2. `WaitingSince` er kun meningsfuld for noget der venter på en anden, og en
vagt-sag venter på dig. Skive 11 henter changeloggen **kun** for ventende rækker — ét HTTP-kald pr.
sag — så vagt-rækker koster **nul** ekstra kald.

Bemærk hvad den forkerte udgave ville have kostet: mappet til `WaitingFor` ville hver vagt-sag have
udløst et changelog-kald **og** være landet i den forkerte sektion. To fejl af én forkert beslutning.

## Puljens størrelse — målt og procesbundet

- **Målt 2026-08-19: 2 sager** i `project = SAAS AND status = "Afventer general" AND resolution = Unresolved`.
- **Procesgrænse oplyst af brugeren: op til 10, ikke højere.** Rotationen tømmer puljen, så den
  akkumulerer ikke.

De to tal er forskellige slags fakta, og det er værd at holde adskilt: **de 2 er en måling, der kan
vokse; de 10 er en procesgrænse, der siger *hvorfor* den ikke gør.**

**Hvad det afgør:** forhåndsvisningen bliver maksimalt omkring **tyve rækker** — dine tildelte plus
puljen. Altså **ingen filtrering før import, ingen paginering i UI'et, ingen "vis kun de nyeste"**.
Skærmen fra skive 11 duer som den er.

**Hvad det ikke afgør — og det her er den vigtige del: koden må ikke *afhænge* af de ti.**
Hentningen håndterer allerede vilkårlig størrelse, fordi skive 11's Task 4 byggede paginerings-løkken
med `startAt`/`total` og et stop hvis en side kommer tom tilbage. Grænsen informerer derfor kun
**UI-beslutningen**, ikke korrektheden. Bliver puljen tredive i en uge hvor ingen har vagten, bliver
skærmen lang — men intet går i stykker.

**Og grænsen holder kun fordi rotationen kører.** Bryder processen sammen, holder den ikke. Derfor
står begrundelsen her og ikke bare tallet: "ingen filtrering" er et valg truffet på en
procesantagelse, ikke en egenskab ved Jira.

## Ting der skal afgøres i planen

**Ingen minder dig om at slukke.** En vagt er tidsbegrænset; en indstilling er ikke. Glemmer du den,
bliver du ved med at trække puljen ind, og din liste bliver kollegernes. Det kan være helt fint — men
det skal være et **valg**, og alternativet (en slutdato på vagten) er dyrere end det lyder: det
kræver noget der kører ved midnat, og skive 9 undgik netop det ved at gøre udskudtheden beregnet.
Overvej i stedet en **synlig** markør på skærmen, så tilstanden ikke er tavs.

**Importerede pulje-sager forsvinder ikke af sig selv.** `Status` er lokal efter import, så tager en
kollega sagen i næste uge, ligger din kopi stadig der. Det er det rigtige design — ellers kunne en
senere sync trække noget tilbage du havde markeret færdigt — men puljen churner mere end dine egne
sager, så du vil se det oftere end i dag.

**Skal `alreadyImported` gælde på tværs af vagter?** Har du taget `SAAS-6354` ind i uge 34, og den
kommer i puljen igen i uge 38, skal forhåndsvisningen så vise den som "importeret tidligere"? Dedup
er i dag `SourceId` + `ExternalKey`, så svaret er ja — og det er formentlig rigtigt, men det er ikke
overvejet.

## Hvad planen skal røre

Kontrakten (`jiraDutyStatuses` på settings), `JiraSettings` + læseren, JQL'en i `JiraTaskSource`,
forhåndsvisningens og importens mapning i `JiraEndpoints`, indstillingssiden, og tests. Statusvælgeren
findes allerede — den henter navnene fra instansen — så den skal blot have en anden liste ved siden af.

**En vagt der skal ses fejle:** en sag i en vagt-status skal importeres som `Open`. Mutationen er at
mappe den til `WaitingFor`, og testen skal fælde den. Skive 11 fandt **ni** vagter der ikke kunne
fejle, alle skrevet i planen frem for i koden — så den her skal muteres, ikke læses.
