# Tastaturgenveje til panelets felter og de nummererede rækker — implementeringsplan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task.

**Mål:** Otte `Alt+Shift+bogstav`-genveje til detaljepanelets felter, og `Alt+1`–`9` til de første
ni valgbare rækker på opgavelisten.

**Arkitektur:** Registret får en nøgle på formen `lag+tast`, beregnet af én delt hjælpefunktion som
både direktivet og `app.ts` kalder, så mærkat og virkelighed ikke kan drive fra hinanden.
Registreringen flytter fra `ngOnInit`/`ngOnDestroy` til en `effect` med `onCleanup`, fordi en rækkes
nummer ændrer sig i levende live. Ingen backend, ingen kontrakt, ingen migrering.

**Design:** `docs/plans/2026-08-24-keyboard-shortcuts-design.md` — læs det først. Planen retter to
fejl i det; de står som opgave 0 og opgave 8.

**Tech stack:** Angular 22 med signals (ingen forms, ingen NgRx), Vitest, Playwright, xUnit.

---

## Opgave 0: To rettelser til designet, før koden røres

Designdokumentet er skrevet før koden blev læst, og to af dets påstande holder ikke. Ret dem i
designet **først**, så planen og designet ikke siger hver sin ting.

**Filer:** Modificér `docs/plans/2026-08-24-keyboard-shortcuts-design.md`, afsnit 3 og 6.

**Rettelse 1 — `BadgeCount = 9` skal ikke ændres.** Designets afsnit 6 siger, at konstanten skal
blive tre. Målt: `Holding_Alt_reveals_the_badges_and_releasing_it_hides_them_again` og
`Every_shortcut_letter_on_screen_is_its_own` **sår ingen opgaver** — de kalder `OpenAppAsync` og
intet andet — så listen er tom, ingen række har et nummer, og der er intet panel. Begge bliver ved
at måle ni. Kravet er derfor et **andet**: guarden er blind for det nye lag, så længe den kun kører
på en tom liste, og den skal have en søsterpåstand på en sået liste med panelet åbent. Skriv det om
til det.

**Rettelse 2 — registreringen kan ikke ligge i `ngOnInit`.** Designets afsnit 3 forudsætter tavst
den nuværende livscyklus. Men `@for` sporer på `task.id`, så en søgning der omfordeler 1–9 giver
**samme komponentinstans et nyt nummer** — `ngOnInit` er kørt for længst, og rækken ville svare på
sit gamle ciffer for evigt. Registreringen skal reagere på inputtet. Skriv det ind.

**Commit:**

```bash
git add docs/plans/2026-08-24-keyboard-shortcuts-design.md
git commit -m '📝 Designet retter sig selv: badge-tallet står, men registreringen skal følge inputtet'
```

---

## Opgave 1: Nøglen og mærkaten som én kilde

**Filer:**

- Opret: `src/Todo.Web/src/app/shortcuts/shortcut-key.ts`
- Opret: `src/Todo.Web/src/app/shortcuts/shortcut-key.spec.ts`

**Trin 1: Skriv den fejlende test**

`shortcut-key.spec.ts`:

```ts
import { shortcutKey, shortcutLabel } from './shortcut-key';

describe('shortcutKey', () => {
  // De to lag er hele grunden til at nøglen ikke længere er bogstavet alene: Alt+O er
  // navigationen til opgavelisten, og Alt+Shift+O er panelets opgavestiller-felt.
  it('should keep the two layers apart for the same letter', () => {
    expect(shortcutKey('alt', 'o')).not.toBe(shortcutKey('alt-shift', 'o'));
  });

  // Alt+Shift+D rapporterer 'D' fra tastaturet, Alt+D rapporterer 'd'. Nøglen skal være den
  // samme, uanset hvilken vej den kom.
  it('should fold the case, because Alt+Shift reports an upper-case key', () => {
    expect(shortcutKey('alt-shift', 'D')).toBe(shortcutKey('alt-shift', 'd'));
  });

  it('should read the label off the same two fields the key is built from', () => {
    expect(shortcutLabel('alt', 'k')).toBe('Alt+K');
    expect(shortcutLabel('alt-shift', 'd')).toBe('Alt+Shift+D');
  });
});
```

**Trin 2: Kør den og se den fejle**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false --run shortcut-key
```

Forventet: `Failed to resolve import "./shortcut-key"`.

**Trin 3: Skriv implementeringen**

`shortcut-key.ts`:

```ts
/** Alt alene bærer navigationen og listens kontroller; Alt+Shift bærer panelets felter. */
export type ShortcutModifier = 'alt' | 'alt-shift';

