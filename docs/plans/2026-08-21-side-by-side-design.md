# Side by side på brede skærme — design

Truffet 2026-08-21. Status: designet er godkendt afsnit for afsnit; implementeringen følger.

Ønsket: på en skærm på Full HD og derover skal opgaverne stå i venstre side og detaljerne i
højre, frem for at detaljerne foldes ud inde i listen.

## 1. De fem beslutninger, og hvem der traf dem

Alle fem er brugerens, truffet 2026-08-21. De står her, fordi et gæt falder den anden vej i
mindst to af dem.

1. **Brydepunktet er vinduets bredde ≥ 1280 px** — Tailwinds `xl` — ikke skærmens opløsning.
   Ordlyden i ønsket sagde "skærm", men et halvt vindue på en Full HD-skærm ville så blive
   tvunget i to spalter á ~460 px, hvor detaljepanelets datofelter bliver for smalle. Viewporten
   er det der har pladsen.
2. **Valget bliver stående.** Et klik på den valgte række fravælger den *ikke*, som den ellers
   gør i dag. Klassisk master-detail.
3. **Den første opgave vælges automatisk**, så højre spalte ikke står tom ved indlæsning.
   Brugeren valgte det med prisen kendt: "første" skal defineres på tværs af sektionerne.
4. **Auto-valget gælder kun side by side.** I én spalte åbnes intet af sig selv. Grunden er ikke
   bekvemmelighed: et udfoldet panel skubber resten af listen ~300 px ned, så en app der åbner med
   opgave nummer ét foldet ud, skjuler de øvrige.
5. **Når det valgte falder væk** — søgt væk, slettet, flyttet til Fuldført med fuldførte skjult —
   vælges **den første synlige**. Konsekvensen er accepteret: panelet skifter opgave, mens du
   skriver i søgefeltet.

## 2. Formen — og hvorfor brydepunktet er et signal, ikke en klasse

Detaljepanelet bor i dag inde i `TaskRow` bag `@if (expanded())`, altså inde i `<li>`'en. Side by
side kræver, at det kan stå **et andet sted i DOM'en**, så det udskilles i en `TaskDetail`
(`app-task-detail`): de ~200 skabelonlinjer plus `save`, `saveStatus`, `stopEditingNote`,
underopgave-metoderne og **begge effekter** — noteeditorens fokus og `askingWho`-effekten.

**To kopier er ikke en mulighed, og det er den bærende begrundelse for hele formen.** Højre spalte
må ikke være `hidden xl:block`: `hidden` renderer stadig i DOM'en, så `data-testid="task-detail"`
ville findes **to** gange på en smal skærm — én gang i rækken, én gang i den skjulte spalte — og
Playwright vælger tavst den første. Derfor er begge sider en `@if` på et signal:
`@if (wide())` om højre spalte, og `@if (expanded() && !wide())` om panelet inde i rækken. En
ren CSS-løsning kan ikke skrives.

Signalet bor i en injicerbar `WideScreen` i `layout/wide-screen.ts` og drives af
`window.matchMedia('(min-width: 80rem)')`. `80rem` er præcis Tailwinds `xl`, så brydepunktet står
**ét** sted og kan ikke drive fra `xl:`-klasserne.

**jsdom har ingen `matchMedia`.** Uden en vagt i konstruktøren vælter hver Vitest der rører
opgavelisten. Vagten defaulter til smal, altså til dagens opførsel, og signalet kan sættes fra en
test. Prisen er, at vagtens gren er umålt medmindre den har sin egen spec — den har den.

## 3. Valget er en afledning, ikke en effekt

```ts
protected readonly selected = computed(() => {
  const id = this.selectedId();
  const selectable = this.selectableTasks();
  return selectable.find((t) => t.id === id) ?? (this.wide.wide() ? selectable[0] : undefined);
});
```

**De tre beslutninger 3, 4 og 5 er den samme regel**, når den udtrykkes som en afledning.
Auto-valg ved indlæsning er "intet gyldigt id → tag den første". At valget følger med, når du
søger den valgte væk, sletter den eller flytter den til Fuldført, er **samme** linje: listen
skifter, `find` fejler, `[0]` svarer. Og at auto-valget kun gælder side by side er `wide()`-leddet.

En effekt skulle have skrevet i et signal, den selv læser, og skulle kaldes fra `load`, `remove`,
`searchFor`, `setShowCompleted`, `setShowSomeday` og statusskiftet — **seks** kaldesteder der kan
drive fra hinanden. Afledningen har nul.

To konsekvenser, som er beslutninger frem for detaljer:

