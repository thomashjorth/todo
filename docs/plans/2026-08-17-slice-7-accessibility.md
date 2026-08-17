# Skive 7 — Tilgængelighed, tastatur og dark mode

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Appen kan bruges i mørkt systemtema uden at tekst forsvinder, hver farve holder WCAG AA, fokus er synligt, og hver handling kan nås med tastaturet.

**Architecture:** Vagten skrives **først**. En Playwright-test går hele DOM'en igennem i begge farvetemaer, læser `getComputedStyle` og regner kontrastforhold på de farver browseren faktisk maler — den løser den parring mellem tekst og baggrund, som statisk analyse ikke kan. Testen fejler på dagens kode, og hver efterfølgende opgave lukker en del af listen. Derefter fokus, tastatur og Alt-genvejene.

**Tech Stack:** Tailwind 4.3.3 (paletten er oklch) · Angular 22 signals · Playwright 1.62.0 · xunit.v3 · Transloco

## Hvorfor

Skiven er **udskudt to gange** — først af markdown, så af GTD-tilstandene. Designdokumentet skriver selv: *"Sker det igen, er det værd at spørge hvorfor."* Det er tredje gang den står forrest.

Og der er en konkret fejl at rette, ikke kun en oprydning: `<body>` sætter hverken baggrund eller tekstfarve. Under mørkt systemtema arver den derfor browserens hvide baggrund, mens komponenternes `dark:`-farver slår til. **`dark:text-gray-100` på hvid giver 1,10:1.** Det er ikke lav kontrast, det er usynlig tekst.

## Hvad målingen viste

Målt 2026-08-17 ved at konvertere Tailwind 4's oklch-palette til den sRGB browseren maler og regne WCAG-relativ luminans. **Ikke** ved at læse tal fra tidligere dokumenter — de var forkerte, se nedenfor.

### Den akutte fejl: dark:-farverne lander på hvid

| Farve | På hvid (i dag) | På `gray-900` (efter rettelsen) |
| --- | --- | --- |
| `text-gray-100` | **1,10:1** | 16,13:1 |
| `text-gray-300` | **1,47:1** | 12,06:1 |
| `text-gray-400` | **2,60:1** | 6,82:1 |
| `text-red-300` | **1,92:1** | 9,24:1 |
| `text-blue-300` | **1,81:1** | 9,80:1 |
| `text-amber-300` | **1,45:1** | 12,27:1 |

Alle syv `dark:`-tekstfarver i brug bliver grønne af den ene rettelse: en baggrund på `<body>`. Ingen af dem skal ændres.

### Kontrasten i lyst tema

| Farve | På hvid | På `gray-50` (detaljepanelet) | Dom |
| --- | --- | --- | --- |
| `text-gray-900` | 17,75:1 | 17,00:1 | OK |
| `text-gray-700` | 10,31:1 | 9,87:1 | OK |
| `text-gray-600` | 7,56:1 | 7,24:1 | OK |
| `text-gray-500` | 4,84:1 | 4,63:1 | **OK** |
| `text-gray-400` | **2,60:1** | **2,49:1** | **FEJLER** |
| `text-red-700` | 6,42:1 | 6,15:1 | OK |
| `text-red-600` | 4,76:1 | 4,56:1 | OK (knapt) |
| `text-blue-700` | 6,82:1 | 6,54:1 | OK |
| `text-amber-700` | 5,05:1 | 4,84:1 | OK |

**To rettelser af dokumentationens tal.** `HANDOFF.md` og designdokumentet siger begge, at `text-gray-400` er *~2,9:1*. Den er **2,60:1**. Tallet 2,85 er `#9ca3af` — Tailwind **3**'s hex-palette. Tailwind 4 definerer paletten i oklch, og `gray-400` er en anden farve. Og påstanden om at `text-gray-500` skulle være et problem holder ikke: den klarer 4,84:1 og bruges 17 steder, som alle kan blive stående.

### Rammer og kanter

| Farve | På | Forhold | Dom |
| --- | --- | --- | --- |
| `border-gray-300` (felter) | hvid | **1,47:1** | FEJLER 3:1 |
| `border-gray-500` | hvid | 4,84:1 | OK |
| `dark:border-gray-600` (felter) | `gray-900` | **2,35:1** | FEJLER 3:1 |
| `dark:border-gray-500` | `gray-900` | 3,67:1 | OK |
| `border-red-200` (overskredet) | hvid | **1,45:1** | FEJLER 3:1 |
| `border-red-500` | hvid | 3,82:1 | OK |
| `border-amber-300` (venter på) | hvid | **1,45:1** | FEJLER 3:1 |
| `border-amber-600` | hvid | 3,19:1 | OK |