/**
 * The registry key: layer plus key, so Alt+O and Alt+Shift+O are two entries rather than one.
 *
 * Lower-cased, because Alt+Shift+D reports `event.key === 'D'` while Alt+D reports 'd', and the
 * registration and the lookup have to meet.
 */
export function shortcutKey(modifier: ShortcutModifier, key: string): string {
  return `${modifier}+${key.toLowerCase()}`;
}

/**
 * What `aria-keyshortcuts` says. Derived from the same two fields as the key above, so the label a
 * screen reader announces cannot drift from the combination that actually works.
 */
export function shortcutLabel(modifier: ShortcutModifier, key: string): string {
  const letter = key.toUpperCase();

  return modifier === 'alt-shift' ? `Alt+Shift+${letter}` : `Alt+${letter}`;
}
```

**Trin 4: Kør testen igen** — forventet: 3 passed.

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/shortcuts/shortcut-key.ts src/Todo.Web/src/app/shortcuts/shortcut-key.spec.ts
git commit -m '✨ Genvejsnøglen bærer sit lag, så Alt+O og Alt+Shift+O er to genveje'
```

---

## Opgave 2: Registret må ikke slette en anden rækkes registrering

`ShortcutStore` er last-writer-wins med vilje, og det er stadig det rigtige. Men med numre der
flytter sig bliver `unregister` farlig: to rækker der bytter numre kører hver sin `effect`-oprydning,
og rækkefølgen mellem to effekter er ikke garanteret, så den ene kan slette den nøgle den anden lige
har skrevet. Resultatet er et ciffer der ikke gør noget, uden at nogen test siger fra.

**Filer:** Modificér `src/Todo.Web/src/app/shortcuts/shortcut-store.ts` og
`shortcut-store.spec.ts`.

**Trin 1: Skriv den fejlende test** — tilføj i `shortcut-store.spec.ts`:

```ts
// To rækker der bytter numre: den nye registrering på '3' må ikke blive slettet af den gamle
// indehavers oprydning. Uden vagten er nøglen væk, og Alt+3 gør ingenting.
it('should keep a registration a newer caller has taken over', () => {
  const store = new ShortcutStore();
  const old = () => {
    throw new Error('should not run');
  };
  let activated = 0;
  const current = () => activated++;

  store.register('alt+3', old);
  store.register('alt+3', current);
  store.unregister('alt+3', old);

  expect(store.activate('alt+3')).toBe(true);
  expect(activated).toBe(1);
});
```

**Trin 2: Kør og se den fejle** — forventet: `expect(store.activate('alt+3')).toBe(true)` giver
`false`, fordi `unregister` sletter uanset hvem der beder om det.

**Trin 3: Implementér** — i `shortcut-store.ts`:

```ts
  /**
   * Registering the same key twice is still last-writer-wins, deliberately. What the second
   * argument buys is that the loser's cleanup cannot delete the winner's entry: a row's number
   * changes while the app runs, and the order between two effects' cleanups is not guaranteed.
   */
  unregister(key: string, activate?: () => void): void {
    if (activate && this.targets.get(key) !== activate) {
      return;
    }

    this.targets.delete(key);
  }
```

**Trin 4: Kør hele Vitest-filen** — forventet: alle grønne, inklusive de fem gamle.

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/shortcuts/shortcut-store.ts src/Todo.Web/src/app/shortcuts/shortcut-store.spec.ts
git commit -m '🐛 En rækkes oprydning sletter ikke det ciffer en anden række har overtaget'
```

---

## Opgave 3: Direktivet får laget, den tomme tast og en effekt

**Filer:** Modificér `src/Todo.Web/src/app/shortcuts/shortcut.ts`. Opret
`src/Todo.Web/src/app/shortcuts/shortcut.spec.ts`.

**Trin 1: Skriv de fejlende tests.** Tre påstande, hver med sin egen fejlmulighed. Brug en
værtskomponent med et signal, så nøglen kan ændres midt i testen:

```ts
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Shortcut } from './shortcut';
import { ShortcutStore } from './shortcut-store';

@Component({
  imports: [Shortcut],
  template: `<button [appShortcut]="key()" [appShortcutModifier]="modifier()">x</button>`,
})
class Host {
  readonly key = signal('3');
  readonly modifier = signal<'alt' | 'alt-shift'>('alt');
}

