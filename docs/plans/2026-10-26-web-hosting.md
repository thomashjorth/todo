# Web-udgaven 2.0.0 — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** Appen kører i en container på brugerens Synology DS224+, deployes automatisk ved hvert push
til `main`, og en hjælper på PC'en tunnelerer AI-, Jira- og ADO-kaldene og er det eneste sted
tokens ligger. For brugeren er det én app.

**Designet er `docs/plans/2026-10-26-web-hosting-design.md`.** Læs det først. Afsnit 1 har de
begrænsninger alt andet hviler på, og hver beslutning er truffet af brugeren afsnit for afsnit.

**Arkitektur:** `Todo.Host` bliver en ren webserver bag et login for én bruger. AI, Jira og ADO
bliver på serveren som i dag, men deres udgående HTTP-kald går gennem en WebSocket som hjælperen
selv har åbnet ud til NAS'en; hjælperen sætter `Authorization` og viderestiller kun til en hvidliste.
Hjælperens UI er en iframe fra `127.0.0.1` på indstillingssiden.

**Teknologi:** .NET 10 minimal APIs, EF Core 10 + SQLite, Angular 22 med signal-stores, Tailwind 4,
xUnit, Playwright, Vitest; Docker, GitHub Actions, GHCR, Synology Container Manager.

**Planen kan være forkert.** Over seks leverancer fandt gennemgangene omkring hundrede planfejl og
nul udførelsesfejl. Mål frem for at tro på det der står her, og **ret planfilen** når en måling
modsiger den.

## Fem leverancer, og kun den første er planlagt i detaljer

En plan for alle fem ville være forældet før den tredje begyndte: leverance 4 hviler på hvad
leverance 1–3 målte. Hver leverance får derfor sin egen planfil, skrevet når den forrige er
landet, og afsnittet her er rækkefølgen og grunden til den.

| # | Leverance | Færdig når |
|---|-----------|-----------|
| 1 | **Værten bliver web-klar** | Photino er væk, links åbnes af browseren, `Todo.cmd` åbner appen i Edges app-vindue, `Check.cmd` grøn |
| 2 | **Login** | Alt under `/api/` undtagen health og login kræver en cookie; E2E logger ind gennem login-siden |
| 3 | **Container og auto-deploy** | Et push til `main` giver et prøvet image i GHCR, og NAS'en kører det bag login — med en **tom** database |
| 4 | **Tunnelen og hjælperen** | `Todo.Helper` forbinder, AI, Jira og ADO går gennem den, tokens er væk fra databasen |
| 5 | **"Forbindelser" som microfrontend, og flytningen** | Tokens tastes i iframen, vagten mod token-lækage er set fejle, data er flyttet til NAS'en |

**Rækkefølgen er bærende tre steder:**

- **Login før containeren.** Databasen har tokens i klartekst indtil leverance 4. En container uden
  login på LAN'et ville give alle på nettet adgang til dem gennem importen.
- **Dataene flytter først i leverance 5.** Indtil da kører NAS'en med en tom database, og brugeren
  bliver ved med at bruge `Todo.cmd` på PC'en. Flyttes de før tunnelen findes, holder AI og importen
  op med at virke, og tokens kommer til at ligge på NAS'en — netop det designet forbyder.
- **`Todo.cmd` kører mod brugerens rigtige database** (`run-app.ps1` giver ingen `--Data:Path`), så
  hver leverance der lander på `main`, rammer den daglige brug. Leverance 4's migrering sletter
  tokens; fra den dag kræver lokal brug at hjælperen kører ved siden af. Skriv det i leverance 4's
  plan, før migreringen skrives.

## Spørg om byg- og testtilladelse én gang

Brugerens regel: byg og test kræver accept, **og der spørges én gang for hele opgaven**, ikke pr.
kommando. Spørg derfor ved starten af leverance 1 om lov til `dotnet build`, `dotnet test`,
`scripts\build-web.ps1`, Vitest og `Check.cmd` for hele leverancen.

## Maskinens fælder der rammer netop denne plan

- `pwsh` findes ikke. `npm.cmd`/`ng.cmd`, aldrig `npm`/`ng`.
- **Brug `Edit`, ikke `sed` eller `Write`, på eksisterende filer.** Begge skriver LF i denne
  CRLF-arbejdskopi. Skal `Write` bruges, konvertér tilbage og verificér med
  `tr -dc '\r' < fil | wc -c` mod `tr -dc '\n' < fil | wc -c`.
- **Kør `scripts\build-web.ps1` før E2E**, også når du ruller en mutation tilbage.
- **Ret kontrakten og kør `scripts\generate-api.ps1`**, ellers fejler friskheds-testen.
- **Ingen `"` i en `git commit -m`**, og verificér et emne med gitmoji med `od` **gennem
  Bash-værktøjet**. `📝` er `360 237 223 235`, `✨` er `342 234 250`, `🔥` er `360 237 224 245`,
  `♻️` er `342 231 273 357 270 217`.
- **Backticks i en Bash-streng i dobbelte anførselstegn er kommandosubstitution.** Mødt under
  designet: en release-tekst med `` `docs/plans/…md` `` fik shellen til at køre designfilen som et
  script. Læg tekst med backticks i en fil, eller brug enkelte anførselstegn.
- Én Vitest-fil køres med `--include` og en **sti** med skråstreger:
  `npm.cmd run test --prefix src/Todo.Web -- --watch=false --include src/app/system/system-store.spec.ts`.
- **Kør aldrig mod `%APPDATA%\TodoApp\todo.db`**, og dræb aldrig en `Todo.Host` du ikke selv har
  startet — find den på porten, ikke på navnet. Fra opgave 7 lytter brugerens egen app på **5180**.

