# Redigering af en opgaves titel

Brugerens ønske 2026-08-27: *"Jeg skal kunne redigere titlen på todos."*

Titlen er det ene felt på en opgave der aldrig har kunnet rettes. Den sættes ved oprettelsen —
`new-task-input` på listen — eller af en import, og derefter står den fast. Alle seks andre felter i
detaljepanelet kan rettes.

## 1. Hvad leverancen ikke er

**Backenden og storen røres ikke.** Det er hele overraskelsen i opgaven, og det er værd at måle frem
for at antage, fordi den nære forventning er en kontraktændring:

- `PUT /api/tasks/{id}` **kræver allerede** `title` og validerer den. `UpdateTodoTaskRequest` har
  feltet som `required` med `minLength: 1, maxLength: 500`, og `TaskEndpoints.cs` kalder
  `ValidateTaskTitle` på opdateringsvejen med `ErrorCodes.TaskTitleRequired` og
  `ErrorCodes.TaskTitleTooLong`. Titlen skrives til entiteten i `task.Title = request.Title`.
- `TaskChanges` har **allerede** `title?: string`, og `TaskStore.update` bærer **allerede** `title` i
  sit `current`-objekt — netop fordi backenden læser et fraværende felt som "ryd", så hvert felt skal
  med i hver `PUT`. Uden det ville enhver redigering af et andet felt have slettet titlen, og det har
  den ikke.

Så: **ingen kontraktændring, ingen `scripts\generate-api.ps1`, ingen migrering, intet nyt endpoint,
ingen ny metode i storen.** Leverancen er ét felt i `task-detail.html`, én metode i `task-detail.ts`,
to oversatte nøgler og vagterne.

## 2. Beslutningerne

Fire, alle truffet af brugeren 2026-08-27.

**Feltet bor i detaljepanelet, ikke inline i rækken.** Et `<input>` med etiketten "Titel", som
opgavestiller-feltet: gemmer på blur og Enter. Alternativet — dobbeltklik på titlen i listen — blev
afvist, fordi titlen står inde i rækkens `<button>`, hvis tilgængelige navn `TaskListScreen.RowTitled`
matcher i sin helhed: et `<input>` derinde er ugyldig HTML, og et klik i feltet ville folde rækken ud.
Prisen for panelet er, at titlen står to steder i én spalte — rækken og feltet. Det er ærligt: rækken
er den der bliver rettet.

**Feltet står først i panelet, over Deadline.** Ikke pynt. Side om side har højre spalte i dag intet
der siger hvilken opgave den hører til — accentkanten på rækken i venstre spalte er hele signalet — så
titlen øverst giver den brede udgave en overskrift gratis, uden en ekstra komponent.

**En tom titel ruller den gamle tilbage frem for at vise en fejl.** Serveren afviser tomt med
`TaskTitleRequired`, men den afvisning skal aldrig nås. Præcedensen er entydig: både
`TaskList.create` og `TaskDetail.createSubTask` returnerer **tavst** på en tom titel, uden et kald.
Titlen der kommer til syne igen *er* svaret. Alternativet — en fejllinje — koster et fejlsignal i
`TaskStore`, som **ikke har et i dag** (hvert gem i panelet slutter på `.catch(() => {})`), en
`errors.*`-nøgle i begge sprogfiler og en `@if`-gren mere som `ContrastTests` skal have et fixture til.

**Genvejen er `Alt+Shift+I`, og status beholder sit `T`.** Laget har D, S, O, N, T, V, U, L optaget —
de otte panelfelter — og titlen bliver det niende. `I` er "tItel"s andet bogstav, frit i laget.
Nøglen i registret er `lag+tast`, så `Alt+I` (opgavelinket) og `Alt+Shift+I` er forskellige nøgler og
kolliderer ikke; præcis som `Alt+Shift+D` findes uden et `Alt+D`.

**Bemærk spændingen der blev afvist, så den ikke rejses igen som en inkonsekvens.** `CLAUDE.md`
begrunder status' `T` med at *"startdatoen har det stærkere krav på et felt man skriver i"*. Titlen
**er** et felt man skriver i, så efter appens egen regel har den det stærkeste krav på `T` af de tre.
Den blev alligevel ikke flyttet: det ville flytte en genvej brugeren har i fingrene, og rette
`CLAUDE.md`, `task-detail.spec.ts` og genvejsrejserne med. Huskereglen for `I` er svagere, og det er
den bevidste handel.

## 3. Feltet