describe('Shortcut', () => {
  it('should announce the layer in aria-keyshortcuts', () => { /* 'Alt+3', then 'Alt+Shift+3' */ });

  // Række ti og frem har intet nummer. Uden guarden registreres den tomme streng, og den første
  // uhåndterede tast rammer den.
  it('should register nothing for an empty key', () => { /* key.set(''); activate('alt+') === false */ });

  // @for sporer på task.id, så en søgning der omfordeler 1–9 giver SAMME instans et nyt nummer.
  it('should follow the key when it changes, and let the old one go', () => {
    /* key.set('5'); activate('alt+3') === false; activate('alt+5') === true */
  });
});
```

**Trin 2: Kør og se dem fejle.** Forventet: `appShortcutModifier` findes ikke (bygningen stopper),
og efter at inputtet er tilføjet fejler den tredje på `activate('alt+3')` fordi `ngOnInit` kun kørte
én gang.

**Trin 3: Implementér.** `ngOnInit`/`ngOnDestroy` **udgår helt** — én `effect` med `onCleanup` gør
begge ting og håndterer desuden nøgleskiftet:

```ts
  readonly appShortcut = input.required<string>();
  readonly appShortcutModifier = input<ShortcutModifier>('alt');
  readonly appShortcutAction = input<'focus' | 'activate'>('focus');

  // null frem for en tom streng: attributten skal være væk, ikke tom, for guarden i E2E påstår
  // at række ti slet ikke har en.
  protected readonly label = computed(() =>
    this.appShortcut() ? shortcutLabel(this.appShortcutModifier(), this.appShortcut()) : null,
  );

  constructor() {
    // En effekt frem for ngOnInit/ngOnDestroy, fordi nøglen ændrer sig i levende live: en række
    // beholder sin komponentinstans (@for sporer på id) og får et nyt nummer, når listen gør.
    // onCleanup afmelder den gamle nøgle, og callbacket sendes med, så oprydningen ikke kan
    // slette en registrering en anden række har overtaget imens.
    effect((onCleanup) => {
      const key = this.appShortcut();
      if (!key) {
        return;
      }

      const registryKey = shortcutKey(this.appShortcutModifier(), key);
      const activate = () => this.trigger();

      this.store.register(registryKey, activate);
      onCleanup(() => this.store.unregister(registryKey, activate));
    });
  }
```

`trigger()` er den nuværende krop af registreringen, uændret — inklusive kommentaren om at et
programmatisk `click()` ikke selv flytter fokus.

Host-bindingen bliver `'[attr.aria-keyshortcuts]': 'label()'`.

**Trin 4: Kør Vitest** — forventet: alle tre grønne, og de 281 eksisterende stadig grønne.

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/shortcuts/shortcut.ts src/Todo.Web/src/app/shortcuts/shortcut.spec.ts
git commit -m '✨ Direktivet kender sit lag og følger en tast der ændrer sig'
```

---

## Opgave 4: `app.ts` slår op i det rigtige lag

**Filer:** Modificér `src/Todo.Web/src/app/app.ts:40-52`. Test i `app.spec.ts`.

**Trin 1: Skriv den fejlende test.** To påstande: `Alt+K` rammer `alt`-laget, og `Alt+Shift+K`
rammer **ikke** samme registrering. Registrér gennem `ShortcutStore` direkte og udsend en
`KeyboardEvent` på `document` med `altKey` og `shiftKey` sat.

**Trin 2: Kør og se den fejle** — forventet: `Alt+Shift+K` aktiverer `alt+k`, fordi opslaget i dag
kun ser `event.key`.

**Trin 3: Implementér:**

```ts
    if (event.altKey && !event.ctrlKey && !event.metaKey) {
      // Ctrl og Meta er stadig udenfor: Ctrl+Alt er AltGr på et dansk tastatur, og at spise den
      // ville ødelægge indtastning af @, £ og $. Shift er derimod et lag og ikke en udelukkelse.
      const modifier = event.shiftKey ? 'alt-shift' : 'alt';

      if (this.shortcuts.activate(shortcutKey(modifier, event.key))) {
        event.preventDefault();
      }
    }
```

**Trin 4: Kør Vitest.**

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/app.ts src/Todo.Web/src/app/app.spec.ts
git commit -m '✨ Alt+Shift slår op i feltlaget og Alt i navigationens'
```

---

## Opgave 5: Panelets otte felter

**Filer:** Modificér `src/Todo.Web/src/app/tasks/task-detail.html` og `task-detail.ts` (tilføj
`Shortcut` og `ShortcutStore` til `imports` og som `protected` felt, så skabelonen kan læse
`shortcuts.altHeld()`).

**Trin 1: Skriv den fejlende Vitest** — én test der påstår, at panelet udsender otte
`aria-keyshortcuts`, og at de er `Alt+Shift+D/S/O/N/T/V/U/L`. Fixturet skal have status
`WaitingFor`, ellers findes `V` ikke, og noten skal være **ulukket** (`editingNote` falsk), ellers
findes `N` ikke.

**Trin 2: Kør og se den fejle** — forventet: 0 fundet.

**Trin 3: Implementér.** Mønstret er ens for de syv felter (deadline vist):

```html
  <input
    #deadline
    data-testid="deadline-input"
    appShortcut="d"
    appShortcutModifier="alt-shift"
    ...
  />
