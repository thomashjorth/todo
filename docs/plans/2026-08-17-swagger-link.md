# Swagger-link på health-linjen — uden for skiverne

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Et klik på "API: ok" åbner API-dokumentationen i systemets browser — og den viser kontrakten, ikke en prosaløs afledning af den.

**Architecture:** `contracts/openapi.yaml` er sandheden i dette repo, og den indeholder den prosa man læser dokumentation for. Runtime-dokumentet fra `MapOpenApi()` matcher den strukturelt men har **nul** beskrivelser — målt. UI'en peges derfor på kontrakten, som indlejres som en ressource, så den også findes i en publiceret exe. `MapOpenApi()` bliver stående, fordi drift-testen læser den.

**Tech Stack:** ASP.NET Core 10 · `Microsoft.AspNetCore.OpenApi` 10.0.11 (allerede refereret) · en UI-pakke, valgt i Task 1 · Angular 22 signals · Playwright 1.62.0

## Hvorfor den ligger uden for skiverne

Den står i `docs/HANDOFF.md` under "Kan ligge stille". Den er ikke en funktionsskive: den tilføjer én affordance og ingen datamodel, ingen ny skærm og ingen ekstern kilde. `HANDOFF.md` har allerede et **"Uden for skiverne"**-afsnit til netop den slags — app-ikonet, `Todo.cmd`-launcheren, feature-mappe-omlægningen, testdata-builderne. Denne hører der.

**Det er også det valg der undgår en omnummerering.** Gav vi den nummer 9, skulle Jira til 10, ADO til 11 og så videre — den øvelse kostede to commits og efterlod tre forældede henvisninger efter skive 6, som først et review fangede. Er du uenig, er det ét sted i planen der skal ændres, men så skal omnummereringen med i Task 5.

## Hvad målingen viste

Målt 2026-08-17 ved at starte hosten headless mod en midlertidig database og hente dokumentet. **Tre af `HANDOFF.md`s antagelser holdt, én skal skærpes.**

### Spec-genereringen findes allerede — kun UI'en mangler

`src/Todo.Host/Todo.Host.csproj` refererer allerede `Microsoft.AspNetCore.OpenApi` 10.0.11, og `TodoHost.cs` kalder allerede `AddOpenApi()` og `MapOpenApi()`. `/openapi/v1.json` svarer **200** i dag.

Og påstanden om at .NET 10 ikke har en indbygget UI **holder** — verificeret ved at læse pakkens egne XML-docs: den eneste offentlige mapping-udvidelse er `MapOpenApi`. Der er ingen `MapOpenApiUi` eller lignende.

### Runtime-dokumentet matcher kontrakten i form, men er dokumentationsmæssigt tomt

| | Kontrakten | `/openapi/v1.json` |
| --- | --- | --- |
| Operationer | 15 `operationId` | **15** |
| Skemaer | 22 | **22** |
| Titel | `Todo API` | **`Todo.Host \| v1`** |
| OpenAPI-version | 3.0.4 | 3.1.1 |
| `description:` | **29** | — |
| `summary` på operationer | ja, fx *"Reports that the API is running."* | **0 af 15** |

> **Rettelse 2026-08-17: tabellens sidste række læses for gunstigt.** Et `ja` i kontraktens kolonne inviterer til at tro, at kontrakten har `summary` hele vejen igennem. Målt har **kun 4 af de 15 operationer** en — `/api/health`, retro-forhåndsvisningen, retro-importen og `/api/system/open-link`. Konklusionen nedenfor holder stadig (afledningen har **0**, og de 29 `description`-felter findes kun i kontrakten), men kontrakten er ikke færdigbeskrevet. Der er prosa at skrive.

Formen er altså ikke et problem — stier, operationer og skemaer stemmer én til én, og responskoderne ser rigtige ud (`201`/`400` på oprettelse, `404` på de indlejrede ruter). **Prosaen er problemet.** Det man åbner et Swagger-UI for at læse, findes kun i kontrakten.

Derfor peges UI'en på kontrakten. Det er samtidig det contract-first-rigtige: at vise en afledning som om den var kilden, inviterer til at nogen retter afledningen.

