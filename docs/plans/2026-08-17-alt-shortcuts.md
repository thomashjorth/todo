# Skive 8 — Alt-genvejssystemet

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Hold Alt for at se genvejene på knapperne, og tryk Alt+bogstav for at aktivere dem — Windows-konventionen, som designdokumentets afsnit 2 lover.

**Architecture:** En signal-baseret store ejer `altHeld` og et register over genveje. Et direktiv registrerer sit værtselement og sætter `aria-keyshortcuts`; mærkaten på skærmen tegnes af skabelonen, så direktivet ikke ændrer elementets tilgængelige navn. `app.ts` lytter på `document` gennem `host`-bindinger, så Angular rydder op selv.

**Tech Stack:** Angular 22 signals (`input()`, direktiv med `host`) · Transloco · Tailwind 4.3.3 · Vitest · Playwright 1.62.0

## Hvorfor den ligger for sig

Udskilt fra skive 7 den 2026-08-17. Skive 7 var en **audit** — målt, afgrænset, med en kontrastvagt der siger hvornår den er færdig. Det her er en **ny funktion** med egne designvalg: hvilke bogstaver, hvordan mærkaten ser ud, hvad der sker ved konflikt. Designdokumentet lagde dem sammen med begrundelsen *"fordi hver farve ellers skulle kontrasttjekkes to gange"* — men det argument gælder farver, ikke genveje.

**Placeret som skive 8 den 2026-08-17.** Den lå først uden nummer, netop for at blive placeret som en beslutning frem for at glide ind foran noget. Beslutningen blev truffet, da `long` som id blev udskudt og frigjorde nummeret — så **ingen skive er omnummereret**, i modsætning til hvad skive 6 udløste. `long`-planen ligger færdig i `docs/plans/2026-08-17-long-ids.md` og står nu under designdokumentets "Ønsket, men ikke placeret endnu".

## Forudsætning

**Skive 7 er færdig, og det er forudsætningen.** Mærkaterne er nye farver på skærmen, og `ContrastTests` fra skive 7 er det der fanger, hvis de ikke holder AA. Uden den vagt bygges genvejene uden noget net.

---

## Task 1: Bogstaverne og storen

**Files:**
- Create: `src/Todo.Web/src/app/shortcuts/shortcut-store.ts`
- Create: `src/Todo.Web/src/app/shortcuts/shortcut-store.spec.ts`

**Step 1: Bogstaverne**

Vælg udenom Chromes `Alt+D/E/F/Home` og piletasterne — de er frie i Photino-vinduet, men ikke under udvikling i en browser, og en genvej der kun virker halvdelen af tiden bliver fejlsøgt i den forkerte ende:

| Tast | Handling |
| --- | --- |
| `Alt+O` | Gå til Opgaver |
| `Alt+I` | Gå til Import |
| `Alt+S` | Gå til Indstillinger |
| `Alt+N` | Fokusér feltet til en ny opgave |
| `Alt+V` | Slå "Vis færdige" til og fra |
| `Alt+M` | Slå "Vis måske" til og fra |

**Step 2: Storen**

`shortcut-store.ts` — signal-baseret, som resten af appen. Den ejer `altHeld` og registret:

```ts
import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ShortcutStore {
  readonly altHeld = signal(false);

  private readonly targets = new Map<string, () => void>();

  register(key: string, activate: () => void): void {
    this.targets.set(key.toLowerCase(), activate);
  }

  unregister(key: string): void {
    this.targets.delete(key.toLowerCase());
  }

  /** True when the key was handled, so the caller knows whether to swallow the event. */
  activate(key: string): boolean {
    const target = this.targets.get(key.toLowerCase());
    target?.();

    return target !== undefined;
  }

  setAltHeld(held: boolean): void {
    this.altHeld.set(held);
  }
}
```

**Step 3: Vitest på storen**

```ts
import { ShortcutStore } from './shortcut-store';

describe('ShortcutStore', () => {
  it('should activate a registered target and report that it handled the key', () => {
    const store = new ShortcutStore();
    let activated = 0;
    store.register('n', () => activated++);

    expect(store.activate('n')).toBe(true);
    expect(activated).toBe(1);
  });

  it('should report an unregistered key as unhandled so the event is not swallowed', () => {
    const store = new ShortcutStore();

    expect(store.activate('q')).toBe(false);
  });

  it('should match the key case-insensitively, because Alt+Shift reports an upper-case key', () => {
    const store = new ShortcutStore();
    let activated = 0;
    store.register('n', () => activated++);

    expect(store.activate('N')).toBe(true);
    expect(activated).toBe(1);
  });

  it('should stop activating a target that has been unregistered', () => {
    const store = new ShortcutStore();
    store.register('n', () => {
      throw new Error('should not run');
    });
    store.unregister('n');

    expect(store.activate('n')).toBe(false);
  });
});
```