```

Bogstaverne: `d` deadline, `s` startdato, `o` opgavestiller, `t` status, `v` venter-på, `u` ny
underopgave. Noten er den eneste med `appShortcutAction="activate"` og sidder på knappen
`note-edit`. Sletteknappen får `appShortcut="l"` **uden** `appShortcutAction`, og en kommentar der
siger hvorfor:

```html
<!-- Kun fokus, i modsætning til resten af de aktiverende genveje: der er ingen bekræftelse og
     ingen fortryd i appen, så det andet tryk ER bekræftelsen. Skift den ikke til activate uden
     at lave en af de to ting først. -->
```

Badgen står ved siden af hvert felts `<span>`-mærkat, med samme klasser som de eksisterende:

```html
      @if (shortcuts.altHeld()) {
        <span data-testid="shortcut-badge" aria-hidden="true" class="ml-1 rounded border border-gray-500 px-1 text-xs text-gray-700 dark:border-gray-400 dark:text-gray-300">⇧D</span>
      }
```

`aria-hidden` er bærende: en mærkat inde i en `<label>` indgår ellers i feltets tilgængelige navn.

**Trin 4: Kør Vitest og `scripts\build-web.ps1`** (skabelonen skal typetjekkes af den rigtige
bygning).

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/tasks/task-detail.html src/Todo.Web/src/app/tasks/task-detail.ts src/Todo.Web/src/app/tasks/task-detail.spec.ts
git commit -m '✨ Alt+Shift plus feltets forbogstav går direkte til feltet i detaljepanelet'
```

---

## Opgave 6: Numrene på de første ni rækker

**Filer:** Modificér `src/Todo.Web/src/app/tasks/task-list.ts`, `task-list.html` (**tre**
`<li appTaskRow>`-blokke: linje ~135, ~177, ~237), `task-row.ts`, `task-row.html`.

**Trin 1: Skriv de fejlende Vitest i `task-list.spec.ts`** — tre påstande:

1. Række ét til ni bærer `Alt+1` … `Alt+9` i skærmens rækkefølge.
2. Række ti bærer **ingen** `aria-keyshortcuts`.
3. En fuldført række bærer ingen — den har intet panel at vælge.

Fixturet skal have mindst ti valgbare opgaver **plus** en fuldført mellem "Venter på" og "Måske", så
tredje påstand ikke består ved et tilfælde.

**Trin 2: Kør og se dem fejle.**

**Trin 3: Implementér.** `task-list.ts`:

```ts
  /**
   * The first nine selectable tasks, numbered as they appear on screen.
   *
   * Nine because there are nine digits worth having: Alt+0 is not a tenth row, it is a key nobody
   * would guess. The completed section has no numbers and is skipped, which is a consequence of
   * `selectableTasks` rather than a rule of its own - a completed row has no panel to select.
   */
  protected readonly numbers = computed(
    () => new Map(this.selectableTasks().slice(0, 9).map((task, i) => [task.id, i + 1])),
  );
```

`task-list.html`, i alle **tre** blokke: `[number]="numbers().get(task.id)"`.

`task-row.ts`:

```ts
  readonly number = input<number | undefined>();

  // Direktivet tager en streng, og den tomme streng er dens "ingen genvej" - række ti og frem.
  protected readonly shortcut = computed(() => this.number()?.toString() ?? '');
```

`task-row.html`, på rækkeknappen:

```html
    <button
      type="button"
      class="w-full text-left"
      [appShortcut]="shortcut()"
      appShortcutAction="activate"
      (click)="toggled.emit()"
    >
```

Badgen inde i knappen, med `aria-hidden="true"` — `TaskListScreen.RowTitled` matcher knappens
**fulde** tilgængelige navn, så et synligt ciffer derinde ville få hver eksisterende rækkelokator
til at holde op med at matche.

`TaskRow` skal tilføje `Shortcut` til sine `imports`.

**Trin 4: Kør Vitest + `scripts\build-web.ps1`.**

**Trin 5: Commit**

```bash
git add src/Todo.Web/src/app/tasks/
git commit -m '✨ Alt plus et ciffer vælger den n te række på listen'
```

---

## Opgave 7: Rejserne