### En asymmetri der er nem at gøre forkert

`text-gray-400` skal op til `gray-500` i lyst tema. Men **`dark:text-gray-500` fejler** (3,67:1 på `gray-900`), hvor `dark:text-gray-400` klarer 6,82:1. Parret er altså `text-gray-500 dark:text-gray-400` — **ikke** samme trin på begge sider. Bytter man mekanisk 400→500 overalt, ødelægger man den mørke side.

### 28 klasselister mangler en `dark:`-modpart

Fordelt: `app.html` 2, `task-list.html` 8, `task-row.html` 18. `settings.html` og `retro-import.html` har **nul** — de blev gjort færdige dengang. Den fulde liste står i Task 4.

### Fokus og tastatur

- **Tre `focus:outline-none`** — `task-list.html:6`, `settings.html:14`, `settings.html:61`. Alle tre fjerner browserens fokusring og sætter kun en 1px kantfarve i stedet (`gray-300` → `gray-500`). Det er den svageste form for fokusmarkering, og på de øvrige kontroller findes den slet ikke som noget bevidst.
- **Noten er klikbar på en `<div>` og en `<p>`** (`task-row.html:99` og `:106`). Det er ikke et tastaturbrud: knappen `data-testid="note-edit"` giver den samme handling, og links inde i noten er rigtige `<a href>`, som Enter aktiverer. Men affordancen findes kun for musen — værd at vide, ikke værd at lave om.
- **Alt-genvejssystemet findes ikke.** Designdokumentets afsnit 2 lover *"Hold Alt for at vise genvejene på knapperne"*. Der er ingen kode.

## Beslutninger

| Emne | Valg |
| --- | --- |
| Vagten | Playwright + `getComputedStyle`, ikke statisk klasseanalyse. Browseren har allerede parret tekst med baggrund. |
| Vagten kører | To testtilfælde, `ColorScheme.Light` og `ColorScheme.Dark`, hver gennem alle fire skærme plus et udfoldet detaljepanel. |
| Mørk baggrund | `dark:bg-gray-900` på `<body>`. Alle syv `dark:`-tekstfarver i brug klarer 4,5:1 på den. |
| Hvor baggrunden sættes | Klasser på `<body>` i `src/index.html`. **Ikke** en CSS-regel — konventionen tillader kun `@plugin`-linjen i `styles.css`. |
| Dæmpet tekst | `text-gray-500 dark:text-gray-400`. Se asymmetrien ovenfor. |
| Feltrammer | `border-gray-500 dark:border-gray-500`. |
| Sektionsrammer | `border-red-500` og `border-amber-600` — se afvejningen nedenfor. |
| Fokus | `focus-visible:outline-2 focus-visible:outline-offset-2` med `outline-blue-600 dark:outline-blue-400`. `focus-visible`, ikke `focus`, så mus­klik ikke efterlader en ring. |
| `prefers-color-scheme` | Kræver ingen opsætning: Tailwind 4's `dark:` er som standard `@media (prefers-color-scheme: dark)`. |

**Sektionsrammerne er en afvejning, ikke en regel.** WCAG 1.4.11 kræver 3:1 af *meningsbærende* grafik. Den røde streg ved "Overskredet" er ikke det eneste signal — overskriften siger "Overskredet" og er `text-red-600`. Man kan derfor argumentere for, at stregen er dekoration og må blive 1,45:1. Vi hæver den alligevel, fordi den er billig at hæve og fordi en streg der ikke kan ses, heller ikke pynter. **Vagten håndhæver ikke rammer** — kun tekst — så det her er et valg planen tager, ikke noget en test tvinger igennem.

## Fælder i denne skive

- **`text-gray-400` findes fire steder med to forskellige betydninger.** På health-linjen og på et pladsholder-felt er den "sekundær". På den færdige opgaves titel og på en afkrydset underopgave er den "gennemstreget og dæmpet". Begge skal op til `gray-500`; intentionen holder, kontrasten kommer med.
- **`placeholder-gray-400` fanges ikke af en almindelig DOM-gennemgang.** Pladsholderfarven ligger på `::placeholder`, ikke på elementet. Vagten skal spørge `getComputedStyle(el, '::placeholder')` særskilt, ellers er den blind for et felt brugeren ser hver gang appen åbnes.
- **`Alt+D`, `Alt+E`, `Alt+F`, `Alt+Home` og piletasterne er Chromes** under udvikling, men frie i Photino-vinduet. Vælg bogstaver udenom, ellers virker genvejene i appen og ikke i browseren, og fejlen ligner en fejl i koden.
- **En kontrasttest der går gennem `body *` rammer også skjult tekst.** Filtrér på `display`, `visibility`, `opacity` og tom tekst, ellers fejler den på noget brugeren ikke kan se — og så bliver den slukket i stedet for læst.
- **Ret ikke `dark:text-gray-400` til 500.** Se asymmetrien.
- **Skiven flytter ingen markup og ingen struktur.** Kun farver, fokus og nye genveje. Ændrer et `<span>` plads, brydes `TaskListScreen.RowTitled`, som matcher rækkeknappens fulde tilgængelige navn.