**Step 4: Commit**

```
git add src/Todo.Web/src/app/shortcuts
git commit -m "⌨️ Store der holder Alt-tilstanden og registret"
```

---

## Task 2: Direktivet og Alt-lytteren

**Files:**
- Create: `src/Todo.Web/src/app/shortcuts/shortcut.ts`
- Modify: `src/Todo.Web/src/app/app.ts`

**Step 1: Direktivet**

`shortcut.ts` — registrerer værtselementet og viser mærkaten. Bemærk `host`-bindingerne frem for en skabelon: direktivet må ikke ændre elementets indhold, fordi rækkeknappens tilgængelige navn matches i sin helhed af E2E-testene.

```ts
import { Directive, ElementRef, inject, input, OnDestroy, OnInit } from '@angular/core';
import { ShortcutStore } from './shortcut-store';

@Directive({
  selector: '[appShortcut]',
  host: {
    '[attr.aria-keyshortcuts]': '"Alt+" + appShortcut().toUpperCase()',
  },
})
export class Shortcut implements OnInit, OnDestroy {
  readonly appShortcut = input.required<string>();

  private readonly store = inject(ShortcutStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngOnInit(): void {
    this.store.register(this.appShortcut(), () => this.host.nativeElement.focus());
  }

  ngOnDestroy(): void {
    this.store.unregister(this.appShortcut());
  }
}
```

`aria-keyshortcuts` er den standardiserede måde at fortælle en skærmlæser om genvejen; mærkaten på skærmen er kun for øjet.

**Step 2: Lyt efter Alt**

I `app.ts`, med `host`-bindinger frem for `window.addEventListener`, så Angular rydder op selv:

```ts
  host: {
    '(document:keydown)': 'onKeyDown($event)',
    '(document:keyup)': 'onKeyUp($event)',
    '(window:blur)': 'shortcuts.setAltHeld(false)',
  },
```

```ts
  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(true);
      return;
    }

    // Alt+letter, and only when Alt is the sole modifier: Ctrl+Alt is AltGr on a Danish
    // keyboard, and swallowing it would break typing @ and £.
    if (event.altKey && !event.ctrlKey && !event.metaKey && this.shortcuts.activate(event.key)) {
      event.preventDefault();
    }
  }

  protected onKeyUp(event: KeyboardEvent): void {
    if (event.key === 'Alt') {
      this.shortcuts.setAltHeld(false);
    }
  }
```

**`Ctrl+Alt` er AltGr på et dansk tastatur.** Sluger man den, kan brugeren ikke skrive `@`, `£` eller `$`. Det er den slags fejl der bliver rapporteret som "appen æder mine tegn" tre uger senere.

**`window:blur` nulstiller `altHeld`.** Alt+Tab væk fra vinduet giver et keydown uden et keyup, og mærkaterne ville stå tilbage for evigt.

**Step 3: Byg**

Direktivet er endnu ikke sat på noget, så skærmen ser uændret ud. Bygningen skal være grøn:

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

**Step 4: Commit**

```
git add src/Todo.Web/src/app
git commit -m "⌨️ Direktiv og Alt-lytter, endnu uden mærkater"
```

---

## Task 3: Mærkaterne

**Files:**
- Modify: `src/Todo.Web/src/app/app.html`
- Modify: `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/i18n/da.json`, `en.json`

**Step 1: Mærkaterne**

Sæt direktivet på de seks elementer, og vis mærkaten når `shortcuts.altHeld()`. På navigationslinkene i `app.html`:

```html
    <a
      data-testid="nav-tasks"
      appShortcut="o"
      class="text-sm text-gray-600 dark:text-gray-400"
      ...
      >{{ 'nav.tasks' | transloco
      }}@if (shortcuts.altHeld()) {
        <span
          class="ml-1 rounded border border-gray-500 px-1 text-xs text-gray-700 dark:border-gray-400 dark:text-gray-300"
          >O</span
        >
      }</a
    >
```