### `MapOpenApi()` er bærende og må ikke fjernes

`ContractDriftTests.OperationsFromRunningAppAsync` gør `Client.GetStreamAsync("/openapi/v1.json")` og sammenligner med kontrakten. Fjerner man `MapOpenApi()`, fejler drift-testen — og den er repoets vagt mod at implementeringen og kontrakten glider fra hinanden.

Så efter denne plan udstiller appen **to** dokumenter, med hver sin rolle: runtime-dokumentet som drift-testens målepunkt, og kontrakten som det mennesker læser. Det skal skrives ned, ellers ser det ud som en dublet nogen bør rydde op i.

### `http` og `https` er allerede hvidlistet

`SystemEndpoints.AllowedSchemes` er `[Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto]`. `HANDOFF.md` siger *"tilføj `http`/`https` er nok, de er hvidlistet"*, hvilket er forvirrende formuleret — men konklusionen holder: **der er ingenting at tilføje.**

### Kontrakten kommer ikke med i en bygning i dag

Kun `scripts/generate-api.ps1` refererer `contracts/openapi.yaml`. Den kopieres ikke til output og er ikke indlejret. `RepoPaths.ContractFile` findes kun i testprojekterne og regner sig frem fra `Todo.sln` — det virker ikke i en publiceret exe.

Skal UI'en vise kontrakten, skal filen altså **med i assemblyen**. Det er også det der holder, når pakke-skiven kommer.

### Health-linjen i dag

To grene i `app.html`, ingen af dem interaktive:

```html
  @if (status(); as s) {
    <p data-testid="health" class="mt-8 text-xs text-gray-500 dark:text-gray-400">
      {{ 'app.health' | transloco: { status: s.status, version: s.version } }}
    </p>
  } @else if (failed()) {
    <p data-testid="health" class="mt-8 text-xs text-red-600 dark:text-red-400">
```

## Beslutninger

| Emne | Valg |
| --- | --- |
| Hvad UI'en viser | **Kontrakten**, indlejret som ressource. Ikke runtime-dokumentet. |
| `MapOpenApi()` | Bliver. Drift-testen læser den. |
| Hvor linket sidder | Health-linjens **ok**-gren. Fejlgrenen får intet link — er API'et nede, er dokumentationen det også. |
| Hvordan det åbnes | Gennem `/api/system/open-link`, altså systemets browser. |
| Alt-genvej | **Nej.** Bogstaverne er knappe efter skive 8, og det her er en sjælden handling. |
| UI-pakke | Vælges i Task 1, **på bevis for at den virker offline**. |

**Linket skal gennem `/api/system/open-link`.** Navigerer Photino-vinduet selv til dokumentationen, er der ingen vej tilbage — vinduet har ingen adresselinje og ingen tilbage-knap. Det er den fælde skive 4 fandt, og den gælder her.

**Ingen genvej, og det er et valg.** Skive 8 lagde `Alt+O/I/S/N/V/M`. En syvende ville skulle undgå Chromes `Alt+D/E/F/Home` og de seks tagne, og gevinsten er lille for noget man klikker på en gang om måneden.

## Fælder i denne skive

- **Appen kører lokalt og kan være uden netværk.** Et UI der henter sin JavaScript fra et CDN viser en **blank side** offline. Det er hele grunden til at Task 1 er en verifikation og ikke bare en pakkeinstallation. Antag ikke; mål det med netværket lukket.
- **Porten er tilfældig.** `TodoHost` bruger `http://127.0.0.1:0`, så frontenden skal bygge URL'en fra sin egen `location.origin` — ikke fra en hårdkodet port.
- **Kør aldrig hosten mod `%APPDATA%\TodoApp\todo.db`.** Giv altid `--Data:Path <midlertidig fil>`.
- **Dræb aldrig en `Todo.Host` du ikke selv har startet.** Under målingen til denne plan kørte der **to**: brugerens og probens. Find din på porten, ikke på navnet.
- **Kontrasten og fokus er vagtet.** `ContrastTests` fra skive 7 måler tekst i begge farvetemaer, og `FocusTests` kræver en synlig ring. Et link i `text-gray-500` ville fejle; brug de målte `text-blue-700 dark:text-blue-300` (6,82:1 og 9,80:1), som note-redigeringsknappen allerede bruger.
- **Health-linjen er én af de fire farver skive 7 rettede.** Rør ikke dens `text-gray-500 dark:text-gray-400` på selve linjen — linket er et element inde i den.
- **`ContrastTests` venter på health-linjens tekst** med `Expect(App.Health).ToContainTextAsync("API:")`. Ændrer du linjens tekststruktur, så tjek at den stadig matcher.
- **Playwright må ikke have bivirkninger uden for appen.** `/api/system/open-link` skal opsnappes med `page.RouteAsync` og afbrydes, ellers åbner hver testkørsel en rigtig browser. Det er præcis den fælde der gør denne skive testbar: opsnapningen *er* stedet man læser URL'en af.