## Sproget i koden er engelsk, og planens kodestumper er det ikke

Kodekommentarer, XML-docs og testnavne er på engelsk. Kodestumperne her har danske kommentarer,
fordi planen læses på dansk — **oversæt dem**, og skriv dem i husets form: *hvorfor*, og hvad der
blev **målt**. Skriv **umålt** hvor noget er umålt.

---

# Leverance 1: værten bliver web-klar

**Hvad der ændrer sig for brugeren:** `Todo.cmd` åbner appen i et Edge-vindue i app-tilstand
(ingen adresselinje, 480×1000 som Photino-vinduet) i stedet for et Photino-vindue. Links åbner i
browseren. Alt andet er som før, også AI og importen, som stadig kalder direkte fra PC'en.

**Hvad der *ikke* ændrer sig:** kontrakten mister én operation, ellers ingen. Ingen migrering.

## Afvigelsen fra designet, og hvorfor

Designets afsnit 7 siger at de syv kaldesteder bliver `<a target="_blank" rel="noopener">`. **De
bliver i stedet ved med at være knapper, der kalder `window.open(url, '_blank', 'noopener')`.**

- Grunden til knappen står stadig: et app-vindue i Edge har heller ingen adresselinje og ingen
  tilbage-knap, så en navigation *i* vinduet er stadig envejs — og en knap kan ikke midterklikkes
  ind i samme vindue.
- Seks tests i E2E og Vitest påstår `tagName === 'BUTTON'` med netop den begrundelse, og
  locatorer som `GetByRole(Button, "Åbn sagen")` bliver ved med at virke.
- Skemakontrollen (http, https, mailto) bor ét sted, i `SystemStore`, i stedet for at blive gentaget
  ved hvert anker.

**Og to fejllinjer forsvinder, fordi de ikke længere kan nås.** `jira-open-error` og
`ado-open-error` står ved en forhåndsvisningsrække når *serveren* afviste at åbne linket. Efter
omlægningen kan åbningen kun fejle på skemaet, og rækkernes URL'er bygges af `JiraSettings.BaseUri`
og `AdoSettings.BaseUri`, som **kun** accepterer http og https (`AdoSettings.cs:42-46`, målt ved
læsning). Grenen ville være død kode med en kontrastmåling der kun kan nås ved at iscenesætte et
svar serveren aldrig giver. `note-link-error` i detaljepanelet **bliver**: en note er tekst andre
har skrevet, og `[x](file:///C:/)` er en note man kan få.

---

### Opgave 1: Ret designet efter afvigelsen

**Filer:**
- Ændr: `docs/plans/2026-10-26-web-hosting-design.md` (afsnit 7, punktet "Links become plain links")

**Trin 1:** Erstat punktet med:

```markdown
- **Links open in the browser.** `/api/system/open-link`, `ShellLinkLauncher` and
  `ErrorCodes.SystemUnsupportedScheme` leave. The seven call sites stay buttons and call
  `window.open(url, '_blank', 'noopener')` through `SystemStore`, which keeps the scheme check (http,
  https, mailto) in one place. A button rather than an anchor, because the app window has no address
  bar and no back button either, so a navigation inside it is still one-way. The per-row open errors
  on the Jira and ADO previews go: their URLs are built from base URLs that only accept http and
  https, so the branch could no longer be reached. The note's error line stays.
```

**Trin 2:** Verificér CRLF (`tr`-målingen ovenfor) og commit:

```bash
git add docs/plans/2026-10-26-web-hosting-design.md
git commit -m "📝 Links stay buttons and open through window.open, and the previews lose an error they can no longer reach"
```

---

### Opgave 1a: "Åbn sagen" på et betydningshit får en test, før mekanismen skiftes

`TaskList.openHit` (`task-list.ts:219`) klikkes af ingen test i dag — `task-list.spec.ts:236` tæller
kun knapperne. Leverance 1 ændrer metoden, så den skal være målt **før**. Fundet af `prep-refactor`,
valgt af brugeren 2026-10-26.

**Filer:**
- Test: `src/Todo.Web/src/app/tasks/task-list.spec.ts` (ved siden af testen omkring linje 236)

**Trin 1:** Læs testen ved linje 236 og genbrug dens opsætning af et betydningshit. Tilføj
`should open a meaning hit's issue through the system`: klik `[data-testid="meaning-hit-open"]` og
påstå `JSON.parse(http.expectOne('/api/system/open-link').request.body)` mod `{ url: <hittets
externalUrl> }`. Vælg et hit hvis URL kun kan komme fra *det* hit.

**Trin 2:** Kør filen. Forventet: **grøn** — den måler den nuværende adfærd.

**Trin 3: Se den fejle.** Byt `openHit(hit.externalUrl)` i `task-list.html` til en fast streng,
kør, se den fejle på URL'en, ret tilbage.

**Trin 4:** Commit:

```bash
git add src/Todo.Web/src/app/tasks/task-list.spec.ts
git commit -m "✅ Opening a meaning hit's issue is measured before the way links open changes"
```

---

### Opgave 1b: E2E læser åbnede links gennem `TodoApp`, før mekanismen skiftes

Fundet af `prep-refactor`, valgt af brugeren 2026-10-26. Seks tests fanger i dag den URL en knap
åbner, hver med sin egen route. Flyttes de til én læser i `TodoApp` **nu**, mens mekanismen stadig er
`/api/system/open-link`, kører de **uændrede** gennem skiftet i opgave 5 — og er dermed beviset for
at hvert kaldested stadig åbner samme URL.