**Mærkaten skal ligge inde i linket, men uden for det tilgængelige navn er ikke muligt her** — teksten indgår i navnet. Tjek at `App.Heading`, `nav-tasks` og de øvrige locatorer stadig matcher; gør de ikke, så brug `aria-hidden="true"` på mærkatens `<span>`, så den forsvinder fra navnet men bliver på skærmen.

Farverne er målt: `text-gray-700` 10,31:1 lyst, `dark:text-gray-300` 12,06:1 mørkt, rammerne 4,84:1 og 6,82:1.

**Step 2: Oversættelsesnøgler**

Genvejsmærkaterne er enkeltbogstaver og hører ikke i oversættelsesfilerne. Men skal der en forklarende linje til — fx *"Hold Alt for at se genvejene"* — skal den have en nøgle i **begge** filer, ellers fejler paritetstesten. Tilføj `shortcuts.hint` til `da.json` og `en.json` hvis du tilføjer teksten; gør du ikke, så tilføj ingen nøgle.

**Step 3: Kontrastvagten fra skive 7**

Mærkaterne er nye farver på skærmen, og de er dækket:

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~ContrastTests"
```

Forventet: grøn i begge farvetemaer. Fejler den, så er mærkatens farver forkerte — hæv dem frem for at slukke vagten.

**Step 4: Commit**

```
git add src/Todo.Web/src
git commit -m "⌨️ Vis genvejsmærkaterne mens Alt holdes nede"
```

---

## Task 4: E2E og vagten

**Files:**
- Modify: `tests/Todo.E2E/KeyboardJourneyTests.cs`
- Modify: `CLAUDE.md`

**Step 1: E2E på genvejene**

Tilføj til `KeyboardJourneyTests`:

```csharp
    [Fact]
    public async Task Alt_reveals_the_shortcuts_and_Alt_N_focuses_the_new_task_field()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });

        await App.Page.Keyboard.DownAsync("Alt");
        await Assertions.Expect(App.Page.GetByTestId("nav-tasks")).ToContainTextAsync("O");
        await App.Page.Keyboard.UpAsync("Alt");

        await App.Page.Keyboard.PressAsync("Alt+n");

        Assert.Equal("new-task-input", await App.Page.EvaluateAsync<string>(
            "() => document.activeElement?.dataset.testid ?? 'none'"));
    }
```

**Step 2: Se vagten fejle**

Fjern `appShortcut="n"` fra feltet, kør testen, og bekræft at den fejler med `none` frem for `new-task-input`. Sæt det tilbage.

**Step 3: Kør alt**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
npm.cmd run test --prefix src\Todo.Web -- --watch=false
dotnet test Todo.sln
```

Kontrastvagten skal stadig være grøn — mærkaterne er nye farver på skærmen, og de er dækket.

**Step 4: Commit**

```
git add tests/Todo.E2E CLAUDE.md
git commit -m "⌨️ E2E på Alt-genvejene og testtallene"
```

---

## Færdig når

- Alt viser mærkaterne på de seks elementer, og Alt slippes igen uden at de bliver hængende.
- De seks genveje aktiverer det de peger på.
- **`Ctrl+Alt` gør ingenting** — det er AltGr på et dansk tastatur, og `@`, `£` og `$` skal stadig kunne skrives.
- Alt+Tab væk fra vinduet efterlader ikke mærkaterne stående.
- **Vagten er set fejle:** en fjernet `appShortcut` giver `none` frem for `new-task-input`, med fejlteksten i rapporten.
- `ContrastTests` fra skive 7 er stadig grøn i begge farvetemaer — mærkaterne er nye farver.
- Hvert tilføjet testtal er skrevet ned i `CLAUDE.md`, og intet gammelt tal er faldet.

## Dokumentation, når den er kørt

Marker skive 8 **Færdig.** i designdokumentets afsnit 9 — den er allerede placeret der, så der skal ikke flyttes noget — og tilføj den til `HANDOFF.md`s Færdigt-tabel. Flyt den ud af HANDOFF's "I gang"-afsnit. Bogstavvalget — og *hvorfor* `Alt+D/E/F/Home` er udeladt — hører i `CLAUDE.md` under Konventioner, ellers bliver det opdaget igen næste gang nogen vil tilføje en genvej.
