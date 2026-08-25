# Animationer når en opgave flytter sig mellem sektioner

Design, 2026-08-25. Ønsket stod i `docs/HANDOFF.md` under "Ønsket, men ikke placeret", noteret
2026-08-24 og ikke designet: *"I dag hopper en opgave uden varsel fra 'Uden deadline' til 'Denne
uge', når en deadline sættes — eller ud i 'Venter på', når statussen skifter — og brugeren skal selv
finde den igen. Ønsket er en overgang hver gang en række skifter plads, så flytningen kan følges med
øjnene."*

## 1. Hvad problemet er, og hvorfor noten kaldte det uafklaret

Noten navngav selv knasten: sektionerne er hver sin `@for`-blok, så rækken **destrueres og
genskabes** frem for at flytte sig. Målt i `task-list.html` er der fire steder en `<li>` kan bo — de
tre `@for`-løkker over `li[appTaskRow]` (deadline-sektionerne, "Venter på", "En dag") og
fuldført-sektionens almindelige `<li>` — og en flytning krydser altid fra det ene til det andet. Det
er samme mekanik `TaskStore.askingWho` findes for: rækken der spørger, er ikke rækken der svarer.

Konsekvensen er, at en klasse på `<li>`'en ikke kan gøre arbejdet. Der er ikke ét element der
bevæger sig; der er et element der forsvinder og et andet der opstår et andet sted. Enhver
CSS-overgang har brug for det samme element i begge tilstande.

Og rammerne er trange. Appen har **ingen** `transition-`, `duration-` eller `animate-`-klasse i
nogen skabelon i dag, `@angular/animations` er **ikke** en afhængighed, appen er **zoneless** (ingen
`zone.js` i `package.json`), og konventionen siger *"Kun standard Tailwind utility-klasser. Ingen
CSS- eller SCSS-regler."* Alt målt 2026-08-25, ikke antaget.

## 2. Beslutningerne

Tre valg, truffet af brugeren 2026-08-25, med det der blev valgt fra. **Der er et fjerde** — at
overgangen kun kører i én spalte — men det blev truffet senere samme dag, efter en måling, og står
derfor i afsnit 8 sammen med den måling der tvang det.

**Mekanikken er View Transitions API'et.** `document.startViewTransition()` snapshotter dokumentet
før og efter, og to elementer der bærer samme `view-transition-name` morfes mellem deres to
positioner — også når `<li>`'en imellem blev destrueret. Det er det ene greb der løser knasten
direkte frem for at omgå den, og det koster omkring tyve linjer i `TaskStore`.

Fravalgt: **FLIP med Web Animations** — mål hver rækkes position før listen skiftes, mål igen efter
render, animér forskellen med `element.animate()`. Præcis afgrænset og let at påstå i en test, men
mere kode, og en række har forskellig højde i to sektioner (deadline-chip mod "venter på X"), så en
ren `translateY` rammer skævt; sektionsoverskrifter der kommer og går hopper stadig. Fravalgt:
**fremhævning på ny plads** — et kort blink hvor rækken landede. Mindst mekanik, men flytningen er
allerede sket når blinket begynder, så den kan ikke følges med øjnene, hvilket var ønskets ordlyd.

**Porten er "enhver ændret plads", ikke "sektionsskift".** En opgave der er med i både den gamle og
den nye liste får en overgang, hvis dens (sektion, indeks) er anderledes. Det tager derfor også
løftet til toppen når statussen bliver I gang — `inProgressFirst` sorterer inden for bøtten — og
rækkerne der glider op efter en sletning.

Fravalgt: **kun sektionsskift**, den snævre læsning af noten, som lader løftet til toppen og
lukningen af hullet efter en sletning blive ved at være hop uden varsel. Fravalgt: **hver gemning**,
altså ingen port, hvis pris er målbar — hvert flueben på en underopgave og hver gemt note ville køre
en 250 ms krydsfade mellem to identiske billeder.

**Reduceret bevægelse respekteres ved at springe overgangen helt over.** Ikke ved at dæmpe den:
varighed og lempe bor i UA-stilarket bag `::view-transition-*`, altså i CSS-regler konventionen
forbyder, så der findes ingen knap mellem "fuld animation" og "ingen".

## 3. Målingerne designet hviler på