**Fuldførte opgaver kan ikke vælges.** Deres rækker er i dag et almindeligt `<li>` uden panel, så
`selectableTasks()` er deadline-sektionerne, Venter på og Måske, i visuel rækkefølge. Det betyder,
at **den tomme tilstand findes alligevel**: er kun fuldførte synlige, har højre spalte ingenting at
vise. Auto-valget fjerner altså ikke behovet for hjælpeteksten og dens to sprognøgler.

**Den smalle udgave bliver enklere.** `searchFor`s eksplicitte `expandedId.set(null)` kan gå:
`find` fejler af sig selv, og `wide()` er falsk, så en bortsøgt række falder sammen uden at nogen
beder om det. Kun `editingNote`-nulstillingen bliver.

## 4. Layoutet

`app.html`s `max-w-2xl` (672 px) er hele appens loft, så to spalter ikke kan lægges ind under det.
Skallen får `xl:flex xl:h-screen xl:max-w-none xl:flex-col` og en ny `<div class="xl:min-h-0
xl:flex-1">` om `<router-outlet />`. **Hele omlægningen står bag `xl:`**, så den smalle udgave er
byte for byte uændret — det er derfor de fire andre skærme og alle 47 E2E måler nøjagtigt det de
måler i dag. Prisen er, at de fire andre skærme bliver brede på en stor skærm og derfor får deres
eget `xl:max-w-2xl`.

**`min-h-0` er bærende — men kun ét sted, og designet gættede forkert på hvor.** Reglen er, at et
flex- eller gitterbarns `min-height` er `auto` og derfor nægter at krympe under sit indhold. Planen
skrev derfor klassen på **både** wrapperen og de to spalter. Målt ved mutation: fjernes den fra
spalterne fælder det **ingenting**, fordi et barn hvis `overflow` ikke er `visible` allerede har
automatisk minimumstørrelse nul — `overflow-y-auto` gør arbejdet selv. Fjernes den fra wrapperen om
`router-outlet`, hvor overflow *er* visible, siger `The_columns_scroll_on_their_own`
*"The list column did not scroll inside itself: 0"*. De tre overflødige klasser er fjernet igen.

Opgavelisten pakkes i et gitter:

```
xl:grid xl:h-full xl:min-h-0 xl:grid-cols-[30rem_minmax(0,1fr)] xl:gap-6
```

Venstre spalte er **30rem = 480 px**, altså præcis den bredde hele appen er designet og testet i,
så listen ser ud som i dag og ingen eksisterende bredde-antagelse flytter sig. Detaljerne får
resten — 712 px på et 1280-vindue. `minmax(0,1fr)` frem for `1fr`, så et langt ord i panelet
ombrydes frem for at skubbe gitteret bredere. Begge spalter får `xl:min-h-0 xl:overflow-y-auto`.

Den tomme tilstand er en centreret hjælpetekst i højre spalte, nøglen `tasks.selectPrompt` i
**begge** sprogfiler.

## 5. Test

**Alle 47 E2E kører på `ColumnWidth = 480`** — hver enkelt fil har konstanten. Side by side er
altså fuldstændig umålt af den nuværende suite, kontrastvagten iberegnet. Det er hele grunden til
at testafsnittet er så stort som det er.

To locator-målinger, gjort frem for gættet:

- `TaskListScreen.Detail` er **allerede** side-uafhængig (`Page.GetByTestId("task-detail")`), så de
  fleste locators virker uændret i begge layouts — netop fordi `@if`'en garanterer præcis én
  forekomst.
- `DetailFor(title)` er **række-skopet** og har fem kaldere (`ContrastTests`,
  `DeferUntilJourneyTests`, `DelegateJourneyTests`, `WaitingJourneyTests` ×2). Den er dermed en
  *smal-udgave*-locator, og det skal stå i dens dokumentation frem for at blive opdaget af en
  fremtidig bred test der fejler uforståeligt.

Ny fil, `SideBySideJourneyTests`, på et 1280-viewport — `BrowserTest` tager allerede en
`ViewportSize`. Fem rejser, hver med den mutation der skal ses fælde den:

| Rejse | Mutationen der skal fælde den |
| --- | --- |
| Panelet står i højre spalte, ikke i rækken | fjern `!wide()` fra rækken → to paneler |
| Klik på den valgte fravælger ikke | behold dagens toggle |
| Auto-valg ved indlæsning | `?? selectable[0]` → `?? undefined` |
| Bortsøgt valg falder til den første synlige | samme led |
| Spalterne ruller hver for sig | fjern `xl:min-h-0` |