**Filer:**
- Ændr: `tests/Todo.E2E/TodoApp.cs`
- Ændr: `ApiDocsJourneyTests.cs` (46), `MarkdownNoteJourneyTests.cs` (93), `AdoImportJourneyTests.cs`
  (355, 446, 529-530, 575, 695), `JiraImportJourneyTests.cs` (270, 318, 412),
  `ImportClosureJourneyTests.cs` (70), `ContrastTests.cs` (1593, 1712, 1832)

**Trin 1: `TodoApp`.** I `OpenAsync`, efter `NewPageAsync` og før `GotoAsync`:

```csharp
        await CaptureOpenedLinksAsync(page);
```

og i klassen:

```csharp
    private readonly List<string> _openedLinks = [];

    /// <summary>
    /// Fanger hvert link appen beder om at få åbnet, og afbryder kaldet: ellers ville styresystemet
    /// åbne en rigtig browser på maskinen der kører testene. For hele suiten frem for pr. test, så en
    /// ny knap ikke kan glemme det.
    /// </summary>
    private async Task CaptureOpenedLinksAsync(IPage page) …
```

Bemærk at `OpenAsync` er statisk og bygger `TodoApp` efter `GotoAsync`: byg instansen først, eller
lad listen bo på siden. Mekanismen er en route på `**/api/system/open-link`, der læser `url` af
kroppen (med samme sentinel-tekst som testene bruger i dag ved en manglende `url`) og afbryder.

```csharp
    /// <summary>Venter på at appen åbner sit n'te link, og svarer med det.</summary>
    public async Task<string> OpenedLinkAsync(int ordinal = 1) …
```

med samme signatur som opgave 5 bruger, og et loft på ti sekunder som testene har i dag.

**Trin 2: De seks der fanger en URL** (ApiDocs 46, MarkdownNote 93, Ado 355 og 529-530, Jira 270 og
318) mister deres route og `TaskCompletionSource` og læser `await App.OpenedLinkAsync()` efter
klikket. `UnrouteAsync` ved Ado 529 skal væk — den ville også fjerne suitens route.

**Trin 3: De otte der kun værnede** (Ado 446, 575, 695; Jira 412; ImportClosure 70; Contrast 1593,
1712, 1832) slettes med deres kommentar. **Kun** de tre i `ContrastTests` ved 627, 941 og 1199
bliver: Playwright prøver den senest registrerede route først, så de vinder stadig, og de maler
fejllinjerne opgave 5 tager sig af.

**Trin 4:** Byg frontenden og kør de berørte E2E-klasser. Forventet: grøn, samme antal.

**Trin 5: Se læseren bære.** Lad routen afbryde uden at gemme URL'en, kør `MarkdownNoteJourneyTests`,
se den fejle på timeout i `OpenedLinkAsync`, ret tilbage.

**Trin 6:** Commit:

```bash
git add tests/Todo.E2E
git commit -m "✅ The E2E suite reads opened links through TodoApp, so the six tests that do can outlive the mechanism"
```

---

### Opgave 2: `SystemStore` åbner selv

**Filer:**
- Ændr: `src/Todo.Web/src/app/system/system-store.spec.ts` (hele filen)
- Ændr: `src/Todo.Web/src/app/system/system-store.ts` (hele filen)

**Trin 1: Skriv specs der fejler.** Erstat filen med:

```ts
import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../i18n/transloco.testing';
import { SystemStore } from './system-store';

function configure(): SystemStore {
  TestBed.configureTestingModule({ imports: [translocoTesting()] });

  return TestBed.inject(SystemStore);
}

describe('SystemStore', () => {
  let open: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    // jsdom har ingen window.open der gør noget; spionen er både målingen og stoppet.
    open = vi.spyOn(window, 'open').mockReturnValue(null);
  });

  afterEach(() => open.mockRestore());

  it('should open a web link in a new browser context without an opener', () => {
    const store = configure();

    store.openLink('https://example.com/docs');

    expect(open).toHaveBeenCalledWith('https://example.com/docs', '_blank', 'noopener');
    expect(store.error()).toBeNull();
  });

  // Marked gør en bar adresse i en note til et mailto:-link, så et afslag her ville få en almindelig
  // note til at se i stykker ud.
  it('should open a mailto link', () => {
    const store = configure();

    store.openLink('mailto:someone@example.com');

    expect(open).toHaveBeenCalledWith('mailto:someone@example.com', '_blank', 'noopener');
  });

  it('should refuse a file link and say why', () => {
    const store = configure();

    store.openLink('file:///C:/Windows');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).toBe('Kun web- og mail-links kan åbnes.');
  });

  // Angulars sanitizer gør et javascript:-link til "unsafe:javascript:…", og det er den form
  // link.href har, når noten klikkes. Afslaget skal gælde den form, ikke kun den rå.
  it('should refuse a link the sanitizer has marked unsafe', () => {
    const store = configure();

    store.openLink('unsafe:javascript:alert(1)');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).not.toBeNull();
  });

  it('should refuse text that is not a URL at all', () => {
    const store = configure();

    store.openLink('ikke en adresse');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).not.toBeNull();
  });

  // Rækkefølgen er tænderne: afslaget skal stå FØR det gyldige link, ellers kunne en store der
  // aldrig satte fejlen, bestå.
  it('should clear an earlier refusal when the next link opens', () => {
    const store = configure();

    store.openLink('file:///C:/Windows');
    expect(store.error()).not.toBeNull();

    store.openLink('https://example.com/docs');

    expect(store.error()).toBeNull();
  });
});
```

**Trin 2: Kør og se dem fejle.**

Kør: `npm.cmd run test --prefix src/Todo.Web -- --watch=false --include src/app/system/system-store.spec.ts`