Målt 2026-08-25 i Chromium 148 med en prøveside, ikke forudsagt:

| Påstand | Målt |
| --- | --- |
| `document.startViewTransition` findes | ja |
| Opdateringen kører selv når overgangen springes over | **ja** — `callbackRan: true` |
| `t.ready` ved en sprunget overgang | **afvises** — `InvalidStateError: Transition was aborted because of invalid state` |
| `t.finished` ved en sprunget overgang | **resolver** — ingen afvisning |
| `t.updateCallbackDone` ved en sprunget overgang | resolver |

Overgangen sprang over, fordi prøvens rude ikke komponerede frames (`document.hidden === true`).
Det er prøvens miljø og ikke API'et — men det gav den vigtigste egenskab gratis: **listen sættes
uanset hvad**, så animationen ikke kan tabe data. Og det fastlægger fejlhåndteringen: `t.ready`
afventes ingen steder, `t.finished` er den sikre.

Prøvefilen ligger ikke i repoet. Skal målingen gentages, er opskriften: to lister i en rullende
beholder, `view-transition-name` sat inline pr. id, en knap der flytter et element fra den ene liste
til den anden ved at bygge DOM'en op igen, og en logning af `document.getAnimations()` i `t.ready`.

## 4. Mekanikken, og hvor den bor

Overgangen startes i `TaskStore.load()`. Det er det ene sted hver genindlæsning går igennem — `add`,
`update`, `remove`, `addSubTask`, `setSubTaskDone`, `removeSubTask`, `setShowCompleted` og
`setShowSomeday` ender alle der — så én kaldeplads frem for otte. Sekvenstælleren der beskytter mod
svar i forkert rækkefølge ligger allerede der, og porten skal læse den nyeste liste og kun den.

Rækkefølgen er bærende: HTTP'en er færdig **før** `startViewTransition`, så det gamle snapshot ikke
står frosset mens en rundtur venter.

```ts
const items = response.items;

if (!this.animates(items)) {
  this.tasks.set(items);
  return;
}

const transition = document.startViewTransition(() => {
  this.tasks.set(items);
  // Zoneless: DOM'en skal være opdateret, før browseren tager det nye snapshot.
  this.appRef.tick();
});

// finished resolver også når overgangen springes over — målt. ready gør ikke.
await transition.finished;
```

`t.ready` afventes **ikke** nogen steder. Den afvises hver gang overgangen springes over — skjult
dokument, en overgang der allerede kører, to elementer med samme navn — og en ufanget afvisning
ville lande i `provideBrowserGlobalErrorListeners`.

`appRef.tick()` frem for `await appRef.whenStable()`: appen er zoneless, så `tick()` kører
ændringsdetektion synkront og efterlader DOM'en opdateret, når callbacket returnerer.
`whenStable()` er faldbagsvalget, hvis `tick()` kaster fordi ændringsdetektion allerede kører — det
skal måles i implementeringen frem for antages.

**Tre** vagter foran kaldet, i den rækkefølge:

1. `typeof document.startViewTransition !== 'function'` — jsdom 28.1.0 har den ikke, samme hul som
   `matchMedia`, og uden vagten ville hver Vitest der rører `load()` kaste.
2. Reduceret bevægelse.
3. Porten.

Der stod en fjerde her — `!wide.wide()`, altså "kun én spalte animerer" — og den er væk igen samme
dag. Årsagen bag den var rigtig og er løst det rigtige sted, i CSS'en frem for ved at nægte at
animere; se afsnit 8b.

Fejler én af dem, sættes listen direkte som i dag.

## 5. Navnet på rækken, og undtagelsen det koster

Browseren morfer kun elementer der bærer samme `view-transition-name` i begge snapshots. Navnet er
derfor opgavens id, og det er hele grunden til at mekanikken virker: `<li>`'en destrueres, men
navnet overlever, fordi det er skrevet af den **nye** række.

Navnet er en `<custom-ident>` og må ikke begynde med et ciffer, så det bliver `task-42`, ikke `42`.

To steder, ikke fire. `TaskRow` får en host-binding, som dækker de tre `@for`-løkker over
`li[appTaskRow]`:

```ts
host: {
  'data-testid': 'task-row',
  class: 'py-2',
  '[style.view-transition-name]': '"task-" + task().id',
}
```