## Bevidst uden for skive 7

Projekter og kontekster, revisionsloggen, og "Sådan er den tænkt"-siden.

**Og en anbefaling om at dele skiven:** Task 1–6 er en audit — målt, afgrænset, med en vagt der siger hvornår den er færdig. Task 7 (Alt-genvejssystemet) er en **ny funktion** med egne designvalg: hvilke bogstaver, hvordan mærkaterne ser ud, hvad der sker ved konflikt. Designdokumentet lagde dem sammen med begrundelsen *"fordi hver farve ellers skulle kontrasttjekkes to gange"* — men det argument gælder farver, ikke genveje. **Overvej at lande Task 1–6 og 8 som skive 7, og gøre genvejene til skive 8.** Planen skriver dem samlet, fordi det er sådan skiven er defineret; delingen er dit kald.

---

## Task 1: Vagten, der skal fejle først

**Files:**
- Modify: `tests/Todo.E2E/TodoApp.cs`
- Create: `tests/Todo.E2E/ContrastTests.cs`

**Step 1: Lad appen åbne i et farvetema**

`TodoApp.OpenAsync` laver siden. Den skal kunne åbne den i mørkt tema. Tilføj parameteren, og lad den gå videre til `NewPageAsync`:

```csharp
    public static async Task<TodoApp> OpenAsync(
        IBrowser browser, RunningHost host, ViewportSize? viewport = null,
        ColorScheme? colorScheme = null)
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = viewport,
            ColorScheme = colorScheme,
        });
        await page.GotoAsync(host.BaseUrl);

        var app = new TodoApp(page);
        await app.Tasks.WaitUntilShownAsync();

        return app;
    }
```

Og i `BrowserTest`, så en test kan bede om det:

```csharp
    protected async Task OpenAppAsync(
        ViewportSize? viewport = null, ColorScheme? colorScheme = null)
        => App = await TodoApp.OpenAsync(fixture.Browser, _host, viewport, colorScheme);
```

**Step 2: Kontrastmålingen, som en metode på `TodoApp`**

Den regner i browseren, hvor de faktiske farver er. Tilføj til `TodoApp.cs`:

```csharp
    /// <summary>
    /// Every element that renders its own text, measured against the background that actually
    /// sits behind it. The browser has already resolved which background that is, which is why
    /// this runs in the page rather than over the class attributes.
    /// </summary>
    public Task<string[]> ContrastFailuresAsync() => Page.EvaluateAsync<string[]>(
        """
        () => {
          const channels = (c) => (c.match(/[\d.]+/g) ?? []).map(Number);

          const luminance = ([r, g, b]) => {
            const lin = [r, g, b].map((v) => {
              v /= 255;
              return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
            });
            return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2];
          };

          const ratio = (fg, bg) => {
            const [a, b] = [luminance(fg), luminance(bg)];
            const [hi, lo] = a > b ? [a, b] : [b, a];
            return (hi + 0.05) / (lo + 0.05);
          };

          // Walk up until something is actually painted: a transparent background means the
          // ancestor's colour is what the user sees behind the text.
          const backgroundOf = (el) => {
            for (let n = el; n; n = n.parentElement) {
              const c = channels(getComputedStyle(n).backgroundColor);
              if (c.length >= 3 && (c[3] === undefined || c[3] > 0)) return c.slice(0, 3);
            }
            return [255, 255, 255];
          };

          const hidden = (s) =>
            s.display === 'none' || s.visibility === 'hidden' || Number(s.opacity) === 0;

          const label = (el) =>
            el.tagName.toLowerCase() + (el.dataset.testid ? `[${el.dataset.testid}]` : '');

          const failures = [];

          const check = (el, style, fg, what, sample) => {
            // WCAG large text: 24px, or 18.66px when bold.
            const size = parseFloat(style.fontSize);
            const large = size >= 24 || (Number(style.fontWeight) >= 700 && size >= 18.66);
            const needed = large ? 3 : 4.5;
            const r = ratio(fg, backgroundOf(el));

            if (r < needed) {
              failures.push(
                `${label(el)} ${what} "${sample.slice(0, 40)}" ${r.toFixed(2)}:1 needs ${needed}`);
            }
          };

          for (const el of document.querySelectorAll('body *')) {
            const style = getComputedStyle(el);
            if (hidden(style)) continue;

            // Only the element's own text: a parent would otherwise be blamed for its child's.
            const own = [...el.childNodes]
              .filter((n) => n.nodeType === Node.TEXT_NODE)
              .map((n) => n.textContent.trim())
              .join(' ')
              .trim();

            if (own) check(el, style, channels(style.color).slice(0, 3), 'text', own);

            // Placeholder colour lives on ::placeholder, so the walk above cannot see it —
            // and it is text the user reads every time the app opens.
            if (el instanceof HTMLInputElement && el.placeholder) {
              const ph = getComputedStyle(el, '::placeholder');
              check(el, ph, channels(ph.color).slice(0, 3), 'placeholder', el.placeholder);
            }
          }

          return failures;
        }
        """);
```