Forventet: rød. De fleste fejler på at `window.open` aldrig blev kaldt, fordi den nuværende
`openLink` sender til et endpoint. Står de **grønne**, måler de ikke det de skal — stop og find ud
af hvorfor.

**Trin 3: Skriv storen.** Erstat filen med:

```ts
import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

/**
 * De skemaer et link må åbnes med. Marked gør en bar adresse i en note til mailto:, så et afslag
 * på det ville få en almindelig note til at se i stykker ud.
 */
const ALLOWED_SCHEMES = new Set(['http:', 'https:', 'mailto:']);

@Injectable({ providedIn: 'root' })
export class SystemStore {
  private readonly transloco = inject(TranslocoService);

  readonly error = signal<string | null>(null);

  /**
   * Åbner et link i en ny fane eller et nyt vindue. Aldrig i stedet for appen: app-vinduet har
   * hverken adresselinje eller tilbage-knap, så en navigation i det er envejs. 'noopener', fordi
   * siden der åbnes er en anden end vores og ikke skal have et håndtag på appens vindue.
   */
  openLink(url: string): void {
    this.error.set(null);

    if (!ALLOWED_SCHEMES.has(schemeOf(url))) {
      // Nøglen ligger under errors.*, fordi den var serverens fejlkode indtil 2.0.0. Teksten er den
      // samme, og ErrorCodeTranslationTests går kun fra kode til nøgle, så en nøgle uden kode
      // fælder intet.
      this.error.set(this.transloco.translate('errors.system.unsupportedScheme'));
      return;
    }

    window.open(url, '_blank', 'noopener');
  }

  clearError(): void {
    this.error.set(null);
  }
}

/** Skemaet med kolon, som URL.protocol staver det — eller tom streng for noget der ikke er en URL. */
function schemeOf(url: string): string {
  try {
    return new URL(url).protocol;
  } catch {
    return '';
  }
}
```

**Trin 4: Ret teksten.** `errors.system.unsupportedScheme` sagde "Kun http- og https-links kan
åbnes", men mailto var tilladt både før og efter (brugerens valg 2026-10-26). `da.json`:
`"Kun web- og mail-links kan åbnes."`; `en.json`: `"Only web and mail links can be opened."` Søg efter
den gamle sætning i hele `src` og `tests` og ret hvert sted der påstår den.

**Trin 4b: Kør igen.** Samme kommando. Forventet: grøn.

**Trin 5: Se den ene vagt fejle på sin påstand.** Fjern `'mailto:'` fra `ALLOWED_SCHEMES`, kør,
og bekræft at **præcis** `should open a mailto link` fælder (og hvorfor ingen anden gør). Læg den
tilbage. Kør `git diff --stat` og se at kun de to filer står der.

**Trin 6:** Commit **ikke** endnu. Opgave 3 retter kalderne, og indtil da compilerer appen ikke —
`openLink` returnerer ikke længere et `Promise`, og hvert `.catch(() => {})` er nu en typefejl. Det
er meningen: compileren finder kalderne for dig.

---

### Opgave 3: Kalderne bruger den nye `openLink`

**Filer:**
- Ændr: `src/Todo.Web/src/app/tasks/task-detail.ts:166`, `tasks/task-list.ts:217-221`,
  `tasks/task-row.ts:92-95`, `app.ts:59-62`
- Ændr: `src/Todo.Web/src/app/jira/jira-import.ts` (omkring 185-200) og `jira-import.html` (omkring
  224-255)
- Ændr: `src/Todo.Web/src/app/ado/ado-import.ts` (omkring 195-215) og `ado-import.html` (omkring
  309-335)
- Ændr: `tasks/task-row.html:132-133`
- Test: `tasks/task-list.spec.ts` (omkring 775-850), `jira/jira-import.spec.ts` (omkring 425-490),
  `ado/ado-import.spec.ts` (omkring 380-440)

**Trin 1: Ret specs først**, så de fejler mod den gamle kode og består mod den nye. Mønsteret i
alle tre filer:

- Læg `let open: ReturnType<typeof vi.spyOn>;` og et `beforeEach`/`afterEach` med
  `vi.spyOn(window, 'open').mockReturnValue(null)` / `mockRestore()` i den `describe` testene står i.
- `http.expectOne('/api/system/open-link')` + påstand på kroppen bliver
  `expect(open).toHaveBeenCalledWith('<url>', '_blank', 'noopener')`.
- **`task-list.spec.ts`, "should say beside the note when a link could not be opened":** fixturet
  `withLink` har et https-link. Lav en søster til fixturet med en note der indeholder
  `[fil](file:///C:/Windows)`, klik det, og påstå teksten `Kun web- og mail-links kan åbnes.` i
  `note-link-error` — uden nogen HTTP-flush, for der går ingen request.
- **`jira-import.spec.ts` og `ado-import.spec.ts`:** slet testen "should say beside the row that its
  issue could not be opened, and only there" (og ADO's tilsvarende). Grunden står i afsnittet om
  afvigelsen. Behold "open the second row"-testen, men omskriv den til spionen.
- **Kommentarer der siger "the Photino window":** skriv "the app window". Begrundelsen er den samme.

**Trin 2: Ret kalderne.**

`task-detail.ts` (`clickNote`):

```ts
    if (link) {
      // At følge linket ville erstatte hele appen: app-vinduet har ingen vej tilbage.
      event.preventDefault();
      this.system.openLink(link.href);
      return;
    }
```

`task-list.ts` (`openHit`), `task-row.ts` (`openIssue`) og `app.ts` (`openApiDocs`): fjern
`.catch(() => {})`. I `app.ts` bliver kommentaren *"The port is random, so the URL is built from the
app's own origin"* til at URL'en bygges af origin, fordi appen ikke kender sin egen vært — den kører
fra opgave 7 på en fast port lokalt og fra leverance 3 bag NAS'ens navn.