Fuldført-sektionens række er et almindeligt `<li>` uden `appTaskRow` og får samme binding i
skabelonen. Det er nødvendigt frem for pænt: en opgave der markeres færdig **med** "vis fuldførte"
slået til flytter sig fra en `appTaskRow`-række til den almindelige, og uden navnet på begge sider
er der ingen morf — kun en krydsfade.

**Prisen er en konventionsundtagelse, og den er godkendt af brugeren 2026-08-25.** `CLAUDE.md`
siger *"Kun standard Tailwind utility-klasser. Ingen CSS- eller SCSS-regler."* En
inline-style-binding er ingen af de to — den er en tredje. Den er nødvendig, fordi værdien er
**forskellig pr. opgave**: en utility-klasse er statisk, og Tailwinds arbitrære egenskab
`[view-transition-name:task-42]` kan ikke tage en køretidsværdi. Der findes ingen vej gennem
klasser. Undtagelsen er derfor snæver og skal læses snævert: `view-transition-name` bundet inline,
fordi identiteten er data. Alt andet visuelt bliver ved at være Tailwind-klasser.

Sektionerne og detaljepanelet får **ikke** navne. De ligger i rodens snapshot og krydsfader, hvilket
er det ønskede for en overskrift der kommer og går, og panelet står stille side om side.

## 6. Porten, og omlægningen den tvinger

Porten skal svare "flyttede nogen opgave sig?" **før** listen sættes, så den kan ikke læse
`sections()` — det signal opdaterer sig først bagefter. Den har brug for en ren funktion der kan
svare på et vilkårligt array.

Den funktion findes ikke i dag: grupperingsreglen bor inde i `sections()`, og en kopi ved siden af
ville være samme regel på to steder. Reglen flyttes derfor ud, og `sections()` bliver en læser af
den frem for dens ejer:

```ts
export type PlacedGroup =
  | { kind: 'bucket'; bucket: DeadlineBucket; tasks: TodoTask[] }
  | { kind: 'status'; status: TodoStatus; tasks: TodoTask[] };

/** Hvor hver opgave sidder: den ene regel begge læsere deler. */
export function placeTasks(tasks: TodoTask[]): PlacedGroup[];

/** id til "hvor", til sammenligning på tværs af to loads: bucket eller status, plus indekset. */
function placements(tasks: TodoTask[]): Map<number, string>;
```

**En udskilt union frem for to nullable felter, og det er rettet i implementeringen 2026-08-25 frem
for at stå som først skrevet.** To felter der hver kan være `null` beskriver fire tilstande, hvoraf
kun to findes — og `strict` er slået til, mens `TaskSection.bucket` ikke er optional, så en læser der
kun vil have deadline-sektionerne ville skulle skrive en non-null-assertion. `sections()` bliver
derfor et **`flatMap`**, ikke et `filter` plus `map`: et `filter` på `kind` indsnævrer ikke unionen,
mens en gren der returnerer ingenting gør det uden assertion.

```ts
readonly sections = computed<TaskSection[]>(() =>
  placeTasks(this.matching()).flatMap((group) =>
    group.kind === 'bucket' ? [{ bucket: group.bucket, tasks: group.tasks }] : [],
  ),
);
```

`placeTasks` bærer `bucketOrder` og `inProgressFirst` for de planlagte opgaver og derefter de tre
statuslister, og dropper tomme grupper i **begge** halvdele. Porten bliver:

```ts
private animates(items: TodoTask[]): boolean {
  const next = placements(items);
  const prev = placements(this.tasks());

  return [...next].some(([id, place]) => prev.has(id) && prev.get(id) !== place);
}
```

`prev.has(id)` er porten mod alt andet end en flytning, og den giver tre ting gratis, uden en gren
hver:

- **Første load animerer ikke.** Listen er tom, så intet id er i begge.
- **En ny opgave animerer ikke.** Den lander sidst i "Uden deadline" og rykker ingen.
- **En rettet note eller et flueben på en underopgave animerer ikke.** Ingen plads ændrer sig.

En sletning rykker rækkerne under sig og animerer — som valgt.