**Filer:** Modificér `tests/Todo.E2E/KeyboardJourneyTests.cs`. Muligvis
`tests/Todo.E2E/Screens/TaskListScreen.cs` for en locator på rækkens nummer-badge.

Fem nye `[Fact]`, hver skal **ses fejle** ved at bryde det den beskytter:

1. **`Alt_3_selects_the_third_row`** — sået liste på 480 px, påstand på række **tre**. Tallet er
   assertionens tænder: auto-valget tager `[0]` side by side, så en påstand på række ét ville
   bestå med genvejen slået fra.
2. **`The_tenth_row_has_no_shortcut`** — elleve valgbare opgaver, og `aria-keyshortcuts` på række
   ti har `ToHaveCountAsync(0)`. Brydes guarden i direktivet, står der `Alt+`.
3. **`Alt_Shift_D_focuses_the_deadline_field`** — `FocusedTestIdAsync()` skal svare
   `deadline-input`.
4. **`Alt_Shift_L_focuses_the_delete_button_without_deleting`** — fokus på `delete-task`, **og**
   opgaven står stadig på listen. Den anden halvdel er den eneste der kan skelne `focus` fra
   `activate`.
5. **`Alt_Shift_N_opens_the_note_editor_and_puts_the_caret_in_it`** — `note-editor` er synlig
   **og** fokuseret.

Udvid desuden `Every_shortcut_letter_on_screen_is_its_own` med en søsterpåstand: samme opslag på en
**sået** liste med panelet åbent, hvor bogstaverne stadig skal være distinkte, og tallet skal være
9 + rækker + 7 (eller 8 med hvem-feltet). Uden den er guarden blind for hele det nye lag —
konstanten `BadgeCount = 9` er stadig rigtig for den tomme liste, se opgave 0.

**Husk:** `scripts\build-web.ps1` **før** E2E. Suiten bygger ikke Angular, så uden bygningen måler
Playwright den forrige udgave af frontenden, og intet ser forkert ud.

**Commit:** `🧪 Rejserne måler cifrene, feltlaget og at sletning kun får fokus`

---

## Opgave 8: Kontrast

**Filer:** Modificér `tests/Todo.E2E/ContrastTests.cs`.

`⇧D` og cifrene er nye tekstflader, og vagten kan ikke se en farve der aldrig blev renderet. Rejsen
skal **holde Alt nede, mens snapshottet tages** — `App.Page.Keyboard.DownAsync("Alt")` før
gennemgangen og `UpAsync` efter, i den teori der måler opgavelisten i begge temaer.

Ses fejle ved at male en badge `text-gray-400 dark:text-gray-600` (2,60:1 og 2,35:1) og bekræfte at
begge temaer melder fejl med badgens tekst i linjen.

**Commit:** `🧪 Kontrastvagten måler genvejsmærkaterne med Alt nede`

---

## Opgave 9: Målingerne kun brugeren kan lave, og dokumentationen

To ting i designet er skrevet ned som **umålte**, og de kan kun måles i Photino-vinduet:

1. Giver `Alt+Shift+D` `event.key === "D"`? Er svaret nej, skal opslaget bruge `event.code`.
2. Stjæler Windows' layoutskift på `Alt+Shift` kombinationen? Bemærk at en grøn måling er svag,
   hvis maskinen kun har ét tastaturlayout installeret — så siger den ingenting om en maskine der
   har to.

Kør `Todo.cmd`, prøv de otte bogstaver og de ni cifre, og skriv resultatet i
`docs/HANDOFF.md` under målingerne. Er nummer 1 negativ, er det en rettelse i opgave 4 og ikke et
nyt design.

**Filer:** Modificér `docs/HANDOFF.md`, `CLAUDE.md` og `README.md`.

`CLAUDE.md` skal have de nye linjer under "Konventioner", hvor bogstavlisten står i dag — at der nu
er **to lag**, at `alt`-laget stadig er last-writer-wins og globalt unikt, at feltlaget hører til
opgavelisten, og at badges vises på Alt alene med vilje. Og under "Testdisciplin": at
`BadgeCount = 9` gælder den **tomme** liste, og hvorfor det ikke er en svaghed.

`README.md` skal have genvejstabellen, så brugeren kan slå dem op uden at læse koden.

**Commit:** `📝 De to genvejslag står i konventionerne, og målingerne i HANDOFF`

---

## Til sidst

```bash
Check.cmd
```

Alle fire tal skal være **højere** end 174/316/59/281 og ingen lavere. Et lavere tal betyder en
tabt test, ikke en heldig oprydning. Opdatér "Testtal" i `CLAUDE.md` med de nye.