**Hvad mutationerne faktisk gav, kørt frem for forudsagt.** Viewporten blev 1400 og ikke 1280, så
ingen påstand afhænger af om en scrollbar tæller med.

- `?? this.store.tasks()[0]` i stedet for `selectable[0]` fældede **tre** rejser, ikke to:
  auto-valget, det bortsøgte valg **og** "kun fuldførte efterlader hjælpeteksten" — for serverens
  liste indeholder de fuldførte, så mutationen gør dem valgbare.
- Uden `!wide()` på rækkens `[expanded]` fejlede **fem af seks**, fordi det dublerede
  `data-testid="task-detail"` bryder strict mode for hver locator bygget på `Detail`. Vitest fældede
  til gengæld **præcis én** — netop den der er skrevet til det.
- Uden `xl:min-h-0` på spalterne fejlede **ingenting**. Se rettelsen i afsnit 4.
- **Og den dyreste:** "klikket fravælger ikke" var **grøn** under sin egen mutation. En
  Playwright-påstand om at ingenting ændrede sig kan ikke laves race-fri ved at polle — den første
  poll der lykkes afslutter ventetiden, og lige efter klikket har Angular ikke re-renderet. En probe
  viste feltet gå `2026-08-14` → `2026-08-16` **efter** at påstanden var bestået. Og den nære fælde
  er værre: den første rettelse — klik, `FillAsync`, og påstå at redigeringen landede på den valgte
  opgave — bestod af **samme** grund, fordi `save()` læste komponentens forældede `task()` og gemte
  på den forrige opgave. Kun en **rundtur** imellem (slå "vis fuldførte" til og vent på rækken)
  gør læsningen ærlig. Rejsen er set fejle med `But was: '2026-08-16'`.

**Auto-valg-rejsens fixture er dens tænder.** Sås den opgave der skal vælges **først** i
seed-rækkefølgen, ville en implementering der bare tager `tasks[0]` fra serverens svar bestå lige
så godt. Den sås **sidst** og løftes op af serverens deadline-sortering.

Kontrastvagten får en ny teori i begge temaer på 1280 med tre tilstande: to-spalte-listen, det
udfyldte panel og hjælpeteksten. Den sidste kræver et fixture med kun en fuldført opgave, siden
fuldførte ikke er valgbare.

Vitest: seks tilfælde på `selected()`-afledningen — intet valgt + smal, intet valgt + bred, valgt
id findes, bortsøgt + bred, bortsøgt + smal, fuldført er ikke valgbar — plus én på `WideScreen`s
jsdom-vagt.

De otte Alt-bogstaver bliver otte: panelet har ingen genveje, så
`Every_shortcut_letter_on_screen_is_its_own` skal ikke røres.

## 6. Fravalgt, så det er et valg og ikke en forglemmelse

- **Ingen trækbar skillelinje.** Bredden er `30rem`; en indstilling koster en række i `Setting`, et
  felt på kontrakten og en rundtur for noget brugeren ikke har bedt om.
- **Intet gemt valg** på tværs af sessioner.
- **Ingen piletast-navigation** mellem rækker.
- **Intet nyt Alt-bogstav.**
- **Ét brydepunkt, ikke tre.** `md` og `2xl` får ingen mellemtilstande.

## 7. Målingen kun brugeren kan lave

`xl:h-screen` er `100vh`, og Playwright på 1280 måler et *browser*-viewport. Om `100vh` er vinduets
klienthøjde i **Photino/WebView2** — og om to spalter faktisk ser rigtige ud i et maksimeret vindue
på en Full HD-skærm — kan ingen agent måle her. Start appen, maksimér, og se om højre spaltes
rulning slutter ved vinduets kant frem for at stikke ud.

## 8. Filer

Ny: `layout/wide-screen.ts`, `layout/wide-screen.spec.ts`, `tasks/task-detail.ts`,
`tasks/task-detail.html`, `tests/Todo.E2E/SideBySideJourneyTests.cs`.

Ændret: `tasks/task-row.{ts,html}`, `tasks/task-list.{ts,html}`, `tasks/task-list.spec.ts`,
`app.html`, de fire andre skærmes rod (`xl:max-w-2xl`), `i18n/da.json` + `i18n/en.json`
(`tasks.selectPrompt`), `tests/Todo.E2E/TaskListScreen.cs` (dokumentation på `DetailFor`),
`tests/Todo.E2E/ContrastTests.cs`, `CLAUDE.md` (Testtal), `docs/HANDOFF.md`.