**Den accepterede pris, skrevet ned frem for opdaget senere:** porten måler den **ufiltrerede**
liste, mens skærmen viser den søgefiltrerede. Flytter en opgave sig, mens en søgning skjuler den,
kører der en overgang der animerer ingenting — 250 ms krydsfade mellem to identiske billeder.
Alternativet var at lade porten kende `query()`, `showCompleted()` og `showSomeday()`, altså tre
kilder mere den kan drive fra. Bemærk at søgefiltret ikke selv kalder `load()`, så prisen kræver en
aktiv søgning **og** en samtidig gemning.

## 7. Reduceret bevægelse

`ReducedMotion` bliver en injectable i `layout/` ved siden af `WideScreen`, bygget efter samme
mønster af samme grund: `matchMedia('(prefers-reduced-motion: reduce)')`, en `reduce`-signal, en
lytter på `change`, og den samme vagt mod jsdom der ingen `matchMedia` har. Symmetrien er ikke pynt
— `WideScreen`s dokumentation af netop det hul er stedet nogen slår det op.

Lytteren frem for én læsning ved opstart: Windows-indstillingen kan skifte mens appen kører, og
`WideScreen` har præcedensen.

## 8. De to risici, som skal måles frem for antages

**Risiko 1: roden krydsfader, og det kan ikke dæmpes.** Alt uden et navn ligger i rodens snapshot,
og UA'ens standardanimation er en krydsfade over 250 ms. Varighed, lempe og "sluk den" bor alle i
`::view-transition-*`. To identiske billeder krydsfadet er usynligt, så i praksis ser man kun de
sektionsoverskrifter der kommer og går — hvilket er ønskeligt. Men det er ikke en knap vi har, og
skulle den vise sig forstyrrende, er valget mellem at leve med den og at omgøre konventionen.

**Risiko 2, den alvorlige: `::view-transition`-træet ligger i top-laget og klippes ikke af nogen
forælder.** Den er **målt 2026-08-25 og bekræftet**, og målingen er grunden til at afsnit 4 har en
fjerde vagt. Prøven var en engangs-`VtProbeTests` i `Todo.E2E`, som spejlede `app.html` og
`task-list.html` med rå CSS, sænkede varigheden til 3000 ms og sammenlignede health-linjens pixels
med en reference — afkodet **i browseren** med `createImageBitmap` og `OffscreenCanvas`, fordi en
byte-sammenligning ikke kan skelne en krydsfades antialiasering fra en række malet henover.

| Runde | Opsætning | Højtråbende pixels på health-linjen |
| --- | --- | --- |
| A | xl 1400×600, **alle** rækker navngivet | 2182 / 21376, værste kanal 237 |
| B | xl, kun den flyttende række navngivet | 0, værste 1 |
| C | xl, kun den flyttende + `view-transition-group` mod spalten | 0, værste 1 |
| D | **smal 480×800**, kun den flyttende | 0, værste 1 |
| F | xl + health-linjen navngivet | 632, værste 55 |
| G | xl, kun den flyttende, **destination ved spaltens underkant** | **7328**, værste 236 |
| H | xl, alle navngivet, destination ved underkanten | 3838, værste 238 |

Fem ting følger af tabellen, og de tre sidste var ikke forudset:

- **Det er ikke kun den flyttende række der slipper klippet.** Hver navngivet række bliver sin egen
  gruppe i top-laget, så i runde A malede hele listens bund gennem health-linjen.
- **Runde G er den afgørende.** B's nul var fikstur-geometri og ikke en garanti: rækken landede
  *under* linjen. Flyttes destinationen op til spaltens underkant, dækkes linjen næsten helt — kun
  `AP` af `API: sund, version 1.0 – Dokumentation` er læsbart. At navngive færre rækker reducerer
  altså hvor ofte og hvor meget, men fjerner det ikke.
- **Indlejrede grupper løser det ikke *alene*.** `CSS.supports('view-transition-group', 'foo')` svarer
  `true`, men det betyder kun at egenskaben *parses*: runde C — nesting uden mere — ser identisk ud
  med B. Nestingen mangler sin anden halvdel, nemlig en regel på `::view-transition-group-children`.
  **Se afsnit 8b: den halvdel blev målt senere samme dag, og den virker.**