`jira-import.ts` og `ado-import.ts`: `openIssue`/`openItem` bliver synkrone og mister
`openError`-signalet og dets kommentar:

```ts
  /** Åbner sagen i browseren, så en række kan læses før den importeres. */
  protected openItem(row: AdoPreviewRow): void {
    this.system.openLink(row.url);
  }
```

`jira-import.html`/`ado-import.html`: slet `@let failedOpen = openError();`-blokken og dens
`<p data-testid="…-open-error">`. Ret kommentaren over knappen fra Photino til app-vinduet.

**Trin 3: Søg efter rester.**

```bash
grep -rn "openError\|open-error\|api/system\|SystemClient\|OpenLinkRequest" src/Todo.Web/src --include=*.ts --include=*.html | grep -v todo-client.ts
```

Forventet: intet. (`todo-client.ts` mister dem først i opgave 4.)

**Trin 4: Kør hele Vitest.**

Kør: `npm.cmd run test --prefix src/Todo.Web -- --watch=false`

Forventet: grøn. Tallet **falder** med de slettede række-fejl-tests og stiger med
`system-store.spec.ts`' netto-tilvækst — skriv begge identiteter ned, ikke kun differensen, til
opgave 9.

**Trin 5: Prettier.** `.\node_modules\.bin\prettier.cmd --check .` fra `src\Todo.Web`.

**Trin 6: Commit.**

```bash
git add src/Todo.Web/src
git commit -m "♻️ Links open through window.open in the browser, with the scheme checked in the store"
```

---

### Opgave 4: Serveren mister `open-link`

**Filer:**
- Ændr: `contracts/openapi.yaml` (stien `/api/system/open-link` omkring linje 626, skemaet
  `OpenLinkRequest` omkring 2136, og tagget `System` hvis intet andet bruger det)
- Slet: `src/Todo.Host/Endpoints/SystemEndpoints.cs`, `src/Todo.Host/Links/` (begge filer),
  `tests/Todo.TestSupport/Links/RecordingLinkLauncher.cs`,
  `tests/Todo.Api.Tests/SystemEndpointsTests.cs`, `tests/Todo.Api.Tests/LinkLauncherRegistrationTests.cs`
- Ændr: `src/Todo.Host/TodoHost.cs` (`using Todo.Host.Links;`, registreringen linje 90,
  `app.MapSystem();`), `src/Todo.Core/Errors/ErrorCodes.cs:206`
- Genereret: `src/Todo.Web/src/app/api/todo-client.ts` og det tilsvarende i `Todo.Contracts`

**Trin 1:** Fjern stien og skemaet fra kontrakten. Kør `scripts\generate-api.ps1`.

**Trin 2:** Slet filerne og linjerne ovenfor. Søg efter rester:

```bash
grep -rn "ILinkLauncher\|ShellLinkLauncher\|RecordingLinkLauncher\|SystemUnsupportedScheme\|MapSystem\|OpenLinkRequest\|open-link" src tests --include=*.cs --include=*.ts --include=*.yaml | grep -v "/bin/\|/obj/"
```

Forventet: kun E2E-filerne, som opgave 5 retter.

**Trin 3:** `dotnet build Todo.sln`. Forventet: E2E-projektet compilerer stadig (dets
`RouteAsync("**/api/system/open-link", …)` er strenge, ikke typer).

**Trin 4:** Kør `Todo.Api.Tests`:

```bash
dotnet test tests/Todo.Api.Tests
```

Forventet: grøn, færre tests. `ContractDriftTests` og `GeneratedCodeFreshnessTests` skal være
grønne, ellers mangler genereringen. `ErrorCodeTranslationTests` er grøn, fordi den går fra kode til
nøgle — **nøglen `errors.system.unsupportedScheme` skal blive stående**, `SystemStore` læser den.
Skriv identiteterne på de slettede tests ned til opgave 9.

**Trin 5: Commit.**

```bash
git add -A contracts src tests
git commit -m "🔥 The server no longer opens links on its own machine, the browser does"
```

Tjek med `git status` at kun de forventede filer er med, og at `AGENTS.md` (uversioneret, ikke
vores) **ikke** er det.

---

### Opgave 5: E2E-suiten erstatter `window.open` for hele suiten

**Hvorfor for hele suiten:** afbrydelsen af `/api/system/open-link` var en egenskab ved den enkelte
test, og CLAUDE.md har en målt historie om en ny knap der ville have åbnet en rigtig browser fra en
*anden* test i samme fil. En init-script på hver side lukker den klasse helt: ingen test kan glemme
det, og intet link kan forlade testbrowseren.

**Opgave 1b har gjort det meste:** læseren `OpenedLinkAsync` findes, og kun routen i `TodoApp` og de
tre `ContrastTests`-steder står tilbage.

**Filer:**
- Ændr: `tests/Todo.E2E/TodoApp.cs` (routen fra opgave 1b bliver til init-scriptet nedenfor)
- Ændr: de steder `grep -n "api/system/open-link" tests/Todo.E2E/*.cs` stadig finder
- Ændr: kommentarer om Photino i `AdoImportScreen.cs`, `JiraImportScreen.cs`,
  `KeyboardJourneyTests.cs`, `ApiDocsJourneyTests.cs`, `AdoImportJourneyTests.cs`,
  `JiraImportJourneyTests.cs`

**Trin 1: `TodoApp` får erstatningen og en læser.** I `OpenAsync`, lige efter `NewPageAsync` og
før `GotoAsync`:

```csharp
        await ReplaceWindowOpenAsync(page);
```

og i klassen:

```csharp
    /// <summary>
    /// Hvert link appen åbner, går gennem window.open, og en testbrowser har ingen grund til at følge
    /// et ud af processen: målet er et fremmedsystem eller en falsk server der allerede er stoppet.
    /// Gjort for hele suiten frem for pr. test, så en ny knap ikke kan glemme det — det hul
    /// afbrydelsen af /api/system/open-link efterlod.
    /// </summary>
    public static Task ReplaceWindowOpenAsync(IPage page) => page.AddInitScriptAsync("""
        window.__openedLinks = [];
        window.open = (url) => { window.__openedLinks.push(String(url)); return null; };
        """);

    /// <summary>Venter på at appen åbner sit n'te link, og svarer med det.</summary>
    public async Task<string> OpenedLinkAsync(int ordinal = 1)
    {
        await Page.WaitForFunctionAsync("n => window.__openedLinks.length >= n", ordinal);

        return (await Page.EvaluateAsync<string[]>("window.__openedLinks"))[ordinal - 1];
    }
```

**Trin 2: Find de sider der ikke går gennem `OpenAsync`.**

```bash
grep -n "NewPageAsync" tests/Todo.E2E/*.cs
```

Hver side der skabes andetsteds (mindst `ApiDocsJourneyTests`' offline-test) skal kalde
`TodoApp.ReplaceWindowOpenAsync(page)` før sin første `GotoAsync`.

**Trin 3: De tre steder der er tilbage efter opgave 1b.** Regel 1 og 2 nedenfor er gjort i opgave
1b og står her for sammenhængens skyld.

1. *(Opgave 1b)* Afbrydelser der kun værnede, er slettet.
2. *(Opgave 1b)* Afbrydelser der fangede URL'en, læser nu `App.OpenedLinkAsync()`.
   `stampedBeforeTheClick`-påstandene bliver: de måler at vinduet ikke navigerede.
3. **Afbrydelsen malede en fejllinje** (`ContrastTests` 627, 941, 1199):
   - **627 (noten):** fixturets note skal have et link med et afvist skema, f.eks.
     `[fil](file:///C:/Windows)`, og det er det link rejsen klikker for at få `note-link-error` på
     skærmen. Påstå at linjen *er* der før målingen, ellers måler kontrasten ingenting. Påstanden
     omkring linje 694 venter på `"Prøv igen"` — den generiske tekst en afbrudt request gav. Med et
     afvist skema står skemasætningen der i stedet, så påstanden skal skrives om til den.
   - **941 og 1199 (Jira- og ADO-rækken):** fejllinjen findes ikke længere. Slet afbrydelsen og
     klikket der skulle male den, og ret kommentaren over. Med dem går påstandene
     `OpenErrorIn` i `ContrastTests.cs` (omkring 1121 og 1414), `AdoImportJourneyTests.cs` (omkring
     392) og `JiraImportJourneyTests.cs` (omkring 359), og hjælperne `OpenErrorIn` i
     `AdoImportScreen.cs:156` og `JiraImportScreen.cs:118`. Fundet af `prep-refactor`, ikke af
     planens første udgave.

**Trin 4: Byg frontenden og kør E2E.**

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E
```

Forventet: grøn. Tallet står stille, medmindre en test faldt bort i regel 3 — skriv identiteten ned.

**Trin 5: Se erstatningen bære.** Gør `SystemStore.openLink` til en no-op (kommentér
`window.open`-kaldet ud), **byg frontenden**, kør `MarkdownNoteJourneyTests` og
`ApiDocsJourneyTests`, og bekræft at de fejler på en timeout i `OpenedLinkAsync`. Læg linjen tilbage,
**byg frontenden igen**, og kør dem grønne. Commit ikke mutationen: kør `git diff --stat` og se at
`system-store.ts` ikke står der.

**Trin 6: Søg efter rester og commit.**

```bash
grep -rn "open-link\|Photino" tests/Todo.E2E --include=*.cs
```

Forventet: intet.

```bash
git add tests/Todo.E2E
git commit -m "✅ The E2E suite replaces window.open on every page, so no link can leave the test browser"
```

---

### Opgave 6: Photino ud af værten

**Filer:**
- Ændr: `src/Todo.Host/Program.cs` (hele filen)
- Ændr: `src/Todo.Host/Todo.Host.csproj`
- Ændr: `scripts/publish.ps1` (linje 73-95 og argumenterne omkring 131)

**Trin 1: `Program.cs`.** Erstat filen med:

```csharp
namespace Todo.Host;

public static class Program
{
    // Intet vindue: fra 2.0.0 er appen en webserver, og vinduet er browserens. --headless fandtes kun
    // for at slå Photino fra, og et ukendt "--headless" er ikke gyldig kommandolinje-konfiguration —
    // derfor er det også væk fra scripts og README.
    public static void Main(string[] args) => TodoHost.Build(args).Run();
}
```

**Trin 2: `Todo.Host.csproj`.**

- Slet `<OutputType>WinExe</OutputType>` og kommentaren over. Web-SDK'ets standard er en
  konsol-exe, og det er det rigtige for en server.
- Slet `<Content Include="..\Todo.Web\public\favicon.ico" Link="icon.ico" … />` og kommentaren. Den
  fandtes kun fordi Photino skulle have ikonet som sti.
- Slet `<PackageReference Include="Photino.NET" … />`.
- Ret kommentarerne der siger "this app is a Photino window that hosts Kestrel on loopback"
  (`IsTransformWebConfigDisabled`) til at appen ikke kører under IIS.
- `ApplicationIcon` bliver: det er exe'ens eget ikon og koster intet på Linux.

**Trin 3: `publish.ps1`.** `$expected` bliver `@('Todo.Host.exe')`, `'--headless',` fjernes fra
probens argumenter, og kommentarerne om Photino og ikonet (73-95) skrives om. **Mål** at den
selvstændige udgivelse stadig kun giver én fil: `IncludeNativeLibrariesForSelfExtract` holdt
`e_sqlite3` inde, og det skal den stadig.

**Trin 4: Byg og test.**

```bash
dotnet build Todo.sln
dotnet test Todo.sln
```

Forventet: grøn. `EmbeddedFrontendTests` og resten kører gennem `TodoHost.Build` og mærker intet.

**Trin 5: Kør `Publish.cmd`** med en midlertidig `-OutputPath`, så den rigtige installation ikke
røres. Forventet: én fil, og proben svarer 200 på frontenden, health og dokumentationssiden.

**Trin 6: Commit.**

```bash
git add src/Todo.Host scripts/publish.ps1
git commit -m "🔥 Photino goes, and the host is a web server whose window is the browser's"
```

---

### Opgave 7: `Todo.cmd` åbner appen i Edges app-vindue

**Filer:**
- Ændr: `scripts/run-app.ps1` (fra `Write-Host 'Starting Todo...'` til slutningen)

**Trin 1:** Erstat slutningen af scriptet med:

```powershell
# En fast port, fordi app-vinduet og browserens lager hører til en origin, og en tilfældig port ville
# give et nyt hver gang. 5180 er valgt fri af andre udviklingsservere; umålt mod hvad der ellers
# kører på maskinen.
$port = 5180
$url = "http://127.0.0.1:$port/"

# Kører den allerede, har brugeren dobbeltklikket igen. Så åbnes vinduet på den der kører, frem for
# at starte en anden vært der ville fejle på porten.
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
    Start-Process 'msedge.exe' -ArgumentList "--app=$url", '--window-size=480,1000'
    exit 0
}