## Bevidst uden for denne plan

Ingen omskrivning af kontrakten. Ingen forsøg på at få runtime-dokumentet til at ligne kontrakten — det er en afledning, og at fodre den med beskrivelser ville være to steder at vedligeholde det samme. Og ingen ændring af drift-testen.

---

## Task 1: Vælg UI-pakken på bevis

Den eneste rigtige ubekendte i planen. **Afgør den med en måling, ikke med en præference.**

**Files:**
- Modify: `src/Todo.Host/Todo.Host.csproj`, `src/Todo.Host/TodoHost.cs`

**Step 1: Prøv Scalar først**

`Scalar.AspNetCore` er den pakke Microsoft peger på, efter at Swashbuckle røg ud af skabelonerne. Tilføj den, og map den i `TodoHost.cs` **efter** `app.MapOpenApi()`.

**Step 2: Verificér offline — det er hele opgaven**

Start hosten headless mod en midlertidig database og en kendt port:

```
dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199 --Data:Path <midlertidig fil>
```

Hent dokumentationssiden, og **læs hvad den henter**. Konkret: hent HTML'en og se efter absolutte URL'er til andre værter — `cdn.jsdelivr.net`, `unpkg.com` og lignende.

```
curl -s http://127.0.0.1:5199/<ui-stien> | grep -oE 'src="[^"]+"|href="[^"]+"'
```

**Findes der en reference til en fremmed vært, er pakken ikke brugbar som den er.** To veje videre, i den rækkefølge:

1. Konfigurér pakken til at levere sin bundle selv, hvis den kan.
2. Ellers skift til Swashbuckles UI (`Swashbuckle.AspNetCore.SwaggerUI`), som lægger sine aktiver **inde i assemblyen** og derfor virker uden netværk. Bemærk at **kun UI-pakken** skal med — ikke Swashbuckles spec-generering, som ville lave et tredje dokument.

**Rapportér hvilken pakke du endte med, og de faktiske URL'er du så.** Det er beviset, og det er den beslutning næste læser skal kunne efterprøve.

**Step 3: Stop hosten — og find den på porten**

```
Get-NetTCPConnection -LocalPort 5199 -State Listen
```

Stop **kun** det PID. Brugeren har ofte appen åben, og et `Stop-Process -Name Todo.Host` ville tage begge.

**Step 4: Commit**

Besked: `📦 Tilføj en API-dokumentations-UI der virker uden netværk`

---

## Task 2: Servér kontrakten frem for afledningen

**Files:**
- Modify: `src/Todo.Host/Todo.Host.csproj`
- Modify: `src/Todo.Host/TodoHost.cs`
- Create: `tests/Todo.Api.Tests/ContractDocumentTests.cs`

**Step 1: Indlejr kontrakten**

`contracts/openapi.yaml` kopieres ikke til output i dag. Indlejr den, så den også findes i en publiceret exe:

```xml
  <ItemGroup>
    <EmbeddedResource Include="..\..\contracts\openapi.yaml" LogicalName="Todo.Host.openapi.yaml" />
  </ItemGroup>
```

Et fast `LogicalName` frem for det stinavns-afledte, så ressourcenavnet ikke afhænger af mappestrukturen.

**Step 2: Udstil den**