- **At navngive health-linjen gør det værre, ikke bedre.** Tanken var at den så ville males ovenpå,
  fordi den står efter spalten i DOM-orden. Runde F giver 632 højtråbende pixels: linjens eget
  snapshot komponeres frem for at males direkte, og subpixel-antialiaseringen går tabt.
- **Den smalle udgave er strukturelt fri, ikke bare målt fri.** `main` har ingen `h-screen` uden
  `xl:`, wrapperen om `router-outlet` intet `overflow`, og health-linjen står i normalt flow
  **efter** alle sektioner. Der findes altså ingen klippende beholder at slippe ud af, og ingen
  destination der ligger under linjen. Runde D bekræfter det, men argumentet er strukturen.

**Den første beslutning, truffet af brugeren 2026-08-25 om formiddagen: animér kun i én spalte.**
`!wide.wide()` blev vagt nummer tre. Den holdt i nogle timer og er **omgjort** — se 8b — men den
står her, fordi fravalgene stadig gælder, og fordi rækkefølgen forklarer hvorfor koden ser ud som den
gør.

## 8b. Omgørelsen: side om side animerer alligevel

**Brugeren afviste asymmetrien samme dag:** appen bruges i fuldskærm på 4K, og der skal animationen
virke. Det er en beslutning, ikke en forglemmelse, og den tvang tre målinger frem der ikke fandtes da
8 blev skrevet. Alle på 3840×2160, én test pr. kandidat:

| Kandidat | Destination | Animerede | Bleed på health-linjen |
| --- | --- | --- | --- |
| A: kort liste | inde i spalten | ja | **0** |
| B: lang liste | uden for | ja | **73**, værste kanal 236 |
| C: nesting **+** `overflow: clip` | uden for | ja | **0** |
| D: `skipTransition()` når uden for | uden for | **nej** (`AbortError`) | 0 |

**C er svaret, og den er tre linjer.** Rækkerne bærer `view-transition-group: task-column`, spalten
bærer `view-transition-name: task-column`, og `styles.css` har
`::view-transition-group-children(task-column) { overflow: clip }`. Nestingen alene gør ingenting —
det var runde C i afsnit 8 — men med reglen klippes rækkens gruppe af spaltens.

**Og "intet bleed" blev ikke læst som en sejr, før morfen var bevist at køre.** Et klip der åd hele
animationen ville give præcis samme nul. Målt på spaltens eget område, midt i flugten mod den satte
side: **36471** bevægede pixels med nesting mod **37071** i en kontrol uden. Morfen kører altså
uændret; reglen fjerner kun det der slap ud.

**Fravalgt igen: FLIP** — den animerer det rigtige element og bliver derfor klippet uden nogen CSS,
men koster 60–80 linjer mod tre nu hvor C er målt. **Fravalgt: `skipTransition()`** — den lukker
bleedet, men ved at *undlade* at animere netop de flytninger man mest har brug for at følge.

**Prisen er en anden end før, og den skal stå her: konventionen mod CSS-regler har nu en rigtig
undtagelse**, ikke kun `@plugin`-linjen. Begrundelsen står ved reglen i `styles.css`, og den er
værd at læse før nogen "rydder op".

**Og fragiliteten er ny: reglen fejler tavst på en ældre runtime.** Nestede grupper er nye; ignoreres
egenskaben, er bleedet tilbage, og animationen ser stadig rigtig ud. Målt på maskinen: WebView2
**151.0.4129.101**, og featuren virkede allerede i Playwrights Chromium 148. Vagten er
`Side_by_side_runs_the_transition_and_keeps_the_clipping_mechanism`, som påstår at mekanikkens **tre**
dele findes — men den kan ikke se en runtime der ignorerer dem. Det er den ene ting her der ikke er
vogtet.

## 9. Testplanen

**Vitest, i `task-store.spec.ts`.** Porten er det stykke der kan drive, og den er målbar ved at
stubbe `document.startViewTransition` og tælle kald. **Syv** påstande, valgt så hver har sin egen
mutation:

| Påstand | Mutationen den skal ses fælde |
| --- | --- |
| Et sektionsskift starter én overgang | porten slået fra |
| Et løft til toppen (I gang) tæller som en flytning | porten sammenligner kun bøtte og status, ikke indeks |
| En rettet note starter ingen overgang | porten sammenligner hele opgaven |
| En ny opgave starter ingen overgang | `prev.has(id)` fjernet |
| Reduceret bevægelse sætter listen uden overgang | grenen fjernet |
| Side om side starter **også** en overgang | `wide()`-leddet lagt tilbage i porten |
| Uden `startViewTransition` sættes listen | vagten fjernet — jsdom kaster |

Bemærk at den tredje **vendte** samme dag: den påstod først at side om side *ikke* animerer, og efter
omgørelsen i afsnit 8b at den gør. Det er samme test, ikke en ny — tallet flyttede sig ikke.

Hver af dem påstår **også**, at listen blev sat. Det er egenskaben målingen i afsnit 3 gav gratis,
og den der betyder at animationen ikke kan tabe data.

**E2E, én ny rejse.** Den ene ting kun en rigtig browser kan se: at overgangen faktisk **kørte** og
ikke blev sprunget over. Et init-script pakker `document.startViewTransition` og gemmer for hvert
kald om `ready` resolverede eller afvistes; rejsen sætter en deadline, så opgaven forlader "Uden
deadline", og påstår ét kald med `ready` resolveret.

Den vogter over en fælde repoet **har mødt før**: to elementer med samme `view-transition-name` får
`ready` til at afvise, og **intet andet kan se det** — nøjagtig som `data-testid="task-detail"`
engang fandtes to gange og Playwright tavst valgte den første.

**Og at rejsen er mulig, er målt frem for antaget:** prøven i afsnit 8 kørte i Playwrights headless
Chromium 148 og fik `visibilityState = visible`, `ready = resolved` og `finished = resolved`. Havde
ruden været ikke-komponerende — som browserruden i designfasen var — ville `ready` altid afvise, og
påstanden kunne slet ikke skrives. Rejsen kører på **480**, fordi vagt nummer tre lukker overgangen
fra `xl`; på `WideWidth` ville den påstå at ingenting skete og bestå på ingenting.

**Tre ting om rejsen, som først blev tydelige da den blev skrevet:**

- **Ingen init-script.** Wrapperen om `document.startViewTransition` lægges på **efter** at appen er
  indlæst, og det er sikkert frem for heldigt: storen læser egenskaben af `document` ved hvert kald,
  og intet kalder den før en gemning. `TodoApp` behøvede derfor ingen ny parameter.
- **`ready` og ikke `finished`.** `finished` resolverer også for en sprunget overgang (målt i afsnit
  3), så den er det forkerte spørgsmål her.
- **Fikstureet skal have *to* rækker, og det er påstandens tænder.** En dublet kræver to elementer;
  med én række på skærmen kan fejlen slet ikke udtrykkes, og en mutation der navngiver hver række det
  samme ville stadig efterlade ét navn i dokumentet. Rejsen sår derfor en anker-opgave, hvis eneste
  rolle er at findes. **Set fejle** med netop den mutation:
  *"The browser skipped the view transition: ready settled as 'rejected: InvalidStateError:
  Transition was aborted because of invalid state'"* — og i **samme** kørsel bestod
  sektionspåstandene, hvilket er hele begrundelsen for at rejsen findes: listen opdateres, så intet
  andet kan se det.

**Vitest, i `task-list.spec.ts`.** To påstande mere, som denne testplan oprindeligt slet ikke havde:
at **hver** rækketype bærer sit navn. De er mulige, fordi jsdom 28.1.0 **beholder**
`view-transition-name` — målt både gennem `setProperty` og camelCase-egenskaben, og den reflekteres i
style-attributten — så bindingen kan ses uden Playwright. Navnene er pinnet til id'erne frem for blot
at være ikke-tomme: navnet *er* den identitet morfen matches på, og to rækker der delte ét ville få
browseren til at springe overgangen helt over.

De to er uafhængige, målt frem for antaget: fjernes **kun** skabelonbindingen på fuldført-rækken,
falder netop den ene påstand (`expected '' to be 'task-9'`), mens host-bindingens bliver grøn.