$hostProject = Join-Path $root 'src\Todo.Host'

Write-Host 'Building Todo...' -ForegroundColor Cyan
& dotnet build $hostProject -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host 'Starting Todo...' -ForegroundColor Cyan

# Indholdsroden navngives af samme grund som før: den er hvor værten læser konfiguration.
$server = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -ArgumentList (@(
    'run', '--project', $hostProject, '-c', 'Release', '--no-build', '--no-launch-profile', '--',
    '--contentRoot', $hostProject, '--urls', $url.TrimEnd('/')) + $args)

# curl.exe, ikke Invoke-WebRequest: AMSI blokerer Start-Process kombineret med Invoke-WebRequest på
# denne maskine (CLAUDE.md, "Maskinen").
$ready = $false
foreach ($attempt in 1..120) {
    Start-Sleep -Milliseconds 500
    if ($server.HasExited) { exit $server.ExitCode }
    $code = & curl.exe -s -o NUL -w '%{http_code}' "${url}api/health"
    if ($code -eq '200') { $ready = $true; break }
}
if (-not $ready) { throw "Todo did not answer on ${url}api/health within a minute." }

Start-Process 'msedge.exe' -ArgumentList "--app=$url", '--window-size=480,1000'
Write-Host "Todo runs on $url - close this window to stop it." -ForegroundColor Cyan