Markup'en er opgavestiller-feltets, linje for linje: `<label class="block">` med etiket-`<span>` og
mærkat, og et `<input type="text">` med `appShortcut="i"` / `appShortcutModifier="alt-shift"`.
Mærkaten er `⇧I` med `aria-hidden="true"` — bærende, ikke pynt: en mærkat inde i en `<label>` indgår
ellers i kontrollens tilgængelige navn.

Nyt ud over det er kun `data-testid="title-input"`, fordi E2E skal kunne finde feltet, og:

**`maxlength="500"`, som spejler serverens grænse.** Med den kan `TaskTitleTooLong` ikke længere nås
fra feltet, og sammen med tilbagerulningen af den tomme titel betyder det, at **serveren aldrig kan
afvise et titelgem**. Det er præcis det der lader os slippe for fejlmekanikken i afsnit 2. Prisen,
sagt højt: en indsat titel på 600 tegn bliver **tavst klippet** til 500 af browseren.

### Hvorfor gemningen ikke kan gå gennem `save()`

Den generiske `save({ title: text(...) })` er **forkert**, og fælden er tavs. `text()` svarer
`undefined` på tomt, og `update`s spread — `{ ...current, ...changes }` — lader et eksplicit
`undefined` **rydde** feltet; det er kommenteret i storen som en egenskab, ikke et uheld. Serveren
ville derfor få `title: undefined`, afvise med 400, og `.catch(() => {})` ville sluge den. Feltet stod
tomt, mens rækken beholdt sin titel, og intet sagde hvorfor.

Metoden er derfor titel-specifik, og reglen er **én linje frem for en gren pr. tilfælde**: feltet
viser altid hvad opgaven holder.

```ts
protected saveTitle(field: HTMLInputElement): void {
  const trimmed = field.value.trim();
  // Tomt ruller den gamle titel tilbage; mellemrum i enderne viser den trimmede form
  // serveren får. Begge gør feltet enigt med opgaven uden en gren pr. tilfælde.
  field.value = trimmed || this.task().title;

  if (!trimmed) {
    return;
  }

  this.save({ title: trimmed });
}
```

**Den direkte DOM-skrivning er nødvendig af samme grund som `[checked]`-fælden.** Ruller vi tilbage,
skifter signalet ikke — `task().title` er uændret — så `[value]`-bindingen genanvendes ikke, og feltet
ville stå tomt, mens opgaven beholdt sin titel. Det er ordret den fejl `CLAUDE.md` beskriver for
afkrydsningsfelter, ét felt over, og rettelsen er den samme: skriv elementets værdi tilbage fra
tilstanden.

`update`s egen lighedstjek fanger "uændret", så Enter efterfulgt af blur ikke gemmer to gange.

## 4. To konsekvenser, sagt højt frem for opdaget senere

**Fuldførte opgaver kan ikke få titlen rettet.** Deres række er et almindeligt `<li>` uden panel, og
de er ikke valgbare — så der er intet sted feltet kan stå. Det følger af den eksisterende arkitektur
og laves ikke om her; vil man rette en fuldført opgaves titel, slår man status tilbage først.

**En omdøbning under en aktiv søgning kan flytte rækken væk under hænderne.** `TaskStore.matching`
filtrerer på titel, så omdøber du en opgave så den ikke længere matcher søgningen, forsvinder rækken
ved genindlæsningen. Side om side flytter `selected` sig til den næste. Det er allerede den kodede
opførsel for "den valgte søges væk", og den er rigtig her: panelet skal ikke vise en opgave listen
ikke har.

## 5. Testplanen

**To eksisterende assertions skal rettes, og de er de eneste.** Begge i `task-detail.spec.ts`, begge
**rækkefølge-følsomme arrays** — kommentaren over den første siger allerede hvorfor rækkefølgen er
assertionens tænder: en mængdesammenligning ville bestå, hvis to felter byttede bogstav. Feltet står
først, så begge får et nyt **første** element: `'Alt+Shift+I'` og `'⇧I:true'`.

**`KeyboardJourneyTests` rører sig ikke, og det er rigtigt.** `BadgeCount = 9` gælder den **tomme**
liste, hvor der ikke findes noget panel — de ni statiske `Alt+bogstav` *er* hele siden. Søsteren
`Every_shortcut_letter_on_a_seeded_list_with_the_panel_open_is_its_own` tæller **fra fixturet**, så
den opdager `⇧I` af sig selv og ville fælde en kollision uden at et tal skal flyttes.