**E2E, en anden rejse — side om side.** Kom til med omgørelsen i afsnit 8b, og den påstår **to** ting,
fordi hver for sig ville de være grønne af den forkerte grund: at overgangen faktisk *kører* på 1400 px
(en brækket nesting kunne tage det), og at klipningens **tre** dele *findes* — spaltens navn, rækkernes
`view-transition-group`, og reglen læst af det **serverede** stilark frem for af kilden, så en bygning
der tabte den ikke består. Set fejle med reglen fjernet:
*"Assert.Contains() Failure: Sub-string not found / String: "missing" / Not found: "overflow: clip""*.

Og rejsen afslørede en fælde undervejs, som er værd at kende: side om side er detaljepanelet **allerede**
synligt for den auto-valgte opgave, så `ToBeVisibleAsync` efter et rækkeklik består **før** valget er
renderet — udfyldningen landede derfor på den forrige opgave, og "Senere" blev ét i stedet for to. Ventetiden
er nu på det tomme deadline-felt, som kun den flyttende opgave kan vise. Samme fælde `CLAUDE.md` beskriver
for en handling lige efter et klik.

Tal: Vitest **289 til 301**, E2E **69 til 71**. `Todo.Core.Tests` (174) og `Todo.Api.Tests` (310)
rører ikke funktionen.

**Tallet stod som 296 her indtil 2026-08-25, og det var forkert** — ikke fordi en test blev tabt,
men fordi det kun talte de syv i `task-store.spec.ts` og glemte `ReducedMotion`s tre. Fordelingen er
289 + 3 (punkt 2) + 7 (punkt 4) + 2 (punkt 5) = 301, målt med `check.ps1`. Det er præcis den drift
`CLAUDE.md` advarer mod: et ændret tal kan kun betyde en tabt eller duplikeret test, hvis tallet var
sandt i forvejen.

**Og de 69 eksisterende E2E blev målt frem for antaget.** Alle grønne på 33 s mod 32–36 s før: ingen
målbar afgift, og ingen flakken set.

**Men "de kører alle den animerede vej" er forkert, og det stod her.** Opgjort frem for påstået:
`grep` på `OpenAppAsync` giver **45** kald med `ColumnWidth = 480`, **11** med `WideWidth = 1400` og
**ét** uden viewport. De 45 animerer; de tolv gør ikke, og det sidste er den nære fælde — uden en
viewport bruger Playwright sin standard på **1280×720**, og 1280 px *er* `xl`, så også den slukkes af
vagten. Grønne E2E på `WideWidth` beviser altså ingenting om overgangen.

## 10. Hvad der ikke kan vogtes, sagt ligeud

At animationen ser rigtig ud. Der findes ingen påstand for det, og rodens krydsfade har ingen knap
at skrue på.

Og en konsekvens der er større end den ser ud: **hver gemning i appen går nu gennem en overgang**,
så de **69 eksisterende E2E** er en del af verificeringen frem for en formalitet. De kan blive
langsommere eller flakke — `::view-transition`-træet har `pointer-events: none`, så klik burde gå
igennem, men "burde" er ikke en måling. Det afgøres af en fuld `Check.cmd`, ikke af en antagelse. Og
`docs/HANDOFF.md` noterer allerede, at E2E-suiten er set flakke én gang uden at testen blev
identificeret; sker det her, skal hele udskriften gemmes.

## 11. Rækkefølgen i implementeringen

1. ~~**Mål risiko 2** med en engangs-prøve i en rigtig, kompositerende browser.~~ **Gjort
   2026-08-25**; resultatet og beslutningen står i afsnit 8, og prøvefilen er slettet igen.
2. ~~`ReducedMotion` i `layout/` med sin spec, symmetrisk med `WideScreen`.~~ **Gjort 2026-08-25**,
   tre nye Vitest (289 → 292). Set fejle to gange: uden klassen på `Could not resolve
   "./reduced-motion"`, og med maskineriet inde men adfærden urettet på sine **egne** påstande —
   `expected [] to deeply equal [ '(prefers-reduced-motion: reduce)' ]` og `expected true to be
   false`.
3. ~~`placeTasks`/`placements` udtrukket~~ — **kun `placeTasks`**, og `sections()` omlagt til at læse
   den. `placements` er flyttet til punkt 4, hvor porten bruger den: lagt ind her ville den være død
   kode uden en vagt, og både TDD og YAGNI siger nej. **Gjort 2026-08-25**, alle fire tal uændrede
   (174/310/69/292), og de eksisterende specs er set have tænder frem for antaget at have dem:
   mutationen "glem statusfiltret" fælder **tre** — *"expected [ 'overdue', 'today' ] to deeply equal
   [ 'overdue' ]"* — og mutationen "behold de tomme grupper" fælder **tretten**.