**Step 3: Testen**

`tests/Todo.E2E/ContrastTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// WCAG AA over every screen, in both colour schemes. The measurement runs in the browser
/// because only it knows which background a given piece of text ended up on.
/// </summary>
public class ContrastTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Every_screen_meets_WCAG_AA(ColorScheme scheme)
    {
        // One task per state, so no section of the list goes unmeasured.
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled("Betal regningen").Overdue().Build(),
            new TaskItemBuilder(Clock).Titled("Send referatet").DueToday().Build(),
            new TaskItemBuilder(Clock).Titled("Svar revisoren")
                .WaitingFor("Mette", Clock.UtcNow.AddDays(-12)).Build(),
            new TaskItemBuilder(Clock).Titled("Læs om typografi").Someday().Build(),
            new TaskItemBuilder(Clock).Titled("Ryd skrivebordet").Done().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1400 }, scheme);

        var failures = new List<string>();
        var tasks = App.Tasks;

        // The two switches reveal the completed and someday sections.
        await tasks.ShowCompleted.CheckAsync();
        await tasks.ShowSomeday.CheckAsync();
        failures.AddRange(await App.ContrastFailuresAsync());

        // The detail panel is the largest single block of colour, and it only exists expanded.
        await tasks.RowTitled("Send referatet").ClickAsync();
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();
        failures.AddRange(await App.ContrastFailuresAsync());

        await App.GoToImport();
        failures.AddRange(await App.ContrastFailuresAsync());

        await App.GoToSettings();
        failures.AddRange(await App.ContrastFailuresAsync());

        Assert.Empty(failures.Distinct().Order());
    }
}
```

**Step 4: Kør den og se den fejle**

```
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~ContrastTests"
```

Forventet: **begge testtilfælde fejler.** Det mørke skal fejle groft — `dark:text-gray-100` på hvid er 1,10:1 — og det lyse skal fejle på `text-gray-400` og pladsholderen omkring 2,5–2,6:1.

**Rapportér den fulde liste af fejl.** Den er arbejdslisten for Task 2–5, og den er det eneste sted, vi får at vide om målingen ovenfor har overset noget.

Hvis det **lyse** tilfælde består, er vagten i stykker — `text-gray-400` findes fire steder og skal fanges. Undersøg filtrene i Step 2 frem for at gå videre.

**Step 5: Commit**

Vagten committes rød. Det er med vilje: den næste opgave skal kunne vise, at den blev grøn.

```
git add tests/Todo.E2E
git commit -m "🧪 Kontrastvagt over alle skærme i begge farvetemaer"
```

---

## Task 2: `<body>` får farver

**Files:**
- Modify: `src/Todo.Web/src/index.html`

**Step 1: Klasserne på `<body>`**

Konventionen tillader ingen CSS-regler, så baggrunden sættes med utility-klasser:

```html
<body class="bg-white text-gray-900 dark:bg-gray-900 dark:text-gray-100">
  <app-root></app-root>
</body>
```

`bg-white` og `text-gray-900` skrives eksplicit frem for at læne sig på browserens standard, så de to temaer er defineret samme sted og ikke kan komme ud af trit.