Et endpoint der læser ressourcen og svarer med den. Læg den under en sti der siger hvad den er — fx `/openapi/contract.yaml` — og sæt `Content-Type` til `application/yaml`.

Den skal **ikke** i `contracts/openapi.yaml` som en dokumenteret rute. Den er ikke en del af app-API'et; den er dokumentationen af det. Ville drift-testen fange den som en udokumenteret operation, så hold den uden for `/api/`-præfikset og bekræft at testen er ligeglad — **hvis den fejler, rapportér det frem for at ændre drift-testen.**

> **Rettelse 2026-08-17: planen tog fejl her.** Den antog, at det var **nok** at holde ruten uden for `/api/`-præfikset, altså at drift-testen var blind for alt andet. Det er den ikke. ASP.NET Core beskriver hver minimal API i `/openapi/v1.json` uanset præfiks, så `/openapi/contract.yaml` dukkede op som en 16. operation, og `ContractDriftTests` fejlede på et mismatch mellem mængderne. Rettet på endpointet med `.ExcludeFromDescription()` — ikke i drift-testen, som planen med rette forbød at røre. Kaldet er også det rigtige på sagen: ruten dokumenterer API'et frem for at være en del af det. **Enhver fremtidig rute uden for `/api/` skal have det samme kald.** Scalars egne ruter slipper kun, fordi biblioteket selv ekskluderer dem.

**Step 3: Peg UI'en på den**

Konfigurér UI'en fra Task 1 til at læse `/openapi/contract.yaml` i stedet for `/openapi/v1.json`.

**Step 4: En test på at det er kontrakten der vises**

`ContractDocumentTests.cs`, arvet fra `ApiTest`. Den skal fastslå at det udstillede dokument **er** kontrakten og ikke afledningen. De to skelnes let, for de målte forskelle er tydelige:

- titlen er `Todo API`, ikke `Todo.Host | v1`
- dokumentet indeholder `summary:`-felter, som runtime-dokumentet slet ikke har

Sammenlign hellere med `RepoPaths.ContractFile` end at hårdkode strenge: består testen, fordi den leder efter noget begge dokumenter har, beviser den ingenting.

**Step 5: Se den fejle**

Peg endpointet midlertidigt på runtime-dokumentet i stedet, kør testen, og bekræft at den fejler. **Rapportér fejlteksten.** Uden det er der intet der holder nogen fra senere at "forenkle" ved at pege UI'en på `/openapi/v1.json`.

**Step 6: Kør suiten**

```
dotnet test Todo.sln
```

Forventet: 33 Core, **110** Api (109 + denne), 22 E2E. Rapportér de faktiske tal.

**Step 7: Commit**

Besked: `📄 Udstil kontrakten selv, så dokumentationen viser prosaen`

---

## Task 3: Linket på health-linjen

**Files:**
- Modify: `src/Todo.Web/src/app/app.html`, `src/Todo.Web/src/app/app.ts`
- Modify: `src/Todo.Web/public/i18n/da.json`, `en.json`

> **Rettelse 2026-08-17: planen havde den forkerte sti.** Den skrev sprogfilerne som `src/Todo.Web/src/app/i18n/da.json`. De ligger i `src/Todo.Web/public/i18n/`; `src/app/i18n/` findes også, men indeholder Transloco-kæden i TypeScript — loader, sprogvalg og datoformatering — ikke oversættelserne.

**Step 1: Nøglen**

Linkets tekst er brugervendt og skal have en nøgle i **begge** sprogfiler, ellers fejler paritetstesten. Noget i retning af `app.apiDocs` — dansk *"API-dokumentation"*, engelsk *"API documentation"*.

**Step 2: Knappen**

Health-linjens **ok**-gren får et link ved siden af teksten. Fejlgrenen får intet: er API'et nede, svarer dokumentationen heller ikke.

Brug en `<button>`, ikke et `<a href>` — handlingen er "bed backenden åbne den i systemets browser", ikke "navigér". Et `<a href>` ville lade et midterklik navigere Photino-vinduet væk, og det er netop den fælde skive 4 fandt.