4. ~~`placements`, porten, vagten fra afsnit 8 og overgangen i `load()`, med de syv Vitest, hver set
   fejle.~~ **Gjort 2026-08-25** (292 → 299). Alle syv set fejle, hver på sin egen mutation: porten
   slået fra fælder 1 og 2, nøglen uden indeks fælder 2, hele-opgave-sammenligningen fælder 3 og 4,
   `prev.has(id)` fjernet fælder 4, og de tre vagter fælder hver sin — den sidste med
   `TypeError: document.startViewTransition is not a function`.
   **To ærlige forbehold.** De fem påstande om at *ingen* overgang starter kunne ikke fejle før
   funktionen fandtes; kun de to positive gav rigtig RED, og de fem er bevist af mutationerne alene.
   Og under mutationskørslen ramte to mønstre ikke, fordi de var skrevet med `\n` mod en CRLF-fil —
   den ene kørsel målte derfor den **umuterede** fil og den anden den **forrige** mutation, begge med
   et resultat der lignede et svar. Normalisér linjeskiftene i et mutationsscript, og læs hvad der
   faktisk blev anvendt, frem for at stole på løkkens navn.
5. ~~`view-transition-name` på de to steder.~~ **Gjort 2026-08-25** (299 → 301). Begge set fejle på
   deres eget fravær: `expected [ '', '' ] to deeply equal [ 'task-1', 'task-2' ]` og `expected '' to
   be 'task-9'`, og de er set være **uafhængige**. Herfra morfes der faktisk noget — indtil nu kørte
   kun rodens krydsfade, fordi intet element bar et navn.
6. ~~E2E-rejsen, set fejle på en dubleret navn-mutation.~~ **Gjort 2026-08-25** (69 → 70), i
   `SectionTransitionJourneyTests`. Set fejle, med sektionspåstandene grønne i samme kørsel.
   **Og mutationen ramte ikke i første forsøg**, fordi citeringen i et `python -c`-kald brækkede —
   kørslen målte den umuterede kode og lignede et svar, anden gang samme klasse fælde som i punkt 4.
   Verificér at mutationen står i filen, før du tror på en kørsel.
7. ~~Fuld `Check.cmd`, og `CLAUDE.md`s testtal rettet **med hvorfor**.~~ **Gjort 2026-08-25**:
   174/310/70/301, og fordelingen skrevet ned. `CLAUDE.md` fik desuden tre ting, valgt fordi de
   **ikke** kan udledes af koden — en auto-indlæst fil betales i hver session, så resten blev holdt
   ude:
   - **Stylingkonventionen fik sin undtagelse.** Som skrevet gjorde den `[style.view-transition-name]`
     til en overtrædelse, og den næste der læste den ville have "rettet" den.
   - **Mutationsfælden** fra punkt 4 og 6: et mønster med `\n` rammer ikke en CRLF-fil, og kørslen
     bagefter ligner et svar.
   - **`OpenAppAsync()` uden viewport giver den brede udgave**, fordi Playwrights standard er
     1280×720 og 1280 px *er* `xl`. Det var kosmetik indtil nu; med vagten afgør viewporten også
     adfærd.

   Holdt **ude** af `CLAUDE.md` med vilje: at `t.ready` afvises mens `t.finished` resolverer, at jsdom
   beholder `view-transition-name`, og hvorfor vagten mod `xl` findes. Alle tre står som kommentarer
   præcis dér hvor nogen ville bryde dem — hvilket er stærkere end en linje i en fil man skimmer.

8. **Omgørelsen** (afsnit 8b), **gjort 2026-08-25** efter at brugeren afviste asymmetrien: `styles.css`
   fik sin regel, spalten og rækkerne fik `view-transition-group`, den fjerde vagt røg ud, den ene
   Vitest vendte, og en E2E-rejse på 1400 px kom til (70 → 71). Slutmåling 174/310/71/301.
   `WideScreen` er ikke længere injiceret i `TaskStore`.

Hver opgave slutter med sin egen commit.