**Step 2: Byg og kør vagten**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~ContrastTests"
```

Forventet: **det mørke tilfælde falder fra mange fejl til få.** De der bliver tilbage er dem, hvor et element mangler en `dark:`-modpart og derfor står med sin lyse farve på mørk baggrund — altså Task 4's liste. Det lyse tilfælde skal være uændret.

Rapportér antal fejl før og efter. Faldt tallet ikke markant i mørkt tema, så bliver `index.html` ikke scannet af Tailwind, og klasserne findes ikke i CSS'en — kontrollér det før du går videre.

**Step 3: Commit**

```
git add src/Todo.Web/src/index.html
git commit -m "🎨 Giv skallen en baggrund i begge temaer"
```

---

## Task 3: Kontrastfejlene i lyst tema

**Files:**
- Modify: `src/Todo.Web/src/app/app.html`
- Modify: `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/tasks/task-row.html`

**Step 1: `text-gray-400` fire steder → `text-gray-500 dark:text-gray-400`**

`gray-400` er 2,60:1 på hvid. `gray-500` er 4,84:1. **På den mørke side skal det blive ved `gray-400`** — `dark:text-gray-500` er 3,67:1 og fejler.

- `app.html:32` — health-linjen: `class="mt-8 text-xs text-gray-400"` → `class="mt-8 text-xs text-gray-500 dark:text-gray-400"`
- `task-list.html:6` — pladsholderen: `placeholder-gray-400` → `placeholder-gray-500 dark:placeholder-gray-400`
- `task-list.html:109` — den færdige opgaves titel: `text-gray-400 line-through` → `text-gray-500 line-through dark:text-gray-400`
- `task-row.html:168` — den afkrydsede underopgave: `text-gray-400 line-through` → `text-gray-500 line-through dark:text-gray-400`

De to sidste er "dæmpet og gennemstreget". Intentionen holder — `gray-500` er stadig tydeligt lysere end `gray-900` ved siden af.

**Step 2: Feltrammerne → `border-gray-500 dark:border-gray-500`**

`border-gray-300` er 1,47:1 og markerer kanten af et indtastningsfelt, altså en UI-komponent der skal holde 3:1. `dark:border-gray-600` er 2,35:1 og fejler også. Begge bliver `gray-500` (4,84:1 lyst, 3,67:1 mørkt).

Rør **kun** rammer på `<input>`, `<textarea>` og `<select>`. Følgende steder: `task-list.html:6`, `task-row.html:57`, `:68`, `:93`, `:128`, `:187`, samt `settings.html:14` og `:61`.

**Lad `border-gray-200`, `divide-gray-100` og `divide-y` være.** De adskiller rækker og sektioner og bærer ingen betydning; 1,24:1 er tilsigtet diskretion, og vagten håndhæver dem ikke.

**Step 3: Sektionsrammerne**

Se afvejningen under Beslutninger — det er et valg, ikke et krav:

- `task-list.html:38` — `border-red-200` → `border-red-500` (1,45:1 → 3,82:1), og `border-gray-200` i samme udtryk bliver stående.
- `task-list.html:64` — `border-amber-300` → `border-amber-600` (1,45:1 → 3,19:1). `dark:border-amber-600` er 5,56:1 og bliver stående.

**Step 4: Byg og kør vagten**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~ContrastTests"
```

Forventet: **det lyse tilfælde er nu grønt.** Det mørke har stadig Task 4's fejl.

Er det lyse stadig rødt, så læs fejlteksten: den nævner elementet og forholdet, og der er et sted mere med `gray-400` end de fire ovenfor.

**Step 5: Commit**

```
git add src/Todo.Web/src/app
git commit -m "🎨 Hæv de fire farver der ikke holdt AA i lyst tema"
```

---

## Task 4: De 28 manglende `dark:`-modparter

Konventionen siger, at hver `bg-*`/`text-*`/`border-*` skal have en `dark:`-modpart. 28 klasselister mangler en. Task 2 og 3 har taget nogle af dem; resten står her.

**Files:**
- Modify: `src/Todo.Web/src/app/app.html`
- Modify: `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/tasks/task-row.html`

**Step 1: Modparterne**

Standardparrene, alle målt til at holde 4,5:1 på `gray-900`:

| Lys | Mørk modpart | Forhold mørkt |
| --- | --- | --- |
| `text-gray-900` | `dark:text-gray-100` | 16,13:1 |
| `text-gray-700` | `dark:text-gray-300` | 12,06:1 |
| `text-gray-600` | `dark:text-gray-400` | 6,82:1 |
| `text-gray-500` | `dark:text-gray-400` | 6,82:1 |
| `text-red-600` | `dark:text-red-400` | 6,14:1 |
| `text-red-700` | `dark:text-red-300` | 9,24:1 |
| `bg-gray-50` | `dark:bg-gray-800` | — |
| `border-gray-200` | `dark:border-gray-700` | dekoration |

Stederne, fra målingen:

**`app.html`** — `:32` er taget i Task 3. `:36` health-fejllinjen: `text-red-600` → `text-red-600 dark:text-red-400`.