```html
    <p data-testid="health" class="mt-8 text-xs text-gray-500 dark:text-gray-400">
      {{ 'app.health' | transloco: { status: s.status, version: s.version } }}
      <button
        type="button"
        data-testid="api-docs"
        class="ml-2 text-blue-700 underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:text-blue-300 dark:focus-visible:outline-blue-400"
        (click)="openApiDocs()"
      >
        {{ 'app.apiDocs' | transloco }}
      </button>
    </p>
```

Farverne er målte: `text-blue-700` er 6,82:1 på hvid, `dark:text-blue-300` 9,80:1 på `gray-900`. Fokusringen er den samme som skive 7 lagde, og `FocusTests`-mønstret dækker formen.

**Step 3: Handlingen**

I `app.ts`. URL'en bygges fra appens egen origin, fordi porten er tilfældig:

```ts
  protected openApiDocs(): void {
    this.system.openLink(`${location.origin}/<ui-stien fra Task 1>`).catch(() => {});
  }
```

`SystemStore.openLink` findes fra skive 4 og sætter selv en fejlbesked, hvis kaldet afvises — derfor `.catch(() => {})` som resten af appen.

**Step 4: Byg og kør vagterne**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet test tests/Todo.E2E/Todo.E2E.csproj
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

Forventet: 22 E2E og 139 Vitest, alle grønne. `ContrastTests` måler den nye knap i begge temaer — den er dækket af de målte farver, men bekræft det.

**Bemærk:** `ContrastTests` venter på `App.Health` med `ToContainTextAsync("API:")`. Den skal stadig matche. Og `app.spec.ts` har tests på health-linjen — hvis en af dem knækker, rapportér før du retter.

**Step 5: Commit**

Besked: `🔗 Gør API-statuslinjen til en vej til dokumentationen`

---

## Task 4: E2E og vagten

**Files:**
- Create: `tests/Todo.E2E/ApiDocsJourneyTests.cs`

**Step 1: To tests, der fastslår hver sin halvdel**

**Test A — klikket beder om den rigtige URL.** Opsnap `/api/system/open-link` med `page.RouteAsync` og afbryd den; læs URL'en af requestens body. Fastslå at den peger på dokumentationsstien på appens egen origin.

Opsnapningen er ikke kun for at læse URL'en af — **uden den åbner hver testkørsel en rigtig browser.** `MarkdownNoteJourneyTests` gør det allerede for note-links; genbrug mønsteret derfra.

**Test B — dokumentationssiden virker uden netværk.** Naviger Playwright direkte til dokumentationsstien, med **alle eksterne requests blokeret** (`page.RouteAsync("**://**", …)` der afbryder alt der ikke er appens egen origin), og fastslå at UI'en faktisk renderede — ikke bare at siden svarede 200. Find noget UI'en tegner ud af dokumentet, fx en operations-sti eller titlen `Todo API`.

Test B er den der beviser Task 1's pakkevalg. Den er også den eneste ting der fanger, hvis pakken senere opgraderes til en version der henter fra et CDN.

**Step 2: Se dem fejle**

Begge, med fejltekst rapporteret:

1. Ændr URL'en i `openApiDocs()` til noget forkert. Test A skal fejle på den URL den læste af.
2. Bloker også appens **egen** origin i Test B. Den skal fejle, fordi UI'en ikke kan tegne noget — det bekræfter at testen faktisk læser indhold og ikke bare en statuskode.

> **Rettelse 2026-08-17: bruddet i punkt 2 beviser ingenting.** Blokeres appens egen origin, dør `GotoAsync` med `net::ERR_FAILED`, før nogen assertion overhovedet kører — testen fejler på navigationen, og en test der kun så på en statuskode ville fejle på præcis samme måde. Bruddet der faktisk viser, at testen læser indhold, er at **lade dokumentet igennem og blokere bundlen**: siden svarer 200, og testen fejler på den manglende renderede titel. Det er det brud der blev brugt.

**Step 3: Kør alt**

Forventet: 33 Core, **111** Api, **24** E2E (22 + 2), 139 Vitest. Rapportér de faktiske tal.