> **Rettelse, målt efter at koden var skrevet: afsnittet ovenfor er forkert, og `CLAUDE.md` var
> kilden.** `KeyboardJourneyTests` flyttede sig, og den fældede tre tests. Totalen i søsteren er
> `BadgeCount + Math.Min(RowDigits, selectable) + PanelFieldShortcuts + WaitingOnFieldShortcut` —
> **aritmetik over konstanter**, ikke en optælling fra fixturet. Kun rækkeleddet kommer fra fixturet;
> `PanelFieldShortcuts` er et nedskrevet tal og skulle 7 → 8. Den samme konstant bærer
> `A_panel_badge_never_covers_the_field_below_it` i begge bredder, så én glemt bump gav **tre** røde
> på én gang. Dertil `ContrastTests`' badge-total, som er en bar literal: 24 → 25.
>
> Kun `⇧I`s *distinkthed* blev bekræftet uden indgreb — kollisionsdelen af påstanden bestod. Det er
> den halvdel af `CLAUDE.md`s sætning der holdt, og forskellen er hele lektionen: **en vagt kan tælle
> fra fixturet i sin ene påstand og fra en konstant i sin anden.** `CLAUDE.md` er rettet, så næste
> leverance ikke arver forudsigelsen.

**`ContrastTests` behøver ingen ny fixture-opgave**, modsat den sædvanlige regel om at en `@if`-gren
er umålt indtil et fixture rammer den. Feltet er **ikke betinget**, og hver opgave har en titel per
definition, så vagten måler `el.value` på det i begge temaer fra første kørsel. Det er den ene gang
det krav er gratis.

Nyt af tests — **+2 Vitest** (301 → 303) og **+1 E2E** (71 → 72), ingen ændring i Core eller Api:

| Test | Hvad den påstår | Set fejle ved |
| --- | --- | --- |
| Vitest, `task-detail.spec.ts` | Et tomt felt kalder **ikke** `updateTask`, og `field.value` er den gamle titel igen | At fjerne DOM-skrivningen |
| Vitest, `task-detail.spec.ts` | Et gem trimmer og kalder `updateTask` med den trimmede titel | At droppe `.trim()` |
| E2E, `TaskListJourneyTests` | Omdøber en opgave og påstår at **rækken** bærer det nye navn | At udelade `title` af gemningen |

E2E-rejsen påstår på `RowTitled`, som matcher rækkeknappens **fulde** tilgængelige navn, så påstanden
er ærlig. Og den behøver ingen genindlæsning: `update` slutter med `load()`, så **første**
rækkepåstand efter et gem er allerede serverens svar.

Vagten der skal ses fejle først er den tomme titel. Den er den eneste af de tre der beskytter en
mekanisme der ikke findes andre steder i appen.

## 6. Rækkefølgen i implementeringen

1. To nøgler i `da.json` og `en.json` — `tasks.title`. **Begge filer**, ellers fejler paritetstesten.
2. Feltet i `task-detail.html` som **første** element, og `saveTitle` i `task-detail.ts`.
3. Ret de to arrays i `task-detail.spec.ts` og se dem grønne.
4. De to nye Vitest, hver set fejle på sin mutation fra tabellen.
5. E2E-rejsen. **Kør `scripts\build-web.ps1` før E2E** — suiten bygger ikke Angular, så uden det
   tester Playwright den forrige udgave af frontenden.
6. `Check.cmd` og sammenlign tallene mod 174 / 310 / 303 / 72.

## 7. Parkeret til bagefter

Brugeren viste 2026-08-27 et skærmbillede: appen er i mørkt tema, men statusvælgerens `<select>`-popup
males hvid. Det er en **selvstændig fejl** og hører ikke i denne leverance, men den er næste opgave.
Fire ting er målt, så de ikke gættes igen:

- `scheme-light-dark` **er** på `<body>` i `src/Todo.Web/src/index.html`. Årsagen er altså ikke en
  manglende `color-scheme`-erklæring.
- Der er **ingen** manuel tema-toggle: ingen `prefers-color-scheme` i egen CSS, ingen
  `classList`-manipulation, intet `darkMode` i konfigurationen. Tailwind 4's `dark:` er derfor
  OS-indstillingen.
- Konsekvens: `color-scheme: light dark` *burde* resolve til dark, når OS'et er mørkt. At den ikke gør
  peger på WebView2's native popup-chrome frem for på CSS'en.
- Popup'en er ikke DOM, så `ContrastTests` kan **ikke** måle den — samme handel som `<datalist>`,
  dokumenteret i `task-detail.html`. En vagt skal derfor findes et andet sted, eller undtagelsen skal
  skrives ned.

Mål i **Photino-vinduet** frem for i en browser: de to runtimes har hver sin popup-implementering, så
en måling i Chrome svarer på det forkerte spørgsmål.