**`task-list.html`** — `:6` (taget i Task 3 og 4's rammer), `:38` og `:93` rammer, `:42` `text-red-600 text-gray-500` i sektionsoverskriften, `:63` `text-gray-500` i den tomme liste, `:94` `text-gray-500` i "Færdige"-overskriften, `:109` (taget i Task 3).

**`task-row.html`** — 18 steder. `:13` `text-gray-900` (rækkens titel), `:22`, `:27`, `:53`, `:64`, `:76`, `:108`, `:125`, `:155` alle `text-gray-500` på etiketter, `:51` `bg-gray-50` (detaljepanelet) → `dark:bg-gray-800`, `:168` (taget i Task 3), `:175` og `:194` `text-red-600` på sletteknapperne, samt rammerne `:57`, `:68`, `:93`, `:128`, `:187` (taget i Task 3).

**Arbejd fra vagtens fejlliste, ikke fra linjenumrene her.** Task 2 og 3 har flyttet linjer. Fejllisten nævner `data-testid` hvor det findes, og det er en stabilere adresse end et linjenummer.

**Step 2: Byg og kør vagten**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~ContrastTests"
```

Forventet: **begge testtilfælde grønne.** Det er første gang, de er det.

**Step 3: Se vagten fejle igen**

En vagt, ingen har set fejle på det den nu beskytter, beviser ingenting. Sæt **én** modpart tilbage — fjern `dark:text-gray-100` fra rækkens titel i `task-row.html` — byg, og kør vagten.

Forventet: **det mørke tilfælde fejler**, med rækkens titel nævnt og et forhold omkring 1,1:1. Rapportér fejlteksten, og sæt så modparten tilbage.

**Step 4: Kør hele suiten**

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false
dotnet test Todo.sln
```

Forventet: 133 Vitest, 33 Core, 109 Api, og E2E oppe på 9 (7 + de to nye). Farveændringer må ikke røre et testtal — gør de det, har en test været afhængig af en farve.

**Step 5: Commit**

```
git add src/Todo.Web/src/app
git commit -m "🌙 Giv hver farve en modpart i mørkt tema"
```

---

## Task 5: Synligt fokus

**Files:**
- Modify: `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/settings/settings.html`
- Modify: `tests/Todo.E2E/TodoApp.cs`
- Create: `tests/Todo.E2E/FocusTests.cs`

**Step 1: Vagten først**

En fokusring der forsvinder, er svær at se i en diff og nem at se i en test. Tilføj til `TodoApp.cs`:

```csharp
    /// <summary>
    /// The outline the browser paints on the focused element. `outline: none` with only a
    /// border change in its place is what this exists to catch.
    /// </summary>
    public Task<string> FocusOutlineAsync() => Page.EvaluateAsync<string>(
        """
        () => {
          const el = document.activeElement;
          if (!el || el === document.body) return 'nothing focused';

          const s = getComputedStyle(el);
          return `${s.outlineStyle} ${s.outlineWidth}`;
        }
        """);
```

`tests/Todo.E2E/FocusTests.cs`:

```csharp
using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// Keyboard focus has to be visible. Three inputs used to set `outline: none` and put only a
/// 1px border colour change in its place, which is the weakest marking there is.
/// </summary>
public class FocusTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task Tabbing_to_the_new_task_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Tasks.NewTaskInput.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.DoesNotContain("none", outline);
        Assert.DoesNotContain("0px", outline);
    }

    [Fact]
    public async Task Tabbing_to_a_settings_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var settings = await App.GoToSettings();

        await settings.Language.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.DoesNotContain("none", outline);
        Assert.DoesNotContain("0px", outline);
    }
}
```

`SettingsScreen.Language` er `GetByTestId("language-select")` og findes allerede — bemærk at locatoren heder `Language`, ikke `LanguageSelect`. Skal du røre et felt der ikke har en locator, så tilføj den til skærmklassen frem for at skrive en rå CSS-vælger i testen.

**Step 2: Kør og se den fejle**

```
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~FocusTests"
```

Forventet: **begge fejler**, fordi `outlineStyle` er `none`. `FocusAsync()` regnes som programmatisk fokus, så `:focus-visible` kan være uafklaret på et `<select>` — fejler testen af den grund frem for på `outline: none`, så brug `App.Page.Keyboard.PressAsync("Tab")` frem for `FocusAsync()` og tab dig frem til feltet. Rapportér hvilken vej du valgte.

**Step 3: Ringen**

Erstat `focus:border-gray-500 focus:outline-none` med en rigtig ring, tre steder — `task-list.html:6`, `settings.html:14`, `settings.html:61`:

```
focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:focus-visible:outline-blue-400
```

`blue-600` er 5,26:1 på hvid, `blue-400` 6,73:1 på `gray-900` — begge over de 3:1 en fokusmarkering skal holde mod sin baggrund.

`focus-visible` og ikke `focus`: et museklik skal ikke efterlade en ring. **Fjern ikke** `focus:border-gray-500` — en kantfarve oveni er fin; det var kun `outline-none` der var problemet.

**Step 4: Kør vagten og kontrastvagten**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj
```

Forventet: alle grønne, E2E på 11.

**Step 5: Commit**

```
git add src/Todo.Web/src/app tests/Todo.E2E
git commit -m "♿ Giv fokus en synlig ring i stedet for outline-none"
```

---

## Task 6: Tastaturgennemgangen

**Files:**
- Create: `tests/Todo.E2E/KeyboardJourneyTests.cs`

Ingen produktionskode forventes her: knapper er `<button>`, felter er `<input>`, og noten har sin egen redigeringsknap. Opgaven er at **bevise** det, og at finde det sted hvor det ikke passer.

**Step 1: Rejsen**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// Every action has to be reachable without a mouse. This walks one task from created to
/// expanded to deleted using only the keyboard.
/// </summary>
public class KeyboardJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string Title = "Send referatet";

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_task_can_be_created_expanded_and_deleted_without_a_mouse()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        // Create: focus the field by tabbing, not by clicking.
        await App.Page.Keyboard.PressAsync("Tab");
        await App.Page.Keyboard.TypeAsync(Title);
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeVisibleAsync();

        // Expand: the row title is a button, so Enter on it must open the detail panel.
        await tasks.RowTitled(Title).FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        // The note's edit button is the keyboard path to editing — the click handlers on the
        // rendered note are a mouse shortcut, not the only way in.
        await tasks.Detail.GetByTestId("note-edit").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.NoteEditor).ToBeVisibleAsync();
        await App.Page.Keyboard.PressAsync("Escape");

        // Delete: reachable and activatable by keyboard.
        await tasks.Detail.GetByTestId("delete-task").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeHiddenAsync();
    }
}
```

**Step 2: Kør den**

```
dotnet test tests/Todo.E2E/Todo.E2E.csproj --filter "FullyQualifiedName~KeyboardJourneyTests"
```

Forventet: **består**, eller fejler på et sted hvor tastaturet faktisk ikke kan nå. **Fejler den, så rapportér hvor og hvorfor, før du ændrer produktionskode** — det er et fund, og det skal beskrives før det rettes.

Bemærk at det første `Tab` rammer det første fokuserbare element på siden. Er det et navigationslink og ikke feltet, så tab videre og skriv i en kommentar hvor mange tryk der skal til; et hårdkodet antal der ikke passer, er en test der fejler af den forkerte grund.

**Step 3: Commit**

```
git add tests/Todo.E2E
git commit -m "⌨️ Bevis at en opgave kan skabes og slettes uden mus"
```

---

## Task 7: Alt-genvejssystemet

Designdokumentets afsnit 2 lover *"Hold Alt for at vise genvejene på knapperne — Windows-konventionen"*. Det her er den funktion. **Se anbefalingen om at dele skiven** — vurder inden du begynder, om den skal være sin egen skive.

**Files:**
- Create: `src/Todo.Web/src/app/shortcuts/shortcut-store.ts`
- Create: `src/Todo.Web/src/app/shortcuts/shortcut.ts`
- Modify: `src/Todo.Web/src/app/app.html`, `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/i18n/da.json`, `en.json`
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

**Step 3: Direktivet**

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

**Step 4: Lyt efter Alt**

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

**Step 5: Mærkaterne**

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

**Step 6: Oversættelsesnøgler**

Genvejsmærkaterne er enkeltbogstaver og hører ikke i oversættelsesfilerne. Men skal der en forklarende linje til — fx *"Hold Alt for at se genvejene"* — skal den have en nøgle i **begge** filer, ellers fejler paritetstesten. Tilføj `shortcuts.hint` til `da.json` og `en.json` hvis du tilføjer teksten; gør du ikke, så tilføj ingen nøgle.

**Step 7: Vitest på storen**

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

**Step 8: E2E på genvejene**

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

**Step 9: Se vagten fejle**

Fjern `appShortcut="n"` fra feltet, kør testen, og bekræft at den fejler med `none` frem for `new-task-input`. Sæt det tilbage.

**Step 10: Kør alt**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
npm.cmd run test --prefix src\Todo.Web -- --watch=false
dotnet test Todo.sln
```

Kontrastvagten skal stadig være grøn — mærkaterne er nye farver på skærmen, og de er dækket.

**Step 11: Commit**

```
git add src/Todo.Web/src
git commit -m "⌨️ Alt viser genvejene og aktiverer dem"
```

---

## Task 8: Dokumentation

**Files:**
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Ret de forkerte kontrasttal**

Begge dokumenter siger, at `text-gray-400` er *~2,9:1*. Den er **2,60:1**. Tallet 2,85 er `#9ca3af`, altså Tailwind **3**'s hex-palette; Tailwind 4 definerer paletten i oklch og `gray-400` er en anden farve. Ret tallet, og skriv **hvorfor** det var forkert — ellers bliver det skrevet ind igen næste gang nogen slår Tailwind-farver op i en ældre kilde.

Fjern samtidig påstanden om at `text-gray-500` er et problem: 4,84:1 på hvid, 4,63:1 på `gray-50`.

**Step 2: `HANDOFF.md`**

Tilføj skive 7 til "Færdigt"-tabellen. Fjern punkt 1 fra "Anbefalet rækkefølge" og lad `long` som id blive punkt 1 — det er så det eneste tilbage i den dyre kategori, så ret også indledningen ("To af punkterne" → ét). Fjern afsnittet med kendte startpunkter; de er ikke længere kendte, de er lukkede.

**Step 3: Designdokumentet**

Marker skive 7 **Færdig** i afsnit 9. I afsnit 10 lukkes to punkter: *"Skallen har ingen baggrundsfarve"* og *"Opgavelisten har ingen `dark:`-varianter"*. Erstat dem — som i skive 6 — med ét punkt der registrerer udfaldet og lektionen: at `<body>` uden baggrund gør hele `dark:`-systemet til usynlig tekst frem for blot forkerte farver, og at kontrasten nu er dækket af en vagt frem for af øjemål.

Delte du skiven, så Alt-genvejene blev deres egen: skriv det i afsnit 9 og omnummerér, som skive 6 gjorde.

**Step 4: Konventionerne i `CLAUDE.md`**

Under **Styling** hører de tre ting, der kostede denne skive:

> **Tailwind 4's palette er oklch, ikke Tailwind 3's hex.** `gray-400` er 2,60:1 på hvid, ikke
> 2,85:1. Slå aldrig et kontrasttal op i en kilde der viser `#9ca3af`; regn det ud fra
> `node_modules/tailwindcss/theme.css`.
>
> **Parret er ikke samme trin på begge sider.** `text-gray-500` holder AA i lyst tema (4,84:1),
> men `dark:text-gray-500` fejler (3,67:1 på `gray-900`). Dæmpet tekst er
> `text-gray-500 dark:text-gray-400`.
>
> **Pladsholderfarve ligger på `::placeholder`**, ikke på elementet. En DOM-gennemgang der kun
> læser `style.color` er blind for et felt brugeren ser hver gang appen åbnes.

Og under **Testdisciplin** den nye vagt: at kontrast måles i browseren med `getComputedStyle`, fordi parringen mellem tekst og baggrund kun findes der, og at `ContrastTests` dækker begge farvetemaer over alle fire skærme.

**Step 5: Testtal**

Opdatér "Testtal" til "Efter skive 7" med de tal du **målte**. Forventet: 33 Core, 109 Api, 133 + 4 Vitest, og E2E oppe fra 7 til 12. Skriv de faktiske.

**Step 6: Commit**

```
git add CLAUDE.md docs/HANDOFF.md docs/plans/2026-08-13-todo-app-design.md
git commit -m "📝 Ret kontrasttallene og luk de to dark mode-punkter"
```

---

## Færdig når

- `ContrastTests` er grøn i **begge** farvetemaer over alle fire skærme og det udfoldede detaljepanel.
- **Vagten er set fejle** på det den beskytter: Task 1 (rød på dagens kode), Task 4 Step 3 (én fjernet `dark:`-modpart), Task 5 Step 2 (`outline: none`), Task 7 Step 9 (fjernet genvej). Alle fire med fejltekst i rapporten.
- `<body>` sætter baggrund og tekstfarve i begge temaer.
- Ingen `focus:outline-none` er tilbage uden en `focus-visible`-ring ved siden af.
- En opgave kan skabes, udfoldes og slettes uden mus.
- Alt viser mærkaterne, og de seks genveje virker. `Ctrl+Alt` gør ikke.
- De forkerte kontrasttal er rettet i begge dokumenter, med begrundelsen.
- Testtallene er skrevet ned som målt, og intet gammelt tal er faldet.

## Til skive 8 (`long` som id)

Skiven her rører ingen id'er, så prisen for `long` er uændret — men den er nu det eneste punkt tilbage, der bliver dyrere af at vente. Designdokumentets afsnit 10 har begrundelsen og advarslen.