$server.WaitForExit()
exit $server.ExitCode
```

**Trin 2: Mål det der ikke kan testes automatisk**, og skriv svarene i opgave 8's HANDOFF-afsnit:

- Starter `Todo.cmd` værten, åbner et Edge-vindue uden adresselinje, og viser appen?
- **Lukkes værten når konsolvinduet lukkes?** `dotnet run` starter appen som et barnebarn;
  antagelsen er at Windows sender CTRL_CLOSE til alle processer i konsollen. Umålt. Tjek med
  `Get-NetTCPConnection -LocalPort 5180 -State Listen` efter lukningen.
- Åbner et andet dobbeltklik et nyt vindue på den kørende vært?
- Åbner "Åbn sagen" og et link i en note en fane i browseren, mens app-vinduet bliver stående?

Kør **ikke** mod brugerens database for at måle det: giv `Todo.cmd --Data:Path <midlertidig fil>`,
som `$args` sender videre.

**Trin 3: Commit.**

```bash
git add scripts/run-app.ps1
git commit -m "✨ Todo.cmd opens the app in an Edge app window on a fixed port, and a second double-click finds the first"
```

---

### Opgave 8: Dokumentationen følger med

**Filer:**
- Ændr: `README.md` (linje 11 og 25 nævner `--headless`, 193 proben)
- Ændr: `CLAUDE.md` — afsnittet om de tre kommandoer (`Publish.cmd` "to filer … `icon.ico`, som
  Photino skal have som sti"), cifrene "kan **kun** prøves i Photino-vinduet", og første linje
  ("Én Photino-proces")
- Ændr: `docs/HANDOFF.md` — tabellens række 0–1 er historie og bliver; målingerne "i
  Photino-vinduet" (omkring 173, 189, 215) bliver til målinger i Edges app-vindue
- Ændr: `.github/workflows/ci.yml:18` — "appen selv er Photino + %APPDATA%" bliver til
  `%APPDATA%`-standardstien og `tsc.cmd`

**Trin 1:** Ret hvert sted. Hold CLAUDE.md-reglen: skriv kun det der ikke kan udledes af koden, og
ingen tal der forældes. Den ene nye fælde der hører hjemme i CLAUDE.md, er **backtick-fælden** fra
designet, under "Maskinen".

**Trin 2:** Læg opgave 7's målinger i HANDOFF som "kun brugeren kan måle": `Alt+1`–`9` og de andre
genveje i Edges app-vindue (Chrome binder cifrene til faneskift; om app-vinduet gør det, er umålt),
og om `type="time"`-vælgeren bliver mørk der.

**Trin 3:** Verificér CRLF på hver rettet fil med `tr`-målingen, og commit:

```bash
git add README.md CLAUDE.md docs/HANDOFF.md .github/workflows/ci.yml
git commit -m "📝 The docs speak of a browser window rather than Photino"
```

---

### Opgave 9: `Check.cmd`, og testtallene med deres grunde

**Trin 1:** Kør `Check.cmd`. Forventet: alle trin grønne.

**Trin 2:** Ret afsnittet "Testtal" i CLAUDE.md. Tallene **falder**, og hvert fald skal stå med sin
identitet, ikke som en differens:

- **Api:** `SystemEndpointsTests` og `LinkLauncherRegistrationTests` er slettet med deres endpoint.
- **E2E:** hvad regel 3 i opgave 5 slettede, hvis noget.
- **Vitest:** de to række-fejl-tests i `jira-import.spec.ts` og `ado-import.spec.ts` er væk; netto
  hvad `system-store.spec.ts` lagde til.

**Trin 3:** Commit:

```bash
git add CLAUDE.md
git commit -m "📝 Leverance 1 is counted: the tests that left with open-link, by name"
```

**Trin 4:** Brugerens regel er at `reviewer`-agenten kører før en PR. Her er der ingen PR, men
leverancen er stor nok til en gennemgang: spørg brugeren om den skal køres før push.

---

# Leverance 2–5: rammerne for de næste planer

Hver får sin egen fil, `docs/plans/<dato>-web-hosting-<n>-<navn>.md`, skrevet når den forrige er
landet. Her er det de skal dække, så intet falder mellem to planer.

**Leverance 2 — login.**
- Cookie-godkendelse for én bruger; password-hashen kommer fra konfiguration
  (`Auth:PasswordHash`), og værten **nægter at starte** uden en, så en container aldrig kan stå
  åben ved en fejl.
- En måde at lave hashen på uden at skrive passwordet i en kommandolinje (CLAUDE.md: tokens og
  hemmeligheder i `$env:`, aldrig i argumenter).
- Login-side i Angular; alt under `/api/` undtagen `/api/health` og login kræver cookien, og et 401
  sender til login-siden.
- `RunningHost` får et test-password, og `TodoApp.OpenAsync` logger ind gennem siden, så hver E2E
  kører login-vejen. `ApiTest` sender cookien.
- Lokal brug: `Todo.cmd` skal også have et password. Afgør i planen om det står i brugerens
  user-secrets eller en miljøvariabel.
- **`app.ts`' konstruktør henter `review`, `tags` og `ai` én gang ved opstart** — bag et login får de
  401 ved en kold start, og intet henter dem igen, fordi rodkomponenten ikke skabes på ny. Genindlæs
  efter login eller flyt kaldene (`prep-refactor`, 2026-10-26).
- `RunningHost` har ét `HttpClient` uden cookie-container; 401-testene skal også have en anonym.
- `app.MapGroup("").RequireAuthorization()` kan pakke alle `Map*`-kald uden at røre
  endpoint-filerne; `/api/health` er mappet for sig, uden for gruppen.
- `run-app.ps1` og `publish.ps1` starter værten; nægter den at starte uden hash, skal proben have en
  engangs-hash.

**Leverance 3 — container og auto-deploy.**
- `Dockerfile` i flere trin og `docker-compose.yml`, `TZ=Europe/Copenhagen` med en test eller probe
  der viser at "i dag" er dansk, `Data__Path=/data/todo.db`, ikke-root.
- `image`-jobbet i `ci.yml` (`needs: test`, kun `main`, ubuntu) med prøven før push, og push til
  `ghcr.io/thomashjorth/todo:latest` + `:<sha>`.
- Watchtower eller DSM-opgaven — **mål Watchtowers vedligeholdelse først**.
- En tjekliste til brugeren for NAS'en: Container Manager, mappen og dens ejer, Tailscale.
- Afgør om `Publish.cmd` skal blive (den laver en Windows-exe, som ingen kører efter 2.0.0).

**Leverance 4 — tunnelen og hjælperen.**
- `Todo.Helper`: WebSocket ud til NAS'en med nøgle og protokolversion, genforbindelse,
  hvidliste fra egne indstillinger, `Authorization` pr. mål, DPAPI-lager, et lokalt API på
  `127.0.0.1:<fast port>` til leverance 5.
- På NAS'en: tunnel-endpointet, `HttpMessageHandler` på de tre typed clients, `helper.offline` på
  begge sprog, hjælperens basis-URL'er i stedet for `jira.baseUrl`/`ado.baseUrl`.
- Migreringen der sletter tokens og basis-URL'er, og kontraktændringen på indstillingerne.
- `Todo.Helper.Tests`, og at de eksisterende `FakeJira`/`FakeAdo`-tests kører uændret med den
  direkte handler.
- **`configureServices` kører efter registreringerne og kan ikke fjerne en handler der er lagt på**
  (`TodoHost.cs`, de tre `AddHttpClient`). Tunnel-handleren skal derfor stå bag en indstilling eller
  en registreret tjeneste testene holder på den direkte vej (`prep-refactor`, 2026-10-26).

**Leverance 5 — "Forbindelser" og flytningen.**
- Microfrontenden i hjælperen, iframen i accordionen, `postMessage` med origin-tjek, reserveteksten.
- **Vagten mod token-lækage**, set fejle.
- Mål Chromes Local Network Access mod iframen, og HTTPS/PWA via Tailscale.
- Flytningen: checkpoint, kopi til volumet, tokens tastet i hjælperen, og en tjekliste brugeren
  følger.