> **Rettelse 2026-08-17:** planen forudsagde **110** Api her og i Task 5. Det blev **111**, fordi `ContractDocumentTests` endte med to tests og ikke én — "er det kontrakten der vises" og "er afledningen genuint ikke en erstatning" er to påstande. Tallet i `CLAUDE.md` er det målte.

**Step 4: Commit**

Besked: `✅ E2E på at dokumentationen åbnes rigtigt og virker offline`

---

## Task 5: Dokumentation

**Files:**
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: `HANDOFF.md`**

Fjern **Swagger-link på health-linjen** fra "Kan ligge stille" og tilføj den til **"Uden for skiverne"**-linjen sammen med app-ikonet og launcheren. Ret samtidig den forvirrende formulering *"tilføj `http`/`https` er nok, de er hvidlistet"* — de **var** hvidlistet hele tiden, og der var ingenting at tilføje.

**Ingen skive omnummereres**, og Færdigt-tabellen får ingen ny række: dette er ikke en skive.

**Step 2: Designdokumentet**

Afsnit 10 skal have det punkt, der ellers bliver genopdaget: **appen udstiller to OpenAPI-dokumenter med hver sin rolle.** `/openapi/v1.json` er runtime-afledningen, som `ContractDriftTests` måler imod; `/openapi/contract.yaml` er kontrakten selv, som UI'en viser. De er ikke en dublet der skal ryddes op — afledningen har **nul** beskrivelser mod kontraktens 29, og drift-testen læser netop afledningen. Fjernes `MapOpenApi()`, fejler drift-testen.

Tilføj også, hvis Task 1 endte med Swashbuckle frem for Scalar, **hvorfor**: at pakken skal levere sine aktiver selv, fordi appen kører lokalt og kan være offline.

**Step 3: `CLAUDE.md`**

Under Konventioner:

- **Et lokalt UI må ikke hente sine aktiver fra et CDN.** Appen kører på maskinen og kan være uden netværk; en side der henter sin JavaScript udefra er blank offline. Vagten er `ApiDocsJourneyTests`, som blokerer alt uden for appens egen origin.
- **Appen har to OpenAPI-dokumenter, og det er med vilje** — se designdokumentets afsnit 10.

Under "Maskinen", hvis det ikke allerede står der: **find din egen `Todo.Host` på porten, ikke på navnet.** Under målingen til denne plan kørte der to processer, og et `Stop-Process -Name Todo.Host` ville have lukket brugerens app.

**Step 4: Testtal**

Opdatér til de tal du **målte**. Forventet: 33 Core, **111** Api, 24 E2E, 139 Vitest — se rettelsen under Task 4 Step 3. Skriv de faktiske, og behold sætningen om at et ændret tal betyder en tabt eller duplikeret test.

**Step 5: Commit**

Besked: `📝 Skriv ned hvorfor appen har to OpenAPI-dokumenter`

---

## Færdig når

- Et klik på dokumentationslinket i health-linjen beder backenden åbne dokumentationen i systemets browser — bevist af en test der opsnapper kaldet.
- Dokumentationssiden **renderer med alle eksterne requests blokeret**, og den test er set fejle, når appens egen origin også blokeres.
- UI'en viser **kontrakten** — titel `Todo API`, med `summary`-felter — og den test er set fejle med runtime-dokumentet i stedet.
- `MapOpenApi()` står stadig, og `ContractDriftTests` er grøn.
- Kontrakten er indlejret i assemblyen, så den også findes uden repo-mappen.
- `ContrastTests` og `FocusTests` er grønne: den nye knap holder AA i begge temaer og har en synlig fokusring.
- Ingen skive er omnummereret, og Færdigt-tabellen har ingen ny række.
- Testtallene er skrevet ned som målt.

## Til næste gang

Tilbage står `long` som id — planen ligger færdig og målt i `docs/plans/2026-08-17-long-ids.md`, og den er det eneste punkt der bliver dyrere af at vente. Derudover revisionsloggen, "Sådan er den tænkt"-siden, og de eksterne kilder fra skive 9 og frem, hvor **ADO-mentions stadig er den mest usikre antagelse i hele designet** og bør verificeres mod en rigtig instans, før der bygges ovenpå.
