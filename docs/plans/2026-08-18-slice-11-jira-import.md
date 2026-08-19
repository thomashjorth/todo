# Skive 11 — Jira-import Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Hent dine ulukkede Jira-sager fra projekt `SAAS` ind som opgaver, med forhåndsvisning,
dedup, og en indstilling der siger hvilke Jira-statusser betyder "venter på".

**Architecture:** `ITaskSource` i `Todo.Core` er sømmen; `JiraTaskSource` er den eneste
implementation der findes efter denne skive. Forhåndsvisning og import følger skive 2's mønster
nøjagtigt — `POST /api/jira/preview` kalder Jira og returnerer rækker, `POST /api/jira/import`
skriver de rækker klienten sender tilbage, og dedup sker på `SourceId` + `ExternalKey`. Wiki-markup
konverteres til CommonMark af en ren funktion i `Todo.Core`. **Ingen migrering:** `SourceId` og
`ExternalKey` findes fra skive 2, indstillinger er nøgle/værdi-rækker, og `externalUrl` **beregnes**
frem for at gemmes.

**Tech Stack:** ASP.NET Core minimal APIs, `IHttpClientFactory`, EF Core 10 / SQLite, Angular 22
signal-stores, xunit.v3, Playwright, Vitest.

---

## Afgrænsning — hvad skiven gør, og hvad den bevidst ikke gør

Designdokumentets afsnit 9 skriver tre ting på skive 11: `ITaskSource`, **afstemning**, og
**lokale felter der overlever sync**. De to sidste flyttes til skive 14 (baggrundssync), og det er
en bevidst ændring frem for en forglemmelse:

**`TitleOverridden`, `ExtTitle`, `ExtStatus`, `LastSyncedAt`, `DetachedAt` bygges ikke her.** De
beskytter en lokal rettelse mod at blive overskrevet af en **senere** sync. Så længe der kun findes
en manuel engangsimport, kan det scenarie ikke opstå — felterne ville blive skrevet og aldrig læst,
og en vagt på dem kunne ikke bringes til at fejle. Det er præcis den fælde `CLAUDE.md` navngiver
under Testdisciplin: *"Pas på assertions der ikke kan fejle."* Byg dem sammen med det der gør dem
nødvendige.

**`ExternalUrl` bliver ikke en kolonne.** `SourceId` er `jira` og `ExternalKey` er `SAAS-123`, så
URL'en er `{basisURL}/browse/{nøgle}` og kan **beregnes** i svaret. Gemt ville den blive forkert
den dag basisURL'en ændres. Skiven har derfor **ingen migrering overhovedet** — det er et
sundhedstegn for afgrænsningen, ikke et hul.

**`ICredentialStore` oprettes ikke.** Afsnit 6 nævner den, men afsnit 3 har allerede besluttet, at
tokens ligger i klartekst i `Setting`-tabellen. Et interface med én implementation, én forbruger og
ingen alternativ lagring er en abstraktion uden et andet tilfælde at retfærdiggøre den. Den hører
til den dag lagringen skal skiftes (DPAPI, Credential Manager). **Opdatér afsnit 6** i Task 10.

**ADO-mentions verificeres ikke her.** Afsnit 10 siger "verificér i skive 11". Det er en **måling
mod brugerens egen ADO-instans**, ikke kode, og den kan ikke udføres af en subagent. Den står som
en opgave til brugeren i Task 10's dokumentation, ikke som en kodeopgave.

## Beslutninger truffet før planen, så de ikke gættes undervejs

**Kun projekt `SAAS` — og en tom projektnøgle må ikke betyde "alle projekter".** Brugerens krav
2026-08-18. PAT'en ser fire projekter (`EC`, `KK`, `SAAS`, `TTMBP`), så en JQL uden projektled
trækker fra dem alle, kundeprojektet iberegnet. Uden en projektnøgle svarer importen derfor **400**
frem for at hente bredt. Det er en vagt, ikke en bekvemmelighed: det stille tilbagefald til "alt"
er den fejl kravet handler om.

**To indstillinger til ventende statusser, hver med én opgave.**
`jira.waitingStatuses` er listen af statusnavne der **betyder** ventende (brugeren vælger fra
instansen). `jira.includeWaiting` er om sager i de statusser **kommer med** — default `false`.
Delt op, fordi de svarer på hver sit spørgsmål, og fordi listen så kan defineres uafhængigt af om
man vil hente dem i dag. En sag hvis status står i listen importeres som `WaitingFor`; alle andre
som `Open`.

**En udelukket ventende sag vises, den skjules ikke.** Er `includeWaiting` slået fra, dukker sagen
op i forhåndsvisningen som *"udeladt: ventende status"* og er slået fra — samme greb som skive 2's
"importeret tidligere". Skjult ville den se ud som en manglende sag, og den ville gøre
`includeWaiting` usynlig i UI'et.

**Tokenet forlader aldrig serveren.** `GET /api/settings` svarer `hasJiraToken: boolean`, aldrig
tokenet. Tokenet skrives gennem **sit eget endpoint** — `PUT /api/settings/jira-token` og
`DELETE /api/settings/jira-token` — netop fordi appens konvention er at *et fraværende felt
betyder ryd*: lå tokenet på `PUT /api/settings`, ville en ændring af sproget slette det. Samme
fejl som `DeferUntil` i skive 9, og den er kendt.

**`WaitingSince` kommer fra changeloggen pr. sag, ikke fra søgningen.** Målt 2026-08-18:
`statuscategorychangedate` returneres ikke af DC 10.3.24, og `expand=changelog` virker. Hvorvidt
`expand=changelog` også virker på `/search`, er **ikke målt** — derfor henter planen changeloggen
med `GET /rest/api/2/issue/{key}?expand=changelog` og **kun for sager i en ventende status**, hvor
`WaitingSince` faktisk skal bruges. `total` var 10, så det er nogle få kald. Virker `expand` på
`/search`, er det en optimering til senere, ikke en forudsætning nu.

**Ingen test må røre den rigtige instans.** To lag, og de dækker forskellige fejl. Api-testene
starter en **falsk Jira** på loopback og peger basisURL-indstillingen derhen, så den rigtige
HttpClient-vej og JSON-læsningen bliver kørt. Og en vagt kræver, at instansens værtsnavn ikke står
nogetsteds i repoet — målt 2026-08-18: nul filer indeholder det i dag, så vagten er grøn og kan
ses fejle ved at skrive det ind.

---

## Task 1: Kontrakten

**Files:**
- Modify: `contracts/openapi.yaml`
- Generated (kør scriptet, commit resultatet): `src/Todo.Web/src/app/api/todo-client.ts`

**Step 1: Udvid `SettingsResponse` og `SettingsRequest`**

I `components.schemas.SettingsResponse`, læg til (bevar `language`):

```yaml
        jiraBaseUrl:
          type: string
          nullable: true
        jiraProjectKey:
          type: string
          nullable: true
        jiraWaitingStatuses:
          type: array
          items:
            type: string
        jiraIncludeWaiting:
          type: boolean
        hasJiraToken:
          type: boolean
          description: >-
            Whether a token is stored. The token itself is never returned; it is written
            through PUT /api/settings/jira-token and cleared through DELETE.
```

Samme felter på `SettingsRequest` **bortset fra `hasJiraToken`** — den er kun et svar.
`jiraIncludeWaiting` er `type: boolean` uden `nullable`, fordi den altid har en værdi.

**Step 2: Tokenets to endpoints**

```yaml
  /api/settings/jira-token:
    put:
      operationId: setJiraToken
      tags:
        - Settings
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/JiraTokenRequest'
      responses:
        '200':
          description: The token was stored.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SettingsResponse'
        '400':
          description: The token was empty.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ApiError'
    delete:
      operationId: clearJiraToken
      tags:
        - Settings
      responses:
        '200':
          description: The token was removed.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SettingsResponse'
```

`JiraTokenRequest` er `{ token: string }`, required.

**Step 3: Jiras fire endpoints**

Alle fire har `tags: [Jira]`. `400` peger på `ApiError` overalt.

| Rute | Metode | operationId | Svar |
| --- | --- | --- | --- |
| `/api/jira/test` | POST | `testJiraConnection` | `JiraConnectionResponse` |
| `/api/jira/statuses` | GET | `listJiraStatuses` | `JiraStatusesResponse` |
| `/api/jira/preview` | POST | `previewJira` | `JiraPreviewResponse` |
| `/api/jira/import` | POST | `importJira` | `JiraImportResponse`, krop `JiraImportRequest` |

`/api/jira/test` og `/api/jira/preview` er POST, fordi de er **handlinger** man beder om og
forventer at kunne gentage — ikke ressourcer man læser. `/api/jira/statuses` er en GET, fordi en
liste af statusnavne netop *er* en ressource, og den må gerne komme fra en cache.

**Bemærk hvad begrundelsen ikke er.** Planens første udgave skrev "POST fordi den kalder ud på
netværket", og den regel forbyder `/api/jira/statuses` som GET tre operationer længere ned — den
kalder også ud. Fanget i kvalitetsreviewet af Task 1. Skellet er handling mod ressource, ikke
netværk mod ikke-netværk. Begrundelsen hører i operationens `description`, ikke i dens `summary`,
som er **titlen** på `/scalar/`.

**Step 4: Skemaerne**

```yaml
    JiraConnectionResponse:
      type: object
      required: [displayName]
      properties:
        displayName:
          type: string
    JiraStatusesResponse:
      type: object
      required: [names]
      properties:
        names:
          type: array
          items:
            type: string
    JiraPreviewResponse:
      type: object
      required: [rows, total]
      properties:
        rows:
          type: array
          items:
            $ref: '#/components/schemas/JiraPreviewRow'
        total:
          type: integer
          format: int32
          description: What Jira reported as the total, so a truncated page is visible.
    JiraPreviewRow:
      type: object
      required: [key, title, status, isWaiting, alreadyImported]
      properties:
        key:
          type: string
        title:
          type: string
        note:
          type: string
          nullable: true
          description: The Jira description, converted from wiki markup to CommonMark.
        deadline:
          type: string
          format: date
          nullable: true
        requester:
          type: string
          nullable: true
        status:
          type: string
          description: The Jira status name, shown so the user can see why a row is waiting.
        isWaiting:
          type: boolean
          description: Whether the status is in the user's waiting list.
        waitingSince:
          type: string
          format: date-time
          nullable: true
        alreadyImported:
          type: boolean
        excluded:
          type: string
          nullable: true
          description: >-
            Why import will skip this row, as an error code the frontend translates.
            Null means it will be imported.
    JiraImportRequest:
      type: object
      required: [rows]
      properties:
        rows:
          type: array
          items:
            $ref: '#/components/schemas/JiraImportRow'
    JiraImportRow:
      type: object
      required: [key, title]
      properties:
        key:
          type: string
        title:
          type: string
        note:
          type: string
          nullable: true
        deadline:
          type: string
          format: date
          nullable: true
        requester:
          type: string
          nullable: true
        isWaiting:
          type: boolean
        waitingSince:
          type: string
          format: date-time
          nullable: true
    JiraImportResponse:
      type: object
      required: [imported, skipped]
      properties:
        imported:
          type: integer
          format: int32
        skipped:
          type: integer
          format: int32
```

**Step 5: `externalUrl` på `TodoTask`**

Læg til på `TodoTask`-skemaet:

```yaml
        externalUrl:
          type: string
          nullable: true
          description: >-
            Where the source system shows this item. Computed from the source and the
            external key, never stored, so it follows a changed base URL.
```

**Step 6: Generér klienten**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

Forventet: `src/Todo.Web/src/app/api/todo-client.ts` skrives om, og der findes nu en `JiraClient`
plus `setJiraToken`/`clearJiraToken` på `SettingsClient`.

**Step 7: Se friskheds-testen bekræfte at kontrakt og kode hænger sammen**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~GeneratedCodeFreshnessTests"
```

Forventet: PASS. Fejler den, blev genereringen ikke committet.

`ContractDriftTests` **vil fejle** her, og det er rigtigt: kontrakten har seks operationer som
endpoints ikke har endnu. Den er porten i Task 6, ikke her.

**Step 8: Commit**

`scripts\generate-api.ps1` skriver **fire** filer, ikke to — målt 2026-08-18, efter at planens
første udgave nævnte to. `src/Todo.Contracts/Generated/.source-hash` er præcis den fil
`GeneratedCodeFreshnessTests` læser, og `Contracts.g.cs` er de C#-DTO'er Task 3 og Task 6 skal
bruge. Udelades de, er commit'en **grøn lokalt og rød hos alle andre**, og de to senere tasks
starter uden deres typer.

```bash
git add contracts/openapi.yaml src/Todo.Web/src/app/api/ src/Todo.Contracts/Generated/
git commit -m "📝 Læg Jira-importen og dens indstillinger på kontrakten"
```

---

## Task 2: Wiki-markup → CommonMark

Den største enkeltforskel i importarbejdet (afsnit 10). REST v2 giver `description` som Jiras
wiki-markup, og noterne er CommonMark.

**Files:**
- Create: `src/Todo.Core/Jira/WikiMarkup.cs`
- Test: `tests/Todo.Core.Tests/Jira/WikiMarkupTests.cs`

> **Rettet efter kørslen, 2026-08-18. Den leverede kode i `3a30cbf` er sandheden, ikke listingen
> nedenfor.** Fem ting var forkerte, og fire af dem blev først fundet ved at måle konverterens
> **output gennem appens egen `marked`** frem for at ræsonnere om regexerne. Det er lektionen:
> konverteringen er kun halvdelen af kæden.
>
> 1. **`----` blev oversat til `---`, som er en setext-overskrift.** `tekst\n---\nmere` renderes som
>    `<h2>tekst</h2>` — stregen forsvinder, og afsnittet over bliver en overskrift. Vi *skabte*
>    fejlen frem for blot at undlade at konvertere. Rettet til `***`, som ikke kan læses sådan.
> 2. **`^#[ \t]+` og `^\*[ \t]+` matchede kun én markør.** Jiras indlejrede punkter er `##`, `###`,
>    `**` og `#*`, så `## en-a` passerede urørt og blev en `<h2>`. En treniveaus-liste blev en
>    liste, to overskrifter og en liste til. Erstattet af én dybdebevidst `ListItem()` med
>    `^(?<marks>[#*]+)[ \t]+` og **fire** mellemrums indrykning pr. niveau — målt: to mellemrum
>    nester ikke under en `1. `-markør.
> 3. **`{noformat}` blev ikke beskyttet.** Det er Jiras *anden* ordrette blok, så en bullet derinde
>    blev en bullet — netop det klassens egen doc-kommentar lover ikke sker.
> 4. **`CodeBlock`, `NoFormat` og `InlineCode` er kvadratiske når de er uafsluttede.** Ikke
>    katastrofalt backtrackende — der er ingen nøstede kvantorer — men 100 KB bare `{{` koster
>    **288 ms** mod under 1 ms med `RegexOptions.NonBacktracking`. Sat på de tre. At det *kan*
>    sættes på præcis dem er ikke tilfældigt: `NonBacktracking` forbyder lookaround, og kun
>    `Bold`/`Emphasis` bruger det.
> 5. **Testfilen hørte i `Jira/`-mappen.** Hver anden test i projektet ligger i en feature-mappe
>    med matchende namespace; denne var den eneste undtagelse.
>
> Endeligt antal: **21** i `WikiMarkupTests`, **59** i `Todo.Core.Tests`.

**Step 1: Skriv den fejlende test — kollisionen først**

Den farligste fejl er ikke en manglende regel, det er en **stille betydningsændring**: i
wiki-markup er `*x*` **fed**, i markdown er det **kursiv**. En gennemgang der lader teksten
passere uændret, degraderer fed til kursiv uden at nogen kan se det.

```csharp
using Todo.Core.Jira;

namespace Todo.Core.Tests;

public class WikiMarkupTests
{
    /// <summary>
    /// The reason this converter exists at all. Jira's `*x*` is bold; CommonMark's is emphasis.
    /// A passthrough silently demotes every bold word in every imported description, and nothing
    /// downstream can tell. This is the assertion that fails if someone "simplifies" the converter
    /// into a no-op.
    /// </summary>
    [Fact]
    public void Bold_stays_bold_rather_than_becoming_emphasis()
    {
        Assert.Equal("**vigtigt**", WikiMarkup.ToCommonMark("*vigtigt*"));
    }

    [Fact]
    public void Emphasis_uses_the_markdown_spelling()
    {
        Assert.Equal("*måske*", WikiMarkup.ToCommonMark("_måske_"));
    }

    [Theory]
    [InlineData("h1. Overskrift", "# Overskrift")]
    [InlineData("h3. Mindre", "### Mindre")]
    [InlineData("h6. Mindst", "###### Mindst")]
    public void A_heading_becomes_hashes(string wiki, string expected)
    {
        Assert.Equal(expected, WikiMarkup.ToCommonMark(wiki));
    }

    [Fact]
    public void Inline_code_becomes_backticks()
    {
        Assert.Equal("Kald `Foo()`", WikiMarkup.ToCommonMark("Kald {{Foo()}}"));
    }

    [Fact]
    public void A_code_block_becomes_a_fence()
    {
        Assert.Equal(
            "```java\nint x = 1;\n```",
            WikiMarkup.ToCommonMark("{code:java}\nint x = 1;\n{code}"));
    }

    [Fact]
    public void A_code_block_without_a_language_still_fences()
    {
        Assert.Equal("```\nrå\n```", WikiMarkup.ToCommonMark("{code}\nrå\n{code}"));
    }

    [Fact]
    public void A_named_link_becomes_a_markdown_link()
    {
        Assert.Equal(
            "[boardet](https://example.test/b)",
            WikiMarkup.ToCommonMark("[boardet|https://example.test/b]"));
    }

    [Fact]
    public void A_bare_link_becomes_an_autolink()
    {
        Assert.Equal("<https://example.test/b>", WikiMarkup.ToCommonMark("[https://example.test/b]"));
    }

    [Fact]
    public void A_numbered_list_becomes_an_ordered_list()
    {
        Assert.Equal("1. en\n1. to", WikiMarkup.ToCommonMark("# en\n# to"));
    }

    [Fact]
    public void A_bullet_list_keeps_its_dashes()
    {
        Assert.Equal("- en\n- to", WikiMarkup.ToCommonMark("* en\n* to"));
    }

    [Fact]
    public void A_quote_becomes_a_blockquote()
    {
        Assert.Equal("> sagt", WikiMarkup.ToCommonMark("bq. sagt"));
    }

    [Fact]
    public void A_rule_becomes_three_dashes()
    {
        Assert.Equal("---", WikiMarkup.ToCommonMark("----"));
    }

    /// <summary>
    /// The converter covers a subset, and the subset is documented. What matters is which way it
    /// fails on the rest: an unknown macro is left as literal text rather than dropped. Dropping
    /// is worse than showing `{color}`, because nobody can tell that something went missing.
    /// </summary>
    [Fact]
    public void An_unknown_macro_survives_as_text()
    {
        Assert.Equal("{color:red}rød{color}", WikiMarkup.ToCommonMark("{color:red}rød{color}"));
    }

    [Fact]
    public void An_empty_description_is_null_rather_than_an_empty_note()
    {
        Assert.Null(WikiMarkup.ToCommonMark(null));
        Assert.Null(WikiMarkup.ToCommonMark("   "));
    }

    /// <summary>
    /// A bullet line inside a code fence is not a bullet. The line-based rules must not run
    /// inside a fence, and this is the only test that can tell.
    /// </summary>
    [Fact]
    public void The_line_rules_do_not_run_inside_a_code_block()
    {
        Assert.Equal(
            "```\n* ikke en liste\nh1. ikke en overskrift\n```",
            WikiMarkup.ToCommonMark("{code}\n* ikke en liste\nh1. ikke en overskrift\n{code}"));
    }

    /// <summary>Bold inside inline code is code, not bold. Same argument as the fence, one line up.</summary>
    [Fact]
    public void Inline_code_keeps_its_asterisks()
    {
        Assert.Equal("`a * b`", WikiMarkup.ToCommonMark("{{a * b}}"));
    }
}
```

**Step 2: Kør den og se den fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~WikiMarkupTests"
```

Forventet: alle fejler på at `Todo.Core.Jira.WikiMarkup` ikke findes (CS0234 eller CS0246).

**Step 3: Implementér**

Rækkefølgen i metoden er bærende: kodeblokke og inline-kode skal **tages ud af vejen først**,
ellers omskriver linjereglerne indeni dem. Læg dem til side som pladsholdere, kør reglerne, sæt
dem tilbage.

```csharp
using System.Text.RegularExpressions;

namespace Todo.Core.Jira;

/// <summary>
/// Converts the subset of Jira's wiki markup that appears in practice into the CommonMark the
/// notes are written in. Self-hosted Jira serves REST v2, where a description is wiki markup —
/// not Cloud's Atlassian Document Format; see the design document's section 10.
///
/// Two rules decide the shape of this class. Code is protected before anything else runs, because
/// a bullet inside a fence is not a bullet. And an unrecognised macro is left alone rather than
/// dropped: showing `{color}` is a visible imperfection, while dropping the text it wraps is an
/// invisible loss.
/// </summary>
public static partial class WikiMarkup
{
    // A sentinel that cannot occur in Jira text and survives the regexes below untouched.
    private const string Fence = "";

    public static string? ToCommonMark(string? wiki)
    {
        if (string.IsNullOrWhiteSpace(wiki))
        {
            return null;
        }

        var protectedBlocks = new List<string>();
        var text = wiki.Replace("\r\n", "\n").Replace('\r', '\n');

        // Fences before inline code: {{a}} inside a {code} block must not be pulled out
        // separately, and a fence is the outer of the two.
        text = CodeBlock().Replace(text, match => Protect(
            protectedBlocks,
            $"```{match.Groups["lang"].Value}\n{match.Groups["body"].Value.Trim('\n')}\n```"));

        text = InlineCode().Replace(
            text, match => Protect(protectedBlocks, $"`{match.Groups["body"].Value}`"));

        text = Quote().Replace(text, "> ");
        text = Rule().Replace(text, "***");   // ikke "---": det er en setext-overskrift

        // Lists before headings, and this order is load-bearing. `Heading()` turns `h1. X` into
        // the line `# X`, and NumberedItem's `^#[ \t]+` would then read that as a Jira ordered
        // item and emit `1. X`. Only `h1` breaks — `h2.` and up put a second `#` where the rule
        // wants a space — so the wrong order fails one heading in three and looks like a regex bug.
        text = NumberedItem().Replace(text, "1. ");
        text = BulletItem().Replace(text, "- ");

        text = Heading().Replace(text, match =>
            new string('#', int.Parse(match.Groups["level"].ValueSpan))
                + " "
                + match.Groups["text"].Value);

        text = NamedLink().Replace(text, "[${text}](${url})");
        text = BareLink().Replace(text, "<${url}>");

        // Bold before emphasis: `*x*` must become `**x**` before `_x_` becomes `*x*`, or the
        // second rule would see the asterisks the first one just wrote.
        text = Bold().Replace(text, "**${text}**");
        text = Emphasis().Replace(text, "*${text}*");

        // Backwards, so a placeholder nested inside a restored block is still replaced.
        for (var i = protectedBlocks.Count - 1; i >= 0; i--)
        {
            text = text.Replace($"{Fence}{i}{Fence}", protectedBlocks[i]);
        }

        return text;
    }

    private static string Protect(List<string> blocks, string value)
    {
        blocks.Add(value);

        return $"{Fence}{blocks.Count - 1}{Fence}";
    }

    [GeneratedRegex(@"\{code(?::(?<lang>[^}]*))?\}(?<body>.*?)\{code\}", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\{\{(?<body>.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^h(?<level>[1-6])\.[ \t]*(?<text>.*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^bq\.[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^-{4,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    [GeneratedRegex(@"^#[ \t]+", RegexOptions.Multiline)]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"^\*[ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletItem();

    [GeneratedRegex(@"\[(?<text>[^\]|]+)\|(?<url>[^\]]+)\]")]
    private static partial Regex NamedLink();

    [GeneratedRegex(@"\[(?<url>(?:https?|mailto):[^\]|]+)\]")]
    private static partial Regex BareLink();

    // Anchored on non-space so `a * b` is not read as an unterminated bold run.
    [GeneratedRegex(@"(?<![\w*])\*(?<text>[^*\n]*[^\s*])\*(?![\w*])")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w_])_(?<text>[^_\n]*[^\s_])_(?![\w_])")]
    private static partial Regex Emphasis();
}
```

**Step 4: Kør testene**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~WikiMarkupTests"
```

Forventet: PASS, 21 tests (de tre `[InlineData]` tæller hver for sig).

**Målt 2026-08-18: regexernes mønstre holdt ordret. Rækkefølgen og kompositionen gjorde ikke.**
Planens første udgave lod `Heading()` køre før listereglen, og det er rettet i koden ovenfor.

Fejlen er værd at kende for **hvor smalt den skjulte sig i den oprindelige udgave**: med de to
enkeltmarkør-regler ramte den **kun `h1`** — `h2.` og opefter sætter et andet `#` hvor `^#[ \t]+`
vil have et mellemrum — så **17 af 18 tests bestod**, og kun én af de tre `[InlineData]`-rækker
fangede den. Havde planen haft ét overskriftseksempel frem for tre, var den sluppet gennem hele
skiven. Det er argumentet for at et `[Theory]` skal dække **grænserne** frem for et pænt midtpunkt.

**Bemærk at rettelsen af B2 gjorde rækkefølgen mere bærende, ikke mindre.** Med den dybdebevidste
`ListItem()` fælder en ombytning **alle seks** niveauer, ikke bare `h1`: `h2.` bliver
`    1. Mindre`, og `h6.` bliver fem niveauers indrykning. Vagten dækker altså mere efter
rettelsen end før — men det er også en påstand der ændrede sig undervejs, og den slags skal måles
igen frem for at blive genciteret.

Fejler noget alligevel: **ret ikke testen først.** Ret regexen eller rækkefølgen. Viser en
forventning sig at være forkert om Jiras egen syntaks, så skriv **hvorfor** i en kommentar frem for
at slette påstanden.

**Step 5: Commit**

```bash
git add src/Todo.Core/Jira/ tests/Todo.Core.Tests/WikiMarkupTests.cs
git commit -m "✨ Konvertér Jiras wiki-markup til CommonMark, så fed bliver fed"
```

---

## Task 3: Indstillingerne i backenden, og tokenet der ikke må slippe ud

**Files:**
- Create: `src/Todo.Core/Jira/JiraSettings.cs`
- Create: `src/Todo.Core/Jira/JiraSettingsReader.cs`
- Modify: `src/Todo.Core/Settings/SettingKeys.cs`
- Modify: `src/Todo.Core/Errors/ErrorCodes.cs`
- Modify: `src/Todo.Host/Endpoints/SettingsEndpoints.cs`
- Modify: `src/Todo.Host/TodoHost.cs` (registrering)
- Test: `tests/Todo.Api.Tests/JiraSettingsEndpointsTests.cs`

> **Rettet efter kørslen, 2026-08-18. Leveret i `aa1bbc3` + `b88af69`; den kode er sandheden.**
> Fem ting var forkerte, og to af dem ville have kostet noget:
>
> 1. **`jiraIncludeWaiting` må ikke gemmes som `"false"`.** To eksisterende tests —
>    `Clearing_the_language_removes_the_row_rather_than_storing_null` og
>    `Choosing_a_language_twice_overwrites_the_one_row` — påstår `Assert.Empty`/`Assert.Single` på
>    hele `Settings`-tabellen efter et PUT hvor feltet defaulter til `false`. En literal
>    `"false"`-række gør tabellen ikke-tom, og begge bliver røde. Gem
>    `request.JiraIncludeWaiting ? "true" : null`, så *slået fra* fjerner rækken. Adfærden er
>    uændret, fordi læseren allerede læser fravær som fra (`Value(...) == "true"`).
> 2. **`partial` fletter ikke på tværs af assemblies.** Step 7's forslag om en midlertidig
>    `partial class SettingsResponse` i `Todo.Host` eller testprojektet laver en **anden** type der
>    skygger for den importerede: fem gange `CS0436` og derefter `CS0117` på hvert felt. Prøven
>    skal ligge **inde i `Todo.Contracts`**, eller man redigerer `Contracts.g.cs` og regenererer.
> 3. **Step 2 giver 405, ikke 404.** `MapFallbackToFile("index.html")` matcher kun GET, så et uroutet
>    `PUT`/`DELETE` under et eksisterende præfiks svarer `405 Method Not Allowed`. Samme signal,
>    andet tal — men den der jagter en 404 tror at ruten halvt fandtes.
> 4. **Lækagevagten kunne bestå uden at bevise noget**, og det er rettet i testen nedenfor. Den
>    gemte et token og påstod at strengen ikke kom tilbage, **uden at tjekke at gemningen lykkedes**.
>    Begyndte ruten at svare 400, ville den lede efter en streng der ikke var i systemet. Målt, ikke
>    ræsonneret: den gamle form blev kørt mod en rute der altid svarede 400 og var **grøn**.
> 5. **`/openapi/v1.json` kan ikke fange en lækage i svarbyggeren** — det afledte dokument bærer
>    skema, ikke værdier, og assertionen kastede i øvrigt på første iteration, så den anden hentning
>    aldrig skete. Den er nu sin egen test med den rigtige begrundelse: et rigtigt token pastet ind
>    i et `example:`. Bemærk at dens `PUT` er **dekorativ** og ikke en forudsætning — et
>    `example:`-token ligger i dokumentet uanset om noget blev gemt. Læg ikke et
>    `EnsureSuccessStatusCode` der og tro at det strammede noget.
>
> Og fire ting mere, fundet ved at **mutere koden** frem for at læse den (`16e1014`):
>
> 6. **`The_openapi_document_carries_no_token` kunne slet ikke fejle.** Den søgte kun efter sin egen
>    konstant, som aldrig kan optræde i et skemaafledt dokument. Målt: et `example:` med en
>    PAT-lignende streng på `JiraTokenRequest.token` gjorde intet — testen bestod. Omskrevet til
>    `No_token_property_in_either_document_carries_a_value`, som fejer **begge** dokumenter for
>    properties med `token` i navnet og påstår at ingen har `example` eller `default`, plus at hver
>    af dem er `boolean` og ikke `string`. Bemærk at **påstanden om at `SettingsResponse` slet ikke
>    må have en token-navngivet property er umulig** — `hasJiraToken` *er* en, og den er hele
>    designet. Typen er det rigtige kriterium, ikke navnet. Og `Assert.NotEmpty` skal med, ellers
>    består løkken på at finde ingenting.
> 7. **At slå ventende fra igen var helt utestet.** Fjernes grenen der rydder rækken, består **alle
>    fjorten** settings-tests. `Turning_waiting_back_off_turns_it_off` lukker det.
> 8. **`Waiting_issues_are_excluded_until_asked_for` vagter læserens default, ikke afsnit 4a.** Med
>    begge felter fjernet fra `ReadAllAsync` består den; det er `Value(...) != "false"` der fælder
>    den. Kravets rigtige vagter er Task 6's rejsetests. Docstringen siger nu det.
> 9. **`EnableSensitiveDataLogging()` ville skrive tokenet i klartekst i loggen**, og alle vagter var
>    blinde. `SensitiveLoggingTests` lukker det. Uden flaget lækker loggen stadig tokenets **længde**
>    (`Size = 32`) — skrevet i doc-kommentaren, så bagatellen ikke bliver en overraskelse.
>
> Endeligt antal: **9** i `JiraSettingsEndpointsTests`, **131** i `Todo.Api.Tests`, hvoraf
> `ContractDriftTests` fortsat er den ene røde indtil Task 6.
>
> **Et mønster værd at tage med til de resterende tasks:** af Task 3's ni fund kom **fire** fra at
> mutere koden og køre testene igen — ikke fra at læse dem. En vagt der ikke er set fejle på den
> mutation den skal fange, er en formodning.

**Step 1: Skriv de fejlende tests — lækagen først**

```csharp
using System.Net;
using System.Net.Http.Json;
using Todo.Core.Settings;

namespace Todo.Api.Tests;

public class JiraSettingsEndpointsTests : ApiTest
{
    private const string Token = "a-secret-that-must-not-come-back";

    /// <summary>
    /// The whole reason the token has its own endpoint. This asserts on the raw response body
    /// rather than on a deserialised field, because a leak could arrive under any property name —
    /// including one the contract does not declare and the generated client would drop silently.
    /// </summary>
    [Fact]
    public async Task The_token_never_comes_back_out_of_the_api()
    {
        var stored = await Host.Client.PutAsJsonAsync(
            "/api/settings/jira-token", new { token = Token });

        // Without this the guard passes on a token that was never stored: a route answering 400
        // would leave it looking for a string the system does not have. Measured — the version
        // without these two lines was green against a route that always answered 400.
        stored.EnsureSuccessStatusCode();

        Assert.True(
            (await stored.Content.ReadFromJsonAsync<SettingsBody>())!.HasJiraToken,
            "The token was not stored, so the leak assertion below would prove nothing.");

        var body = await Host.Client.GetStringAsync("/api/settings");

        Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A separate assertion with a separate reason. The derived document carries schema, not
    /// values, so it cannot catch a leak in the response builder — what it can catch is a real
    /// token pasted into an `example:` on the contract. The PUT below is therefore decorative
    /// rather than a precondition; do not "strengthen" it with EnsureSuccessStatusCode.
    /// </summary>
    [Fact]
    public async Task The_openapi_document_carries_no_token()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        foreach (var path in new[] { "/openapi/v1.json", "/openapi/contract.yaml" })
        {
            var body = await Host.Client.GetStringAsync(path);

            Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Storing_a_token_shows_up_as_having_one()
    {
        var before = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(before!.HasJiraToken);

        var response = await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.True(after!.HasJiraToken);
    }

    [Fact]
    public async Task Clearing_the_token_removes_it()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        var response = await Host.Client.DeleteAsync("/api/settings/jira-token");

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.False(after!.HasJiraToken);
    }

    [Fact]
    public async Task An_empty_token_is_rejected_rather_than_stored_as_blank()
    {
        var response = await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The regression this slice's own convention warns about. PUT /api/settings is a full
    /// replacement — an absent field means clear — so a settings save must not be able to reach
    /// the token. Slice 9 lost a stored DeferUntil to exactly this shape of bug.
    /// </summary>
    [Fact]
    public async Task Saving_the_other_settings_does_not_clear_the_token()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings", new { language = "en", jiraProjectKey = "SAAS" });

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.True(after!.HasJiraToken);
        Assert.Equal("SAAS", after.JiraProjectKey);
    }

    [Fact]
    public async Task The_waiting_statuses_round_trip_as_a_list()
    {
        string[] names = ["Afventer general", "Venter på support"];

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings", new { jiraWaitingStatuses = names, jiraIncludeWaiting = true });

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.Equal(names, after!.JiraWaitingStatuses);
        Assert.True(after.JiraIncludeWaiting);
    }

    /// <summary>
    /// Default off, and the design document's section 4a says why: an import that silently pulled
    /// waiting issues in would fill the list with things you cannot act on.
    /// </summary>
    [Fact]
    public async Task Waiting_issues_are_excluded_until_asked_for()
    {
        var settings = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(settings!.JiraIncludeWaiting);
        Assert.Empty(settings.JiraWaitingStatuses);
    }

    private sealed record SettingsBody(
        string? Language,
        string? JiraBaseUrl,
        string? JiraProjectKey,
        string[] JiraWaitingStatuses,
        bool JiraIncludeWaiting,
        bool HasJiraToken);
}
```

**Step 2: Kør dem og se dem fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraSettingsEndpointsTests"
```

Forventet: **405 Method Not Allowed** på `/api/settings/jira-token` — ikke 404, fordi
`MapFallbackToFile` kun matcher GET. Og de nye felter mangler i svaret.

**Step 3: Nøglerne og fejlkoderne**

I `SettingKeys`:

```csharp
    public const string JiraBaseUrl = "jira.baseUrl";
    public const string JiraProjectKey = "jira.projectKey";
    public const string JiraToken = "jira.token";
    public const string JiraWaitingStatuses = "jira.waitingStatuses";
    public const string JiraIncludeWaiting = "jira.includeWaiting";
```

I `ErrorCodes`:

```csharp
    public const string SettingsEmptyToken = "settings.emptyToken";

    public const string JiraNotConfigured = "jira.notConfigured";
    public const string JiraProjectKeyRequired = "jira.projectKeyRequired";
    public const string JiraRefused = "jira.refused";
    public const string JiraUnreachable = "jira.unreachable";
    public const string JiraRowKeyRequired = "jira.rowKeyRequired";
    public const string JiraRowTitleRequired = "jira.rowTitleRequired";
    public const string JiraRowStatusRequired = "jira.rowStatusRequired";
    public const string JiraExcludedWaiting = "jira.excludedWaiting";
```

`jira.excludedWaiting` er både en fejlkode og teksten i `excluded` på en forhåndsvisningsrække —
samme mekanisme som `ApiError.code`, så frontenden oversætter den med samme funktion.

**Step 4: `JiraSettings` og læseren**

`src/Todo.Core/Jira/JiraSettings.cs`:

```csharp
namespace Todo.Core.Jira;

/// <summary>
/// What the app needs to talk to one Jira. The token is in here because the caller is server-side
/// only; it must never be put on a contract type. See the design document's section 3 on why it is
/// stored in cleartext.
/// </summary>
public sealed record JiraSettings(
    string? BaseUrl,
    string? ProjectKey,
    string? Token,
    IReadOnlyList<string> WaitingStatuses,
    bool IncludeWaiting)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token);

    /// <summary>Where the source system shows an item, computed rather than stored.</summary>
    public string? BrowseUrl(string externalKey) =>
        string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(externalKey)
            ? null
            : $"{BaseUrl!.TrimEnd('/')}/browse/{externalKey}";
}
```

`src/Todo.Core/Jira/JiraSettingsReader.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Core.Jira;

public sealed class JiraSettingsReader(TodoDbContext db)
{
    public async Task<JiraSettings> ReadAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings
            .Where(s => s.Key.StartsWith("jira."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new JiraSettings(
            BaseUrl: Value(rows, SettingKeys.JiraBaseUrl),
            ProjectKey: Value(rows, SettingKeys.JiraProjectKey),
            Token: Value(rows, SettingKeys.JiraToken),
            WaitingStatuses: ReadList(Value(rows, SettingKeys.JiraWaitingStatuses)),
            IncludeWaiting: Value(rows, SettingKeys.JiraIncludeWaiting) == "true");
    }

    private static string? Value(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// The list is one row of JSON. A corrupt value reads as an empty list rather than throwing:
    /// unreadable settings must not stop the app from opening, and an empty waiting list is the
    /// safe reading — it means nothing is treated as waiting.
    /// </summary>
    private static IReadOnlyList<string> ReadList(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
```

Registrér den i `TodoHost`: `builder.Services.AddScoped<JiraSettingsReader>();`

**Step 5: Udvid `SettingsEndpoints`**

`SettingsResponse` får de fem nye felter. Byg svaret ét sted, så begge endpoints og tokenets to
kan dele det:

```csharp
    private static async Task<SettingsResponse> ReadAllAsync(TodoDbContext db)
    {
        var jira = await new JiraSettingsReader(db).ReadAsync();

        return new SettingsResponse
        {
            Language = await ReadAsync(db, SettingKeys.Language),
            JiraBaseUrl = jira.BaseUrl,
            JiraProjectKey = jira.ProjectKey,
            JiraWaitingStatuses = [.. jira.WaitingStatuses],
            JiraIncludeWaiting = jira.IncludeWaiting,
            // The token itself is deliberately absent. Only whether there is one.
            HasJiraToken = jira.Token is not null,
        };
    }
```

`PUT /api/settings` skriver `Language`, `JiraBaseUrl`, `JiraProjectKey`,
`JiraWaitingStatuses` (som JSON, eller fjernet når listen er tom) og `JiraIncludeWaiting`
(`request.JiraIncludeWaiting ? "true" : null` — se rettelse 1 ovenfor) — og **rører ikke `SettingKeys.JiraToken`**. Basisurl'en trimmes for
efterstillet `/`, så `BrowseUrl` ikke laver en dobbelt skråstreg.

Tokenets to ruter:

```csharp
        app.MapPut("/api/settings/jira-token",
            async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
                JiraTokenRequest request, TodoDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsEmptyToken, "A token cannot be blank.");
            }

            await StoreAsync(db, SettingKeys.JiraToken, request.Token.Trim());
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db));
        })
        .WithName("setJiraToken")
        .WithTags("Settings");

        app.MapDelete("/api/settings/jira-token", async (TodoDbContext db) =>
        {
            await StoreAsync(db, SettingKeys.JiraToken, null);
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db));
        })
        .WithName("clearJiraToken")
        .WithTags("Settings");
```

`StoreAsync(db, key, null)` fjerner rækken — det gør den allerede.

**Step 6: Kør testene**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraSettingsEndpointsTests"
```

Forventet: PASS, 8 tests. Kør derefter hele Api-projektet: **129 i alt, 128 grønne**, hvor den ene
røde er `ContractDriftTests` — den lukkes i Task 6. Afviger tallet, sig det frem for at runde af.

**Step 7: Se lækage-vagten fejle — på begge sine forudsætninger**

Vagten har nu **to** ting den hviler på, og en vagt med to forudsætninger skal ses fejle på begge.

**Den ægte lækage.** Læg `JiraToken = jira.Token` på `SettingsResponse` og returnér den. Prøven
skal ligge **inde i `Todo.Contracts`** — `partial` fletter ikke på tværs af assemblies, se
rettelse 2 ovenfor — eller redigér `Contracts.g.cs` og kør `scripts\generate-api.ps1` bagefter.
Forventet: `Assert.DoesNotContain() Failure: Sub-string found`.

**Det ugemte token.** Vend betingelsen i `PUT /api/settings/jira-token` til
`if (!string.IsNullOrWhiteSpace(request.Token))`, så ruten altid svarer 400. Forventet: fejl på
`EnsureSuccessStatusCode`. Uden Step 1's to ekstra linjer ville vagten **bestå** her — det er målt,
ikke ræsonneret.

Rul begge tilbage og bekræft at `git status` er ren: en efterladt prøve er en lækage i sig selv.

Byg lækagen med vilje: læg `JiraToken = jira.Token` på `SettingsResponse` og returnér den.

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~The_token_never_comes_back_out_of_the_api"
```

Forventet: FAIL med `Assert.DoesNotContain() Failure: Found: a-secret-that-must-not-come-back`.
Rul lækagen tilbage og kør igen: PASS. **Rapportér fejlteksten.**

**Step 8: Commit**

```bash
git add src/Todo.Core/ src/Todo.Host/ tests/Todo.Api.Tests/JiraSettingsEndpointsTests.cs
git commit -m "✨ Gem Jira-indstillingerne, med tokenet på sit eget endpoint"
```

---

## Task 4: `ITaskSource`, `JiraTaskSource` og den falske Jira

**Files:**
- Create: `src/Todo.Core/Sources/ITaskSource.cs`, `ExternalTask.cs`, `ExternalTaskPage.cs`, `SourceIdentity.cs`, `SourceException.cs`
- Create: `src/Todo.Host/Jira/JiraTaskSource.cs`
- Create: `tests/Todo.TestSupport/Jira/FakeJira.cs`
- Test: `tests/Todo.Api.Tests/JiraTaskSourceTests.cs`
- Test: `tests/Todo.Core.Tests/Jira/JiraSettingsTests.cs` (ny, se boksen)

> **Arv fra Task 3, målt i dens review: tre ting den byggede har nul tests, og denne task bruger to
> af dem.**
>
> - **`JiraSettings.IsConfigured`** tjekker kun, at `BaseUrl` og `Token` er ikke-blanke. Er navnet
>   ærligt? En basisURL som `https:/jira` med én skråstreg, eller en tom sti, gør den `true`.
>   Denne task er den første der faktisk *bruger* svaret til at kalde ud, så grænsen hører her.
> - **`JiraSettings.BrowseUrl`** trimmer efterhængt `/`. Så gør `PUT /api/settings` også, på vej ind
>   (`SettingsEndpoints.cs`). **Én af de to trimninger er død kode, og ingen ved hvilken**, fordi
>   ingen test nogensinde sætter `jiraBaseUrl`. Afgør det, og fjern den døde — eller behold begge og
>   skriv hvorfor.
> - **`JiraSettingsReader.ReadList`**'s dokumenterede fallback ved korrupt JSON er utestet. Den kan
>   kun nås ved at skrive skrald direkte i databasen, så den hører i en **Core**-test frem for en
>   Api-test.
>
> Læg de host-frie i `tests/Todo.Core.Tests/Jira/JiraSettingsTests.cs`. **Kun `IsConfigured` og
> `BrowseUrl` er host-frie** — trimningen bor i endpointet og `ReadList`s fallback kræver skrald
> skrevet direkte i databasen, så de to hører i `JiraSettingsEndpointsTests`.

> **Rettet efter kørslen, 2026-08-18. Leveret i `f61d513`; den kode er sandheden.** Ni fejl, og to af
> dem ville have kostet data eller været usynlige:
>
> 1. **Begrundelsen for den danske comparer er falsk, og vagten kunne ikke fejle.** Planen påstod, at
>    en kodepunkt-sortering ville sætte `Løst` efter `Venter`. Det sker ikke: `'L'` er `0x4C` og
>    `'V'` er `0x56`, så sammenligningen afgøres på **første tegn** og `ø` nås aldrig. Ordinal,
>    invariant og dansk giver alle tre den samme rækkefølge, så testen kunne ikke se den comparer den
>    var skrevet for. Lukket med `Æ`/`Å`, hvor de to genuint skilles — dansk sætter æ, ø, å efter z,
>    mens kodepunkterne har `Å` (0xC5) **før** `Æ` (0xC6). Det er den fjerde uopnåelige vagt i denne
>    skive; de var alle mine.
> 2. **`duedate` skal have et eksplicit `[JsonPropertyName]`.** Jira staver det i ét ord, og
>    navnepolitikken leder efter `dueDate`, læser feltet som fraværende, og **hver deadline ville
>    lydløst ankomme som null** — mens `An_issue_without_a_due_date_or_reporter_still_maps` fortsat
>    bestod. Ingen test ville have fanget det.
> 3. **`UriBuilder` kan ikke bygge søge-URL'en.** Målt: den af-escaper `%20` tilbage til et
>    mellemrum, mens `%3D` bliver stående, så resultatet er `?jql=project %3D SAAS`. URL'en bygges
>    som streng.
> 4. **`FakeJira.SourceFor` modsagde DI-designet** — en falsk uden database kan ikke bygge en
>    `JiraSettingsReader`. Løst med én offentlig constructor plus en statisk
>    `JiraTaskSource.With(client, settings)`. Det **måtte** være en factory: `ActivatorUtilities`
>    vælger en typet klients constructor ved at tælle opløselige parametre, og to
>    to-parameter-constructorer var tvetydige.
> 5. **Intet i planen testede DI-registreringen**, og alle tests går uden om den via `With`.
>    `AddHttpClient` er **lazy**, så en brudt registrering ville først dukke op i Task 6 og ligne
>    Task 6's fejl. `JiraTaskSourceRegistrationTests` lukker det, på
>    `LinkLauncherRegistrationTests`' mønster.
> 6. **`IsConfigured` blev strammet frem for omdøbt.** Den kræver nu at `BaseUrl` parser som en
>    absolut `http`/`https`-URI. Målt: `Uri.TryCreate("https:/jira", UriKind.Absolute, …)` er
>    **false**, så planens eget eksempel er præcis det stramningen fanger — før den læste det som
>    konfigureret og ville have givet en uhåndteret `UriFormatException` på første request.
>    Skemategninget er separat, fordi `file:///c:/temp` og `javascript:alert(1)` **er** absolutte
>    URI'er.
> 7. **Den dobbelte trimning: `BrowseUrl`s er den redundante.** Målt med `git log -S` — begge ankom i
>    **samme** commit, så ingen gemt værdi har nogensinde båret en efterhængt skråstreg. Beholdt
>    alligevel, fordi den er en offentlig metode på en offentlig record i Core, der ikke kan se om
>    dens kalder kom gennem endpointet, og fordi den er den af de to hvis fravær **brugeren** ser
>    (`//browse/SAAS-1`). Den rigtige fejl var, at ingen af dem var målt; nu er begge set fejle.
> 8. **Planens `git add`-liste var ufuldstændig** — den udelod `JiraSettings.cs` og
>    `JiraSettingsEndpointsTests.cs`, som arven nødvendigvis ændrer.
> 9. Mindre: planens forudsagte fejlbesked for `Distinct`-mutationen ("`I gang` to gange") er
>    retningsrigtig, men fejlen er reelt et indeks-2-mismatch, fordi dubletten sorterer ved siden af
>    sig selv frem for at blive hængt bagpå.
>
> Endeligt antal: **10** i `JiraTaskSourceTests`, **144** i `Todo.Api.Tests` (143 grønne,
> `ContractDriftTests` rød til Task 6), **83** i `Todo.Core.Tests`.
>
> **Ti mutationer blev kørt, og alle tio fældede deres vagt.** Det er metoden der fandt punkt 1 og 5.

**Step 1: Sømmen i Core**

Fem små filer, én type pr. fil.

```csharp
namespace Todo.Core.Sources;

/// <summary>
/// One external system that can hand over the items assigned to you. Jira implements it in slice
/// 11; ADO follows in slice 12, and that is when it shows whether the shape holds. Deliberately
/// not an IMentionSource — Jira has no mentions to fetch, and forcing it to throw would be worse
/// than two interfaces (design document, section 6).
/// </summary>
public interface ITaskSource
{
    /// <summary>What lands in <c>TaskItem.SourceId</c>.</summary>
    string SourceId { get; }

    /// <summary>Who the stored credential belongs to. This is what "Test connection" answers.</summary>
    Task<SourceIdentity> TestAsync(CancellationToken ct = default);

    /// <summary>The status names the configured project uses, so the user can pick from them.</summary>
    Task<IReadOnlyList<string>> FetchStatusNamesAsync(CancellationToken ct = default);

    Task<ExternalTaskPage> FetchAssignedAsync(CancellationToken ct = default);

    /// <summary>
    /// When the item last changed status. A separate call on purpose: Jira DC 10.3.24 does not
    /// return statuscategorychangedate (measured 2026-08-18), so it comes from the changelog, and
    /// only the rows that need it should pay for it.
    /// </summary>
    Task<DateTime?> FetchStatusChangedAtAsync(string externalKey, CancellationToken ct = default);
}
```

```csharp
namespace Todo.Core.Sources;

public sealed record ExternalTask(
    string Key,
    string Title,
    string? Note,
    DateOnly? Deadline,
    string? Requester,
    string StatusName);
```

```csharp
namespace Todo.Core.Sources;

/// <summary>
/// The items plus what the source said the total was, so a page that got truncated is visible
/// rather than looking like the whole answer.
/// </summary>
public sealed record ExternalTaskPage(IReadOnlyList<ExternalTask> Items, int Total);
```

```csharp
namespace Todo.Core.Sources;

public sealed record SourceIdentity(string DisplayName);
```

```csharp
namespace Todo.Core.Sources;

/// <summary>
/// Something outside the process said no. Carries an <see cref="ErrorCodes"/> value so the
/// endpoint can turn it into a 400 the frontend can translate, rather than a 500 with a stack
/// trace the user cannot act on.
/// </summary>
public sealed class SourceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
```

**Step 2: Skriv de fejlende tests mod en falsk Jira**

```csharp
using Todo.Core.Sources;
using Todo.TestSupport.Jira;

namespace Todo.Api.Tests;

public class JiraTaskSourceTests
{
    [Fact]
    public async Task Testing_the_connection_answers_with_the_display_name()
    {
        await using var jira = await FakeJira.StartAsync();

        var identity = await jira.SourceFor("SAAS").TestAsync();

        Assert.Equal("Thomas", identity.DisplayName);
    }

    /// <summary>
    /// The PAT goes in as a Bearer token. Measured against the real instance 2026-08-18: GET
    /// /rest/api/2/myself with Authorization: Bearer answers 200. Basic auth would also be
    /// plausible from the outside, so this pins which one.
    /// </summary>
    [Fact]
    public async Task The_token_is_sent_as_a_bearer_token()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").TestAsync();

        Assert.Equal("Bearer", jira.LastAuthorizationScheme);
        Assert.Equal(FakeJira.Token, jira.LastAuthorizationParameter);
    }

    [Fact]
    public async Task A_refused_token_becomes_a_source_exception_rather_than_a_crash()
    {
        await using var jira = await FakeJira.StartAsync(rejectToken: true);

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS").TestAsync());

        Assert.Equal(ErrorCodes.JiraRefused, exception.Code);
    }

    [Fact]
    public async Task The_status_names_come_back_sorted_and_without_duplicates()
    {
        await using var jira = await FakeJira.StartAsync();

        var names = await jira.SourceFor("SAAS").FetchStatusNamesAsync();

        Assert.Equal(
            ["Afventer general", "I gang", "Løst", "Venter på support"],
            names);
    }

    /// <summary>
    /// The JQL is the whole requirement about only importing SAAS. Asserting on the query string
    /// the source sent is the only place that can see it — the fake would happily answer a JQL
    /// with no project clause at all.
    /// </summary>
    [Fact]
    public async Task The_query_is_narrowed_to_the_configured_project()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").FetchAssignedAsync();

        Assert.Contains("project = SAAS", jira.LastJql);
        Assert.Contains("assignee = currentUser()", jira.LastJql);
        Assert.Contains("resolution = Unresolved", jira.LastJql);
    }

    [Fact]
    public async Task An_issue_maps_field_by_field()
    {
        await using var jira = await FakeJira.StartAsync();

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        var issue = Assert.Single(page.Items, i => i.Key == "SAAS-1");

        Assert.Equal("Kunden kan ikke logge ind", issue.Title);
        Assert.Equal(new DateOnly(2026, 8, 20), issue.Deadline);
        Assert.Equal("Anna Andersen", issue.Requester);
        Assert.Equal("I gang", issue.StatusName);
        // The description arrives as wiki markup and is stored as CommonMark.
        Assert.Equal("**vigtigt**", issue.Note);
    }

    [Fact]
    public async Task An_issue_without_a_due_date_or_reporter_still_maps()
    {
        await using var jira = await FakeJira.StartAsync();

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        var issue = Assert.Single(page.Items, i => i.Key == "SAAS-3");

        Assert.Null(issue.Deadline);
        Assert.Null(issue.Requester);
        Assert.Null(issue.Note);
    }

    /// <summary>
    /// Classic pagination, measured 2026-08-18: startAt, maxResults, total, issues — not Cloud's
    /// nextPageToken/isLast. The fake serves two pages, so a source that reads only the first one
    /// fails here.
    /// </summary>
    [Fact]
    public async Task Every_page_is_read_rather_than_only_the_first()
    {
        await using var jira = await FakeJira.StartAsync(pageSize: 2);

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task An_unreachable_host_becomes_a_source_exception()
    {
        await using var jira = await FakeJira.StartAsync();
        await jira.StopServerAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS").TestAsync());

        Assert.Equal(ErrorCodes.JiraUnreachable, exception.Code);
    }
}
```

**Step 3: Kør dem og se dem fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraTaskSourceTests"
```

Forventet: fejler på at `FakeJira` og `JiraTaskSource` ikke findes.

**Step 4: Byg den falske Jira**

`tests/Todo.TestSupport/Jira/FakeJira.cs`. En minimal `WebApplication` på en fri loopback-port,
der svarer på de tre ruter `JiraTaskSource` bruger. Den bruger **rigtig HTTP**, så
`HttpClient`-vejen, headerne, query-strengen og JSON-læsningen bliver kørt — en stubbet
`ITaskSource` ville springe præcis det over der kan gå galt.

Krav til klassen:

- `StartAsync(bool rejectToken = false, int pageSize = 50)` binder til `http://127.0.0.1:0`.
- `Token` er en konstant, fx `"fake-pat"`.
- `SourceFor(string projectKey)` bygger en rigtig `JiraTaskSource` med en rigtig `HttpClient`
  og en `JiraSettings` der peger på `BaseUrl` med `Token`.
- Optager `LastAuthorizationScheme`, `LastAuthorizationParameter` og `LastJql`.
- `StopServerAsync()` lukker serveren, men bevarer `BaseUrl`, så "værten svarer ikke" kan prøves.
- **Ingen vært uden for `127.0.0.1`.** Ingen konstant i denne fil må indeholde et rigtigt
  værtsnavn; Task 7's vagt håndhæver det på hele repoet.

Ruterne:

`GET /rest/api/2/myself` → `{"displayName":"Thomas"}`, eller `401` med
`{"errorMessages":["Log ind krævet"]}` når `rejectToken`.

`GET /rest/api/2/project/{key}/statuses` → Jiras egen form er en liste **pr. issuetype**, hver med
sit `statuses`-array, og de samme statusser optræder i flere issuetyper. Den falske Jira skal have
**begge** — det er hele grunden til at kilden skal flade ud og fjerne dubletter:

```json
[
  { "name": "Support",  "statuses": [ { "name": "I gang" }, { "name": "Afventer general" }, { "name": "Løst" } ] },
  { "name": "Bug",      "statuses": [ { "name": "I gang" }, { "name": "Venter på support" } ] }
]
```

`GET /rest/api/2/search?jql=…&startAt=…&maxResults=…` → klassisk paginering over tre sager:

| key | summary | duedate | reporter | status | description |
| --- | --- | --- | --- | --- | --- |
| SAAS-1 | Kunden kan ikke logge ind | `2026-08-20` | Anna Andersen | I gang | `*vigtigt*` |
| SAAS-2 | Venter på svar fra kunden | `null` | Bo Bertelsen | Afventer general | `h1. Sag` |
| SAAS-3 | Uden noget som helst | `null` | `null` | I gang | `null` |

Svarets krop er `{ "startAt": n, "maxResults": m, "total": 3, "issues": [ … ] }`. Skæres i
`pageSize`-bidder, så `Every_page_is_read_rather_than_only_the_first` har noget at fange.

`GET /rest/api/2/issue/{key}?expand=changelog` → Task 5.

**Step 5: Implementér `JiraTaskSource`**

`src/Todo.Host/Jira/JiraTaskSource.cs`. De ting der ikke må gættes:

- **Basisurl'en kommer fra indstillingerne i runtime**, ikke fra registreringen, så
  `HttpClient.BaseAddress` sættes **ikke**. Byg absolutte URI'er pr. kald ud fra `settings.BaseUrl`.
- `Authorization` sættes pr. request som `new AuthenticationHeaderValue("Bearer", token)`.
- **Timeout.** En Jira der ikke svarer må ikke hænge appen. Sæt `HttpClient.Timeout` til 30 s, og
  oversæt `TaskCanceledException` til `SourceException(ErrorCodes.JiraUnreachable, …)` —
  `HttpClient` kaster den både ved timeout og ved afbrudt kald, og en 500 med "A task was
  canceled" siger intet til brugeren.
- `HttpRequestException` → `ErrorCodes.JiraUnreachable`. En svarkode uden for 2xx →
  `ErrorCodes.JiraRefused`. **Fejlbeskeden må aldrig indeholde tokenet** — log og indpak kun
  statuskoden og Jiras egen `errorMessages`.
- JQL: `project = {key} AND assignee = currentUser() AND resolution = Unresolved ORDER BY duedate ASC`.
  Projektnøglen valideres mod `^[A-Z][A-Z0-9_]*$` før den sættes ind; **en nøgle fra en indstilling
  er brugerinput, og JQL har citater**. Fejler den, kast
  `SourceException(ErrorCodes.JiraProjectKeyRequired, …)`.
- `fields=summary,description,duedate,reporter,status` — hent kun det der bruges. Uden `fields`
  sender Jira alt, inklusive felter der kan være store.
- `duedate` er `"2026-08-20"` eller `null` → `DateOnly.Parse` med `InvariantCulture`.
- `description` gennem `WikiMarkup.ToCommonMark`.
- Statusnavne: flad `[].statuses[].name` ud, `Distinct(StringComparer.Ordinal)`, sortér med
  `StringComparer.Create(new CultureInfo("da-DK"), ignoreCase: false)` — `Løst` skal sortere som
  dansk, ikke efter kodepunkt, ellers står den efter `Venter`.
- Paginering: hent til `startAt + issues.Count >= total`, og **stop hvis en side kommer tom tilbage**
  selvom `total` er højere. Uden det loop kan en instans der svarer inkonsekvent hænge appen.

**Step 6: Kør testene**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraTaskSourceTests"
```

Forventet: PASS, 9 tests.

**Step 7: Se projektvagten fejle**

Fjern `project = {key} AND` fra JQL-strengen.

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~The_query_is_narrowed_to_the_configured_project"
```

Forventet: FAIL med `Assert.Contains() Failure` og den JQL der faktisk blev sendt i beskeden. Rul
tilbage. **Rapportér fejlteksten.**

**Step 8: Commit**

```bash
git add src/Todo.Core/Sources/ src/Todo.Host/Jira/ tests/Todo.TestSupport/Jira/ tests/Todo.Api.Tests/JiraTaskSourceTests.cs
git commit -m "✨ Hent tildelte SAAS-sager gennem ITaskSource, målt mod en falsk Jira"
```

---

## Task 5: `WaitingSince` fra changeloggen

**Files:**
- Modify: `src/Todo.Host/Jira/JiraTaskSource.cs`
- Modify: `tests/Todo.TestSupport/Jira/FakeJira.cs`
- Test: `tests/Todo.Api.Tests/JiraChangelogTests.cs`

> **Rettet efter kørslen, 2026-08-18. Leveret i `bffec6f`; den kode er sandheden.** Otte fejl, og
> **den første ville have sendt en import der kaster på hver enkelt sag.**
>
> 1. **`DateTimeOffset?` på DTO'en er den forkerte kur.** Jira skriver offsettet i ISO 8601's
>    **basale** form, `+0200`; System.Text.Json accepterer kun den **udvidede**, `+02:00`. Målt før
>    implementeringen: `System.FormatException : The JSON value is not in a supported DateTimeOffset
>    format`. `Created` bindes derfor som `string` og parses med `DateTimeOffset.TryParse` og
>    `InvariantCulture`, som tager begge former. Planens diagnose var rigtig — en `DateTime` taber
>    offsettet — men kuren fejlede hårdere end sygdommen.
> 2. **Og fælden er dobbelt: den falske Jira skulle også have `created` som `string`.** Havde den
>    været typet `DateTimeOffset`, ville den udsende `+02:00`, planens kode ville blive **grøn**, og
>    kun den rigtige instans ville kaste. En falsk server der udsender .NET's format frem for
>    fremmedsystemets, måler sig selv. Det er derfor fejl 1 var findbar overhovedet.
> 3. **Step 4's kode kompilerer ikke mod Task 4.** Der findes ingen `GetAsync<T>(url, ct)`; Task 4's
>    hjælper er `GetAsync(settings, path, query, ct)` med `path` relativt til `rest/api/2/`, query
>    for sig, og en separat `Read<T>(body)`.
> 4. **`An_issue_that_never_changed_status_has_no_waiting_since` kunne ikke fejle** — målt: den bestod
>    en metode der returnerer `null` uden at kalde ud. `Assert.Null` alene kan ikke skelne en tom
>    changelog fra en metode der aldrig spurgte. Lukket med en påstand om kaldet. **Femte uopnåelige
>    vagt i skiven.**
> 5. **Den positive påstand i newest-wins-testen er bærende, ikke valgfri.** Målt: `OrderBy` i stedet
>    for `OrderByDescending` giver `07:00`, og den negative påstand om `06:00` fanger den **ikke**.
>    Planen kaldte den positive "behold gerne".
> 6. **`SAAS-4` kan ikke ligge i den falske Jiras `Issues`-array**, fordi `JiraTaskSourceTests`
>    påstår `Total == 3`. Den findes kun som changelog-fixture — en sag med historik som søgningen
>    ikke returnerer. **Task 6 skal vide det:** dens forhåndsvisning ser aldrig `SAAS-4`.
> 7. Mutation 3's forudsigelse var "en test fejler" — **to** gør.
> 8. Kravet om at changeloggen kun hentes for **ventende** sager kan ikke bo her; det er
>    forhåndsvisningens beslutning. Flyttet til Task 6.
>
> Ud over planen, og godt: den falske Jira honorerer nu `expand`, og en sjette test kræver at
> parameteren sendes. Uden den ville en glemt `expand=changelog` gøre `WaitingSince` **null for hver
> række** og ligne "Jira har ingen historik" frem for en fejl. En uparsebar `created` svarer desuden
> `null` frem for at kaste, så én skæv post koster sin egen ventedato og ikke hele importen — den vej
> er **utestet**.
>
> Endeligt antal: **6** i `JiraChangelogTests`, **150** i Api (149 grønne), **83** i Core.

**Step 1: Skriv de fejlende tests**

Målt tidsstempel fra instansen 2026-08-18: `2026-08-17T14:10:13.593+0200`. Det er `12:10:13.593`
i UTC, og **det er hele testen** — `DateTime.Parse` uden omregning giver `14:10`, og
`waitingDays` fra skive 5 ville så være regnet på et forkert udgangspunkt.

```csharp
using Todo.TestSupport.Jira;

namespace Todo.Api.Tests;

public class JiraChangelogTests
{
    /// <summary>
    /// The offset is the point. Jira answers 2026-08-17T14:10:13.593+0200; the app stores UTC
    /// DateTime — never DateTimeOffset, which SQLite cannot sort — so the stored value has to be
    /// 12:10. A plain DateTime.Parse keeps 14:10 and passes every other assertion here.
    /// </summary>
    [Fact]
    public async Task The_status_change_is_converted_to_utc()
    {
        await using var jira = await FakeJira.StartAsync();

        var changed = await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Equal(new DateTime(2026, 8, 17, 12, 10, 13, 593, DateTimeKind.Utc), changed);
        Assert.Equal(DateTimeKind.Utc, changed!.Value.Kind);
    }

    /// <summary>
    /// The newest status change, not the newest entry. The fake's changelog has a later entry that
    /// only changed the assignee — a source that reads the last entry blindly picks that one.
    /// </summary>
    [Fact]
    public async Task The_newest_status_change_wins_over_a_newer_unrelated_change()
    {
        await using var jira = await FakeJira.StartAsync();

        var changed = await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Equal(13, changed!.Value.Second);
    }

    [Fact]
    public async Task An_issue_that_never_changed_status_has_no_waiting_since()
    {
        await using var jira = await FakeJira.StartAsync();

        Assert.Null(await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-3"));
    }

    /// <summary>
    /// Not an optimisation with no observable effect: the changelog is one call per issue, so
    /// fetching it for rows that will never show a waiting duration is pure cost against the
    /// instance. The counter on the fake is what makes the claim checkable.
    /// </summary>
    [Fact]
    public async Task The_changelog_is_not_fetched_for_an_issue_that_is_not_waiting()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Equal(["SAAS-2"], jira.ChangelogRequests);
    }
}
```

**Step 2: Kør dem og se dem fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraChangelogTests"
```

Forventet: `FetchStatusChangedAtAsync` findes ikke, eller returnerer `null` for alt.

**Step 3: Læg changeloggen i den falske Jira**

`GET /rest/api/2/issue/{key}?expand=changelog`. For `SAAS-2` — bemærk at den **nyeste** post ikke
er en statusændring:

```json
{
  "key": "SAAS-2",
  "changelog": {
    "histories": [
      { "created": "2026-08-15T09:00:00.000+0200",
        "items": [ { "field": "status", "fromString": "Ny SLA", "toString": "I gang" } ] },
      { "created": "2026-08-17T14:10:13.593+0200",
        "items": [ { "field": "status", "fromString": "I gang", "toString": "Afventer general" } ] },
      { "created": "2026-08-18T08:00:00.000+0200",
        "items": [ { "field": "assignee", "toString": "thh" } ] }
    ]
  }
}
```

For `SAAS-3`: `"histories": []`.

Optag hver forespurgt nøgle i en `List<string> ChangelogRequests`.

**Step 4: Implementér**

```csharp
    public async Task<DateTime?> FetchStatusChangedAtAsync(
        string externalKey, CancellationToken ct = default)
    {
        var issue = await GetAsync<JiraIssueDetail>(
            $"rest/api/2/issue/{Uri.EscapeDataString(externalKey)}?expand=changelog", ct);

        var newest = issue?.Changelog?.Histories
            ?.Where(history => history.Items?.Any(
                item => string.Equals(item.Field, "status", StringComparison.OrdinalIgnoreCase))
                    == true)
            .Select(history => history.Created)
            .Where(created => created is not null)
            .OrderByDescending(created => created!.Value)
            .FirstOrDefault();

        // Parsed as DateTimeOffset so the +0200 is honoured, then flattened to UTC DateTime:
        // SQLite cannot sort DateTimeOffset, so it must never reach the entity.
        return newest?.UtcDateTime;
    }
```

`Created` er en `DateTimeOffset?` på DTO'en. **Ikke** en `DateTime` — bindes den som `DateTime`,
har System.Text.Json allerede kastet offsettet væk, før koden ovenfor får den at se, og testen
fejler et sted der ikke peger på årsagen.

**Step 5: Kør testene og se den vigtigste fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraChangelogTests"
```

Forventet: PASS, 4 tests.

Skift derefter DTO'ens `DateTimeOffset?` til `DateTime?` og `newest?.UtcDateTime` til `newest`.

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~The_status_change_is_converted_to_utc"
```

Forventet: FAIL med de to tidspunkter, `14:10:13.593` mod `12:10:13.593`. Rul tilbage.
**Rapportér fejlteksten.**

**Step 6: Commit**

```bash
git add src/Todo.Host/Jira/ tests/Todo.TestSupport/Jira/ tests/Todo.Api.Tests/JiraChangelogTests.cs
git commit -m "✨ Læs WaitingSince af changeloggen og omregn offsettet til UTC"
```

---

## Task 6: Endpoints — test, statusser, forhåndsvisning og import

**Files:**
- Create: `src/Todo.Host/Endpoints/JiraEndpoints.cs`
- Modify: `src/Todo.Host/TodoHost.cs` (registrér `MapJira()`, `JiraTaskSource`, `HttpClient`)
- Modify: `src/Todo.Host/Endpoints/TaskEndpoints.cs` (`externalUrl` på svaret)
- Modify: `src/Todo.Core/Errors/ErrorCodes.cs` (en **niende** kode, se rettelse 4)
- Test: `tests/Todo.Api.Tests/JiraEndpointsTests.cs`

> **Rettet efter kørslen, 2026-08-18. Leveret i `90bf368`; den kode er sandheden.** Syv fejl, og de
> **to vigtigste er mutationer jeg foreskrev som verifikation, og som selv var uopnåelige.**
>
> 1. **`Ordinal` mod `OrdinalIgnoreCase` var uvagtet.** Mutationen fældede **ingenting** — 166 grønne.
>    Hvert fixture staver statussen identisk på begge sider, så valget var uden konsekvens for
>    suiten. Lukket med `A_status_that_differs_only_in_case_is_not_the_waiting_status`. Prisen ved den
>    forkerte comparer er reel: `Afventer Kunden` og `Afventer kunden` **kan** være to statusser i
>    Jira, og en versalufølsom sammenligning ville slå dem sammen usynligt. **Sjette uopnåelige vagt
>    i skiven.**
> 2. **`WaitingOn`-mutationen fældede heller ingenting**, fordi importpayloadet i den relevante test
>    ikke bar en `requester` — så fejlen skrev `null`, og `Assert.Null` bestod. Bevist levende frem
>    for død ved at mutere til `row.Requester ?? row.Key` og se `"SAAS-2"`, derefter lukket ved at
>    lægge `requester` i payloadet. **Ingen ny test — fixturet var hullet.**
> 3. **Testfilen kompilerer ikke som skrevet.** `ApiError` bor kun i `Todo.Contracts`, som
>    using-listen udelod (`CS0246` fire gange) — og et `using Todo.Contracts;` løser det **ikke**:
>    `CS0104`, `TodoStatus` er tvetydig mellem `Todo.Contracts` og `Todo.Core.Tasks`. Løst med
>    `using ApiError = Todo.Contracts.ApiError;`.
> 4. **Der findes ingen `ErrorCodes.JiraRowTitleTooLong`.** Planen beder om `ValidateRow`'s tre tjek
>    inklusive længden, men Task 3 leverede otte koder og ikke den. Der er nu **ni** `jira.*`-koder,
>    og **Task 8 og 9 skal oversætte ni nøgler, ikke otte.**
> 5. **`DateTime? WaitingSince` på forhåndsvisnings-recorden læser det forkerte tidspunkt.**
>    Kontraktens felt er en `DateTimeOffset`, så wiren siger `+00:00`, og System.Text.Json gør en
>    offset-bærende streng til `Kind=Local` omregnet til lokal tid: `12:10Z` blev `14:10+02:00`.
>    Det er **præcis** den forvirring Task 5 findes for at forhindre, dukket op i testen frem for i
>    koden — og den fejler **kun uden for UTC**, så den ville have lignet en maskinspecifik flakker
>    hos alle andre.
> 6. **`TodoStatus` kan ikke deserialiseres fra wiren på en håndskrevet DTO.** Kerne-enummet kaster,
>    og kontrakt-enummet er **ikke** nok: NSwag sætter `[JsonConverter]` på hver **property**, ikke på
>    typen, og wire-stavemåderne bor i `JsonStringEnumMemberName`, som kun den converter læser.
>    Recorden bærer attributten eksplicit.
> 7. **Step 2's "404 på alle fire ruter" er forkert i begge retninger.** De tre POST'er giver **405**,
>    og GET'en giver **`200` med `index.html`**, så fejlen er `'<' is an invalid start of a value`.
>    Skrevet i `CLAUDE.md`.
>
> Mindre: `git add`-listen udelod `ErrorCodes.cs`. Og `ContractDocumentTests` havde et **andet**
> forældet tal i sin brødtekst — "Four of the fifteen operations carry a summary", målt til **10 af
> 21** — placeret direkte over den assertion det handler om.
>
> Endeligt antal: **17** i `JiraEndpointsTests`, **167** i Api (alle grønne, drift-testen med),
> **83** i Core.

**Step 1: Skriv de fejlende tests**

Disse kører mod en **rigtig host** med en **falsk Jira** ved siden af, og indstillingerne skrives
gennem API'et. Det er hele vejen igennem.

```csharp
using System.Net;
using System.Net.Http.Json;
using Todo.Core.Errors;
using Todo.Core.Tasks;
using Todo.TestSupport.Jira;

namespace Todo.Api.Tests;

public class JiraEndpointsTests : ApiTest
{
    private async Task<FakeJira> ConfigureAsync(
        bool includeWaiting = false,
        string? projectKey = "SAAS",
        string[]? waitingStatuses = null)
    {
        var jira = await FakeJira.StartAsync();

        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = FakeJira.Token });
        await Host.Client.PutAsJsonAsync("/api/settings", new
        {
            jiraBaseUrl = jira.BaseUrl,
            jiraProjectKey = projectKey,
            jiraWaitingStatuses = waitingStatuses ?? ["Afventer general"],
            jiraIncludeWaiting = includeWaiting,
        });

        return jira;
    }

    [Fact]
    public async Task Testing_the_connection_reports_who_the_token_belongs_to()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsync("/api/jira/test", null);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Connection>();

        Assert.Equal("Thomas", body!.DisplayName);
    }

    [Fact]
    public async Task Testing_without_a_configured_jira_is_a_bad_request_rather_than_a_crash()
    {
        var response = await Host.Client.PostAsync("/api/jira/test", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraNotConfigured, error!.Code);
    }

    /// <summary>
    /// The user's requirement, and the reason it is a guard rather than a default: the token sees
    /// four projects including a customer one, so an empty project key must refuse rather than
    /// quietly widen the query to everything.
    /// </summary>
    [Fact]
    public async Task An_empty_project_key_refuses_rather_than_importing_every_project()
    {
        await using var jira = await ConfigureAsync(projectKey: null);

        var response = await Host.Client.PostAsync("/api/jira/preview", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraProjectKeyRequired, error!.Code);
        Assert.Empty(jira.SearchRequests);
    }

    [Fact]
    public async Task The_statuses_come_from_the_configured_project()
    {
        await using var jira = await ConfigureAsync();

        var body = await Host.Client.GetFromJsonAsync<Statuses>("/api/jira/statuses");

        Assert.Contains("Afventer general", body!.Names);
        Assert.Contains("Venter på support", body.Names);
    }

    [Fact]
    public async Task The_preview_reports_the_total_the_source_gave()
    {
        await using var jira = await ConfigureAsync();

        var body = await Preview();

        Assert.Equal(3, body.Total);
        Assert.Equal(3, body.Rows.Length);
    }

    /// <summary>
    /// Default off. The waiting row is present and marked excluded rather than missing — hiding it
    /// would look like Jira lost an issue, and it would make the setting invisible.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_shown_as_excluded_when_waiting_is_not_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: false);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsWaiting);
        Assert.Equal(ErrorCodes.JiraExcludedWaiting, row.Excluded);
    }

    [Fact]
    public async Task A_waiting_row_is_included_when_waiting_is_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsWaiting);
        Assert.Null(row.Excluded);
        Assert.Equal(new DateTime(2026, 8, 17, 12, 10, 13, 593, DateTimeKind.Utc), row.WaitingSince);
    }

    /// <summary>
    /// A status not in the user's list is not waiting, whatever it is called. This is what stops
    /// the code growing a startsWith("Afventer") shortcut — measured 2026-08-18, that heuristic
    /// loses "Venter på support".
    /// </summary>
    [Fact]
    public async Task A_status_outside_the_list_is_not_treated_as_waiting()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true, waitingStatuses: []);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.False(row.IsWaiting);
        Assert.Null(row.Excluded);
        Assert.Null(row.WaitingSince);
    }

    [Fact]
    public async Task Importing_writes_the_rows_as_tasks()
    {
        await using var jira = await ConfigureAsync();

        var imported = await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        Assert.Equal(1, imported.Imported);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal("Kunden kan ikke logge ind", task.Title);
        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Equal($"{jira.BaseUrl.TrimEnd('/')}/browse/SAAS-1", task.ExternalUrl);
    }

    [Fact]
    public async Task A_waiting_row_arrives_as_waiting_for_rather_than_open()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        // The row carries Jira's status name, not the waiting decision. The server looks the name
        // up in the user's list — see the note under the import bullet on why a required boolean
        // could not be enforced on the wire.
        await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
            waitingSince = "2026-08-17T12:10:13.593Z",
        });

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.WaitingFor, task.Status);
        // WaitingOn is deliberately empty: an issue assigned to you that is waiting is waiting on
        // somebody who is not in the assignee field, so the app cannot know who. Section 4a.
        Assert.Null(task.WaitingOn);
    }

    [Fact]
    public async Task Importing_the_same_issue_twice_skips_it()
    {
        await using var jira = await ConfigureAsync();

        await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        var second = await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
    }

    [Fact]
    public async Task A_previously_imported_issue_is_marked_in_the_preview()
    {
        await using var jira = await ConfigureAsync();

        await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.AlreadyImported);
    }

    /// <summary>
    /// Dedup is scoped by source. A retro row and a Jira issue could carry the same key, and one
    /// must not hide the other.
    /// </summary>
    [Fact]
    public async Task A_retro_row_with_the_same_key_does_not_count_as_imported()
    {
        await using var jira = await ConfigureAsync();

        await Host.AddAndSaveChangesAsync(new TaskItem
        {
            SourceId = "retro",
            ExternalKey = "SAAS-1",
            Title = "Et retro-kort",
            CreatedAt = DateTime.UtcNow,
        });

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.False(row.AlreadyImported);
    }

    /// <summary>
    /// The status is valid here on purpose. Without it the row would be rejected for the missing
    /// status instead, and this test would pass while proving nothing about the title.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_title_is_rejected()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import",
            new { rows = new[] { new { key = "SAAS-1", title = "  ", status = "I gang" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraRowTitleRequired, error!.Code);
    }

    /// <summary>
    /// The status is what the server derives waiting-ness from, so a row without one is not
    /// importable. A required boolean could not be enforced on the wire — an absent bool is
    /// `false`, which is a legal value — but an absent string is null, and that can be refused.
    /// This assertion is the whole reason the contract carries `status` rather than `isWaiting`.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_status_is_rejected()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import", new { rows = new[] { new { key = "SAAS-1", title = "En sag" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraRowStatusRequired, error!.Code);
    }

    /// <summary>
    /// The setting is authoritative at import time, not at preview time. A row the user previewed
    /// while waiting was allowed must not slip in after they turned it off — the payload carries
    /// Jira's status, so the server re-derives the decision from the list as it stands now.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_skipped_when_waiting_is_not_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: false);

        var result = await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
        });

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");

        Assert.Empty(tasks!.Items);
    }

    private async Task<PreviewBody> Preview()
    {
        var response = await Host.Client.PostAsync("/api/jira/preview", null);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private async Task<ImportBody> Import(object row)
    {
        var response = await Host.Client.PostAsJsonAsync("/api/jira/import", new { rows = new[] { row } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    private sealed record Connection(string DisplayName);
    private sealed record Statuses(string[] Names);
    private sealed record PreviewBody(PreviewRow[] Rows, int Total);
    private sealed record PreviewRow(
        string Key, string Title, string Status, bool IsWaiting,
        DateTime? WaitingSince, bool AlreadyImported, string? Excluded);
    private sealed record ImportBody(int Imported, int Skipped);
    private sealed record TaskList(TaskBody[] Items);
    private sealed record TaskBody(
        long Id, string Title, TodoStatus Status, string? WaitingOn, string? ExternalUrl);
}
```

**Step 2: Kør dem og se dem fejle**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraEndpointsTests"
```

Forventet: 404 på alle fire ruter.

**Step 3: Implementér endpointsene**

`JiraEndpoints.cs`, med skive 2's `RetroEndpoints` som forlæg. Det der ikke må gættes:

- **Alle fire ruter starter med at læse indstillingerne** og svarer
  `ApiErrors.BadRequest(ErrorCodes.JiraNotConfigured, …)` hvis `!settings.IsConfigured`.
  `/api/jira/preview` og `/api/jira/import` kræver **også** en projektnøgle
  (`ErrorCodes.JiraProjectKeyRequired`) — og skal svare **før** kilden kaldes, så
  `Assert.Empty(jira.SearchRequests)` er sand.
- **`SourceException` fanges ved rutens rand** og bliver til
  `ApiErrors.BadRequest(exception.Code, exception.Message)`. Ikke en `IExceptionHandler`: en 400
  hører til den rute der kaldte ud, og en global handler ville også fange dem der ikke gjorde.
- Forhåndsvisningen: `FetchAssignedAsync`, dernæst pr. række
  - `isWaiting = settings.WaitingStatuses.Contains(item.StatusName, StringComparer.Ordinal)` —
    **`Ordinal`**, fordi navnene kommer fra instansen i samme form begge veje, og en
    versalufølsom sammenligning ville gøre to forskellige Jira-statusser til én.
  - `waitingSince` hentes **kun** når `isWaiting` — ét kald pr. sag.
  - `excluded = isWaiting && !settings.IncludeWaiting ? ErrorCodes.JiraExcludedWaiting : null`.
  - `alreadyImported` fra `SourceId == "jira" && ExternalKey == key`.
- Importen: validér som `RetroEndpoints.ValidateRow` (nøgle, titel, `TitleMaxLength` 500) **plus at
  `status` er udfyldt**, dedup gennem et `HashSet` der udvides mens der itereres,
  `SourceId = "jira"`, `CreatedAt = clock.UtcNow`, og `WaitingOn` sættes **ikke**.

- **Ventendeheden udledes serverside, den sendes ikke.** Rækken bærer Jiras statusnavn, og
  handleren regner selv:

  ```csharp
  var isWaiting = settings.WaitingStatuses.Contains(row.Status, StringComparer.Ordinal);
  ```

  → `Status = isWaiting ? WaitingFor : Open` og `WaitingSince = isWaiting ? row.WaitingSince : null`.
  Er `isWaiting` sand mens `settings.IncludeWaiting` er falsk, **springes rækken over** og tælles
  som `skipped` — samme genudledning som forhåndsvisningens `excluded`, så kroppen ikke kan
  omgå indstillingen.

  **Hvorfor det ikke bare er et `isWaiting`-felt på kontrakten:** det var det først, med
  `required: [key, title, isWaiting]` — og det er **uhåndhæveligt**. Målt 2026-08-18: NSwag udsender
  ikke `[Required]` på en ikke-nullable værditype, så DTO'en blev `public bool IsWaiting` uden
  attribut, og `[Required]` er i øvrigt DataAnnotations, som System.Text.Json ikke håndhæver ved
  deserialisering. En fraværende bool bliver `false`, hvilket er en **gyldig værdi**, så handleren
  kan ikke afvise den — og en glemt kopiering af ét felt ville importere hver ventende sag som
  `Open`, uden fejl og med grønne tests. En fraværende `string` bliver derimod `null`, og det *kan*
  afvises. Kendsgerningen kan sendes; beslutningen kan ikke.

  Sidegevinsten er, at indstillingen gælder på **importtidspunktet**: ændrer brugeren sin liste
  mellem forhåndsvisning og import, følger importen den nye liste.
- **Importen kalder ikke Jira.** Den skriver de rækker klienten sender — samme kontrakt som skive
  2. Det holder importen hurtig og gør at det, der blev vist, er det, der bliver skrevet.

**Step 4: `externalUrl` på opgavesvaret**

I `TaskEndpoints`' afbildning til `TodoTask`: læs `JiraSettings` én gang pr. request og sæt

```csharp
    ExternalUrl = task.SourceId == "jira" ? jira.BrowseUrl(task.ExternalKey ?? "") : null,
```

Beregnet, ikke gemt. Læs indstillingerne **uden for** løkken over opgaverne — én forespørgsel, ikke
én pr. opgave.

**Step 5: Registrér i `TodoHost`**

```csharp
builder.Services.AddHttpClient<JiraTaskSource>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<JiraSettingsReader>();
```

og `app.MapJira();` ved siden af `app.MapRetro();`.

**Step 6: Kør testene**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~JiraEndpointsTests"
```

Forventet: PASS, 16 tests.

**Step 7: Nu skal drift-testen være grøn**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~ContractDriftTests"
```

Forventet: PASS. Fejler den på en operation for meget, så tjek at ingen ny rute uden for `/api/`
blev lagt til uden `.ExcludeFromDescription()` — se `CLAUDE.md`.

Ret samtidig doc-kommentaren i `ContractDocumentTests.cs`, som siger "the same 15 operations and
the same 22 schemas". Task 1 gjorde det til **21 og 30** (målt). Det er prosa, ikke en assertion, så
intet fejler — og netop derfor bliver et forkert tal stående som et holdepunkt nogen stoler på.

**Step 8: Læg de nye enum- og wire-værdier på wire-format-testen**

Drift-testen sammenligner kun stier og metoder. `excluded`-koden og `externalUrl` er skemaændringer,
og dem fanger den ikke. Læg en påstand i
`Wire_format_uses_the_names_the_contract_declares`: at et importeret Jira-svar indeholder
`"externalUrl":"` og at en udeladt række har `"excluded":"jira.excludedWaiting"`.

**Step 9: Se projektnøgle-vagten fejle**

Byt `ErrorCodes.JiraProjectKeyRequired`-grenen ud med et tilbagefald til
`assignee = currentUser()` uden projektled.

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~An_empty_project_key_refuses"
```

Forventet: FAIL — 200 frem for 400, og `Assert.Empty(jira.SearchRequests)` med én forespørgsel i.
Rul tilbage. **Rapportér fejlteksten.**

**Step 10: Commit**

```bash
git add src/Todo.Host/ tests/Todo.Api.Tests/
git commit -m "✨ Forhåndsvis og importér Jira-sager, med ventende statusser bag en indstilling"
```

---

## Task 7: Vagten — ingen test må kunne ringe til den rigtige instans

**Files:**
- Test: `tests/Todo.Api.Tests/NoRealInstanceTests.cs`

> **Rettet efter kørslen, 2026-08-18. Leveret i `088844e`; den kode er sandheden.** Fire fejl, og den
> tredje er en **syvende svag vagt** — den kunne godt fejle, men var blind for den mest sandsynlige
> halve fejl.
>
> 1. **`RepoPaths.Root` findes allerede** og bruges af `GeneratedCodeFreshnessTests`. Planens
>    `RepoRoot` ville have været et andet navn for samme ting. `RepoPaths.cs` er urørt.
> 2. **`scanned > 100` skjuler et konkret hul, ikke blot en slap margin.** Målt: scanningen når **172**
>    filer, men `src` alene er **110**. En rekursion der aldrig forlader `src` — eller en fejl i
>    skip-listen der spiser `tests`, hvor en indsat instans netop ville ligge — **består** tælle-
>    påstanden. At hæve tallet hjælper ikke: enhver tærskel der er sikker mod almindelig filtilvækst
>    ligger under 110. Lukket med en **strukturel** påstand i stedet: `["src", "tests", "contracts"]`
>    skal alle optræde blandt de topniveau-segmenter scanningen faktisk nåede. Samme form som
>    `LongIdMigrationTests`' lektion — kræv at de tabelnavne løkken nåede er alle tre.
>    Bevist: et brud der lagde `tests` i skip-listen scannede **117** filer og sejlede forbi
>    `scanned > 100`, men faldt på områdepåstanden.
> 3. **Udeladelsen af `.md` er en nødvendighed, ikke en præference — og af en anden grund end planen
>    gav.** Målt: designdokumentet nævner **ikke** instansen nogen steder, hverken `edora.dk` eller
>    `atlassian.net`. Den **eneste** markdown-fil der navngiver en forbudt vært er *denne plan*, og den
>    gør det **tre gange, alle som citat af vagten selv** — `ForbiddenHosts`-literalen og
>    brud 1's anvisninger. Scannedes markdown, ville skiveplanen være vagtens første offer i det
>    øjeblik vagten fandtes, selvrefererende og uden anden udvej end at slette planens dokumentation
>    af sig selv.
> 4. **Den forudsagte fejlbesked havde forkert stiseparator.** `Path.GetRelativePath` giver
>    backslashes på Windows, så beskeden er `tests\Todo.TestSupport\Jira\FakeJira.cs`. Kosmetisk — men
>    en forudsagt streng der ikke matcher, får nogen til at tro at vagten fyrede på den forkerte fil.
>
> **`atlassian.net` blev beholdt, og asymmetrien afgør det:** et falsk positivt koster en omskrevet
> kommentar, et falsk negativt koster produktionstrafik ved hver CI-kørsel. Designdokumentet
> diskuterer Cloud mod Data Center udførligt **uden** at skrive domænet, så den prosa der ville udløse
> den, findes ikke og har ikke skullet findes.
>
> **`wwwroot`, `dist` og `.angular` springes over, og det er rigtigt af en grund værd at skrive ned:**
> de er **genereret** output, så en vært derinde må være kommet fra en scannet kildefil, som vagten
> ser. Scannedes de, ville vagtens resultat afhænge af om nogen havde kørt `build-web.ps1` — grøn på
> en frisk klon, rød efter en bygning. Den slags test bliver slettet.
>
> Endeligt antal: **168** i Api, **83** i Core. Tre brud, tre forskellige påstande, tre forskellige
> linjer.

**Step 1: Skriv vagten**

Målt 2026-08-18: **nul** filer i repoet indeholder instansens værtsnavn i dag. Vagten er derfor
grøn fra første kørsel og kan ses fejle ved at skrive navnet ind — den beskytter mod en fremtidig
indsætning, ikke mod en nuværende.

```csharp
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The suite starts a fake Jira on loopback. Nothing stops a future test from pasting the real
/// instance in "just to check", and that test would then talk to production Jira on every CI run
/// and on every machine that clones the repo — with a real token if one happens to be configured.
///
/// The guard is a text search rather than a network policy on purpose: a network policy would have
/// to be installed in every test host, and one forgotten registration would make it silent. A
/// hostname cannot hide from a file scan.
///
/// What it does not cover, so nobody reads it as more than it is: markdown is not scanned. Prose
/// naming the instance is a documentation decision, not code that calls out, and scanning docs
/// would make this file's own plan its first offender. It also cannot see a hostname the user types
/// into the settings page at runtime — that is the whole point of the setting, and the guard is
/// about what ships in the repository.
/// </summary>
public class NoRealInstanceTests
{
    /// <summary>
    /// Split so a match names which one it hit. Add a host here rather than loosening the test.
    /// </summary>
    private static readonly string[] ForbiddenHosts = ["edora.dk", "atlassian.net"];

    private static readonly string[] SearchedExtensions =
        [".cs", ".ts", ".html", ".json", ".yaml", ".yml", ".ps1", ".cmd"];

    private static readonly string[] SkippedDirectories =
        ["node_modules", "bin", "obj", ".git", "wwwroot", "dist", ".angular"];

    /// <summary>
    /// This file has to spell the hostnames out to look for them, so it would otherwise be its own
    /// first offender. Skipping it means a hostname could hide in exactly one file in the
    /// repository — this one — and that is the cheapest honest trade available. The alternative,
    /// assembling the strings from fragments so the literal never appears, buys nothing and makes
    /// the list unreadable.
    /// </summary>
    private static readonly string ThisFile = $"{nameof(NoRealInstanceTests)}.cs";

    [Fact]
    public void No_source_file_names_a_real_jira_instance()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in Files(RepoPaths.RepoRoot))
        {
            scanned++;

            var text = File.ReadAllText(path);

            offenders.AddRange(
                from host in ForbiddenHosts
                where text.Contains(host, StringComparison.OrdinalIgnoreCase)
                select $"{Path.GetRelativePath(RepoPaths.RepoRoot, path)} names {host}");
        }

        // A scan that reached nothing also finds nothing. Without this, a wrong RepoRoot or a
        // typo in SearchedExtensions turns the guard into a test that always passes.
        Assert.True(
            scanned > 100,
            $"The scan only reached {scanned} files, so a green result proves nothing. Check "
                + $"RepoPaths.RepoRoot ({RepoPaths.RepoRoot}) and SearchedExtensions.");

        Assert.True(
            offenders.Count == 0,
            "A real Jira host is named in the repository. Tests must talk to FakeJira on "
                + "loopback, and a hostname in source is how a test suite ends up calling "
                + "production:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (SearchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), ThisFile, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var path in Files(child))
                {
                    yield return path;
                }
            }
        }
    }
}
```

Mangler `RepoPaths.RepoRoot`, så læg den til ved siden af `WebRoot`.

**Step 2: Kør den**

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~NoRealInstanceTests"
```

Forventet: PASS, og `scanned` godt over 100.

**Step 3: Se den fejle**

Læg `// jira: support.edora.dk` i `FakeJira.cs`.

```bash
dotnet test Todo.sln --filter "FullyQualifiedName~NoRealInstanceTests"
```

Forventet: FAIL med `tests/Todo.TestSupport/Jira/FakeJira.cs names edora.dk`. Fjern linjen.
**Rapportér fejlteksten.**

Se derefter *tælle*-påstanden fejle for sig: sæt `SearchedExtensions` til `[".nonexistent"]`.
Forventet: FAIL med "The scan only reached 0 files". Rul tilbage. To brud, to fejl —
`CLAUDE.md`'s punkt om at en mængdesammenligning kan bestå på ingenting.

**Step 4: Commit**

```bash
git add tests/Todo.Api.Tests/NoRealInstanceTests.cs tests/Todo.TestSupport/RepoPaths.cs
git commit -m "✅ Vagt: intet i repoet må navngive en rigtig Jira-instans"
```

---

## Task 8: Frontenden — indstillingssiden

**Files:**
- Modify: `src/Todo.Web/src/app/settings/settings-store.ts`, `settings.html`, `settings.ts`
- Create: `src/Todo.Web/src/app/jira/jira-store.ts`
- Modify: `src/Todo.Web/src/assets/i18n/da.json`, `en.json`
- Test: `src/Todo.Web/src/app/settings/settings-store.spec.ts`, `src/Todo.Web/src/app/jira/jira-store.spec.ts`
- Modify: `src/Todo.Web/public/i18n/da.json`, `en.json` — **`public/`, ikke `src/`**, se rettelse 4

> **Rettet efter kørslen, 2026-08-18. Leveret i `4c14d0e`; den kode er sandheden.** Otte fejl.
>
> 1. **Planens spec-skitser modsiger repoets faktiske mønster.** Der findes **ingen** fake af den
>    genererede klient: begge eksisterende spec-filer bruger `HttpTestingController` med de **rigtige**
>    klienter. `client.lastRequest` og `client.preview = {...}` findes ingen steder i repoet. Alle tre
>    skitser måtte skrives om til `http.expectOne(...)` og `JSON.parse(request.request.body)`.
>    Instruktionen om at følge det eksisterende mønster var rigtig; skitserne modsagde den.
> 2. **`/api/jira/test` og `/api/jira/preview` er POST, inte GET** — kontrakten siger hvorfor, men
>    planen gav intet hint, så specs'ene påstod GET. Præcis den slags der ellers "rettes" ved at
>    slette assertionen.
> 3. **`import(keys)` er ikke implementerbar.** `JiraImportRequest` bærer **hele rækker**; nøgler alene
>    efterlader intet at skrive ud fra. Blev `import(rows)`, med `RetroStore.import` som forlæg og en
>    test på at `isWaiting` **ikke** er på wiren.
> 4. **`git add src/Todo.Web/src/` ville have udeladt oversættelserne.** De bor i
>    `src/Todo.Web/public/i18n/`. Committer man som planen sagde, sender man komponenter der refererer
>    26 nøgler som aldrig blev committet — **og paritetstesten er grøn lokalt**, så intet ville sige
>    det. Det er en fejl der først dukker op hos den næste der kloner.
> 5. **Paritetstesten er en Vitest-spec**, `src/Todo.Web/src/app/i18n/translations.spec.ts`, ikke en
>    C#-test. Planen sendte agenten på jagt i `tests/`.
> 6. **"Ikke-optionel, så du behøver ikke `?? false`" holder kun hvis fixturet er komplet.** De
>    eksisterende settings-specs flushede `{ language: … }` og intet andet, så `jiraWaitingStatuses`
>    ville have været `undefined` i runtime og `@for`-løkken kastet i **hver** gammel test. Rettet i
>    fixturet frem for med `??`, hvilket bevarer Task 1's hensigt.
> 7. **"Slå knappen fra" kan ikke gælde forhåndsvisningsknappen her** — den skærm er Task 9. Blev et
>    `busy`-signal på `JiraStore` med kommentaren om hvorfor sekvenstælleren er unødvendig.
> 8. `apply()` kører igen ved **hver** Jira-gemning, fordi `save` er den ene vej. Billigt, men bevidst
>    frem for tilfældigt.
>
> **`Object.keys(store)`-vagten virker — målt.** Klassefelt-initialisatorer er egne enumerable
> properties, så et tilføjet `jiraToken`-signal fælder den. Men **navnetjekket omgås ved at omdøbe**,
> så vagten fik en anden halvdel: efter `setToken` kaldes hver egen funktionsværdi-property (metoder
> bor på prototypen, så det er præcis signalerne) og dens værdi gennemsøges for hemmeligheden. Målt med
> et felt kaldet `credential`: navnehalvdelen består, værdihalvdelen fejler.
>
> **Tre `@if`-grene er umålte af `ContrastTests`** — `jira-token-stored`, `jira-clear-token` og
> `jira-connection`. At nå dem kræver et gemt token i fixturet og en falsk Jira, altså Task 9/10.
> Klasserne er genbrugt ordret fra allerede målte elementer, så risikoen er lav — men efter repoets
> egen regel er de umålte.
>
> Endeligt antal: **Vitest 143 → 168** (+25: 12 `JiraStore`, 7 `SettingsStore`, 6 `Settings`). Api 168
> og Core 83 står stille.

**Step 1: Skriv de fejlende Vitest-specs**

De to der bærer noget, ud over den almindelige round-trip:

```ts
  it('should keep every setting in the request so saving one does not clear another', async () => {
    // The backend reads an absent field as "clear". SettingsStore.save must therefore carry all
    // five fields, exactly as TaskStore.update has to — slice 9 lost a stored DeferUntil to this.
    store.jiraBaseUrl.set('https://jira.test');
    store.jiraProjectKey.set('SAAS');
    store.jiraWaitingStatuses.set(['Afventer general']);
    store.jiraIncludeWaiting.set(true);

    await store.save({ language: 'en' });

    expect(client.lastRequest).toEqual({
      language: 'en',
      jiraBaseUrl: 'https://jira.test',
      jiraProjectKey: 'SAAS',
      jiraWaitingStatuses: ['Afventer general'],
      jiraIncludeWaiting: true,
    });
  });

  it('should never hold the token in a signal', () => {
    // The token is write-only: it goes out through setJiraToken and comes back only as
    // hasJiraToken. A signal holding it would put it in a component's template scope.
    expect(Object.keys(store)).not.toContain('jiraToken');
  });
```

Og på `JiraStore`, den ene der ellers ville blive gættet:

```ts
  it('should keep an excluded row visible rather than dropping it', async () => {
    client.preview = {
      total: 2,
      rows: [
        { key: 'SAAS-1', title: 'En', status: 'I gang', isWaiting: false, alreadyImported: false },
        {
          key: 'SAAS-2', title: 'To', status: 'Afventer general', isWaiting: true,
          alreadyImported: false, excluded: 'jira.excludedWaiting',
        },
      ],
    };

    await store.preview();

    expect(store.rows().length).toBe(2);
    // Selectable rows are the ones import will actually write.
    expect(store.selectable().map((r) => r.key)).toEqual(['SAAS-1']);
  });
```

**Step 2: Kør dem og se dem fejle**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

**Step 3: Udvid `SettingsStore`**

Fem nye signaler (`jiraBaseUrl`, `jiraProjectKey`, `jiraWaitingStatuses`, `jiraIncludeWaiting`,
`hasJiraToken`), og **intet signal til tokenet**. `save(changes)` bygger `current` af alle fem —
samme mønster som `TaskStore.update`, og af samme grund.

> **Fejlen er allerede i koden, og den er latent — målt 2026-08-18 under Task 3's review.**
> `settings-store.ts:35` sender i dag `new SettingsRequest({ language })` og **intet andet**.
> `PUT /api/settings` er en fuld erstatning, så et sprogskift **rydder** `jiraBaseUrl`,
> `jiraProjectKey`, `jiraWaitingStatuses` og `jiraIncludeWaiting`. Det sker ikke i dag, fordi UI'et
> endnu ikke kan sætte dem — men i det øjeblik denne task giver brugeren felterne, bliver den
> latente fejl en rigtig: konfigurér Jira, skift derefter sprog, og indstillingerne er væk.
> Det er ord for ord den konvention `CLAUDE.md` navngiver, og den fejl skive 9 tabte en `DeferUntil`
> til. **Skriv regressionstesten før felterne**, og lad den være en af de første i denne task frem
> for en eftertanke. `setToken(value)` og `clearToken()` kalder
de to nye endpoints og opdaterer `hasJiraToken` fra svaret.

Tokenfeltets værdi lever i komponentens lokale `signal('')` og ryddes efter et gemt token. Den må
ikke ligge i storen, hvor den ville overleve navigation.

**Step 4: `JiraStore`**

`testConnection()`, `loadStatuses()`, `preview()`, `import(keys)`. Signaler: `rows`, `total`,
`statuses`, `connection`, `error`. `selectable` er en `computed` over `rows` hvor `excluded` er
`null` og `alreadyImported` er `false`.

`load`-mønsteret med sekvenstæller er **ikke** nødvendigt her: intet i UI'et kan sætte to
forhåndsvisninger i luften på én gang, fordi knappen slås fra mens den kører. Skriv det i en
kommentar, så næste læser ikke tror det er en forglemmelse — og slå knappen fra, ellers **er** det
en forglemmelse.

**Step 5: Indstillingssiden**

Et `<section>` under sproget: basisURL (`type="url"`), projektnøgle, token (`type="password"`, med
"Gemt"-tilstand og et Ryd-knap når `hasJiraToken()`), "Test forbindelse"-knap med svaret ved siden
af, og statusvælgeren — en `Hent statusser`-knap, dernæst et afkrydsningsfelt pr. navn, plus
kontakten `jiraIncludeWaiting`.

Styling: **kun Tailwind utility-klasser**, `dark:`-modpart til hver `bg-*`/`text-*`/`border-*`,
dæmpet tekst er `text-gray-500 dark:text-gray-400`, og hvert felt har en `placeholder-*`-klasse —
uden arver det `currentColor` med ~54 % alfa og fejler i **begge** temaer. Uprefixede klasser er
den smalle udgave (~480 px).

Hver brugervendt streng er en nøgle i **både** `da.json` og `en.json`, `aria-label` og `title` med,
ellers fejler paritetstesten. Fejlkoderne fra Task 3 skal have en nøgle hver.

**Step 6: Kør Vitest og byg**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

**Step 7: Commit**

```bash
git add src/Todo.Web/src/
git commit -m "✨ Konfigurér Jira fra indstillingssiden, med tokenet der kun skrives"
```

---

## Task 9: Frontenden — importskærmen og linket til sagen

**Files:**
- Create: `src/Todo.Web/src/app/jira/jira-import.ts`, `jira-import.html`
- Modify: `src/Todo.Web/src/app/app.routes.ts`
- Modify: `src/Todo.Web/src/app/tasks/task-row.html`, `task-row.ts`
- Modify: `da.json`, `en.json`
- Test: `src/Todo.Web/src/app/jira/jira-import.spec.ts`

**Step 1: Skærmen**

Samme form som `retro-import`: en knap der henter forhåndsvisningen, en liste med et
afkrydsningsfelt pr. række, og en Importér-knap. Pr. række: nøgle, titel, Jira-status, deadline.

Tre tilstande skal siges med ord frem for at vise ingenting — det er skive 2's lektion:

- **Jira er ikke konfigureret.** Vis et link til indstillingerne, ikke en tom liste.
- **Ingen sager tildelt dig.** Det er et gyldigt svar, ikke en fejl.
- **Alle rækker er udeladt eller allerede importeret.** Sig hvilken af de to, og hvor mange.

En udeladt række vises **slået fra** med sin grund oversat fra `excluded` — samme mekanisme som
retro-importens "importeret tidligere".

**Step 2: Ruten**

`app.routes.ts` har i dag præcis tre ruter, og `ContrastTests` gennemgår dem alle. Denne bliver den
**fjerde**, og vagten skal udvides i Task 10 — ellers er skærmens farver umålte.

**Step 3: Linket til sagen på opgaverækken**

`externalUrl` fra kontrakten. Det skal være en **knap**, ikke et `<a href>`, og den skal gå gennem
`/api/system/open-link` — af samme grund som markdown-links i skive 4 og
dokumentationslinket: Photino-vinduet har ingen adresselinje og ingen tilbage-knap, så en
navigation væk er enkeltrettet.

`@if (task().externalUrl != null)` indsnævrer **ikke** — bind med `@let` først, ikke `as`.

Og pas på: mærkaten indgår i rækkeknappens tilgængelige navn, som `TaskListScreen.RowTitled`
matcher **præcist**. Ligger linket inde i rækkeknappen, holder den op med at matche, og fejlen
ligner en manglende række. Læg det **uden for** knappen, eller giv det `aria-hidden` og et
`aria-label` på knappen.

**Step 4: Kør Vitest, byg, commit**

```bash
npm.cmd run test --prefix src\Todo.Web -- --watch=false
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
git add src/Todo.Web/src/
git commit -m "✨ Importér Jira-sager fra en skærm, og åbn sagen i systemets browser"
```

---

## Task 10: E2E, kontrastvagten og dokumentationen

**Files:**
- Create: `tests/Todo.E2E/JiraImportJourneyTests.cs`, `JiraImportScreen.cs`
- Modify: `tests/Todo.E2E/ContrastTests.cs`, `SettingsScreen.cs`
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: E2E-rejsen**

Playwright kan ikke starte en `FakeJira` i hostens proces, så rejsen **opsnapper** kaldene med
`page.RouteAsync` — samme greb som `/api/system/open-link`:

- `**/api/jira/preview` → svar med tre rækker, hvoraf én er udeladt.
- `**/api/jira/import` → svar `{ imported: 1, skipped: 0 }`.
- `**/api/system/open-link` → **afbryd**, og læs URL'en. Uden det åbner hver testkørsel en rigtig
  browser.

Fire påstande:
1. Uden konfiguration siger skærmen det og linker til indstillingerne.
2. Med en forhåndsvisning står den udeladte række der, slået fra, med sin grund.
3. Import skriver kun de valgte, og skærmen siger hvor mange.
4. Linket på en importeret opgave beder `/api/system/open-link` om `…/browse/SAAS-1`, og det er en
   `BUTTON` — `Assert.Equal("BUTTON", await link.EvaluateAsync<string>("el => el.tagName"))`, samme
   påstand som dokumentationslinket, og det eneste der stopper en "forenkling" til et `<a href>`.

**Step 2: Udvid kontrastvagten til den fjerde skærm**

`ContrastTests` går i dag appens **tre** skærme igennem i begge temaer. Jira-importen er den
fjerde. Rejsen skal desuden nå de nye betingede grene, for **en `@if`-gren er umålt indtil
fixturet har noget i den tilstand og rejsen åbner den**:

- den udeladte række (dæmpet tekst og "slået fra"-tilstanden),
- "ikke konfigureret"-beskeden,
- Jira-sektionen på indstillingssiden, inklusive "Gemt"-tilstanden på tokenfeltet,
- linket på opgaverækken.

Opdatér kommentaren i `ContrastTests` fra "tre skærme" til fire, og ret **også** sætningen i
`CLAUDE.md` og designdokumentets afsnit 10 — begge steder står tallet tre skrevet ned.

**Step 3: Kør alt**

```bash
dotnet test Todo.sln
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

**Step 4: Skriv tallene op**

Tæl selv frem for at gætte, og skriv hvad hver blok lagde til, i `CLAUDE.md`'s **Testtal**.
Udgangspunktet er **38 Core, 121 Api, 25 E2E, 143 Vitest**.

**Step 5: Dokumentationen — kun det der blev målt**

Fem påstande skal skrives ned, og hver af dem er noget der kostede tid at finde ud af:

1. **Wiki-markups `*x*` er fed, markdowns er kursiv.** En gennemgang der lader teksten passere,
   degraderer hver fed sætning i hver importeret beskrivelse. Kodeblokke skal beskyttes **før**
   linjereglerne kører, ellers bliver en `*` i en kodeblok til en punktopstilling.
2. **`statuscategorychangedate` findes ikke i DC 10.3.24, og changeloggen har offset.**
   `DateTimeOffset` ind, `UtcDateTime` ud, og en DTO der binder `created` som `DateTime` har
   allerede tabt offsettet før koden ser den.
3. **Statusnavnene er inkonsekvente, og kategorien kan ikke hjælpe.** Fem `Afventer *`, én
   `Venter på support`, alle seks i `indeterminate` sammen med `I gang`. Derfor en eksplicit liste.
4. **En tom projektnøgle må ikke betyde alle projekter.** PAT'en ser fire projekter, kundeprojektet
   `KK` iberegnet.
5. **Tokenet skal have sit eget endpoint**, fordi `PUT /api/settings` er en fuld erstatning der
   læser et fraværende felt som "ryd" — samme fejl som `DeferUntil` i skive 9.

Og hvad skiven **ikke** gjorde, så det ikke ser ud som et hul: `Ext*`-felterne,
`TitleOverridden`, `LastSyncedAt` og afstemningen ligger i skive 14; `ICredentialStore` blev ikke
oprettet. **Ret afsnit 6**, som nævner den, frem for at lade dokumentet love noget koden ikke har.

**Step 6: En opgave til brugeren, ikke til en agent**

Designdokumentets afsnit 10 siger, at **ADO-mentions skal verificeres i skive 11**. Det kræver et
kald mod brugerens egen ADO-instans og kan ikke gøres herfra. Skriv det i `HANDOFF.md` som næste
måling, med den WIQL afsnit 6 foreslår, og med noten om **aldrig at lægge et token direkte i en
kommandolinje** — brug `$env:NAME`.

**Step 7: Commit**

```bash
git add tests/ CLAUDE.md docs/
git commit -m "✅ E2E på Jira-importen, kontrastvagten på fjerde skærm og lektionerne skrevet ned"
```

---

## Hvad der kan gå galt, og hvad man så skal gøre

**Konverteringen i Task 2 er nu målt — og lektionen var, at regexerne ikke var problemet.**
Alle elleve mønstre holdt ordret. Det der fejlede, var **kompositionen og hvad renderen gør med
outputtet**: `----` blev en setext-overskrift, indlejrede `##`-punkter blev `<h2>`, og `{noformat}`
blev formateret som prosa. Fundet ved at føre konverterens output gennem appens egen `marked` med
appens flag, ikke ved at læse mønstrene. **Gør det samme næste gang der lægges en regel til.**

**Fire konstruktioner passerer bevidst som rå tekst, og tre af dem er sikre.** Tabeller
(`||h||h||`) bliver synlig rørsuppe, fordi GFM kræver en `|---|`-skillelinje som Jira aldrig
udsender; `-x-` og `^2^` står uændret. Den fjerde er **ikke** sikker: `~x~` er *subscript* i Jira,
men GFM læser en enkelt tilde som **gennemstregning**, så meningen inverteres. Det er den modsatte
af hvad man gætter — `-x-` ser farlig ud og er harmløs, `~x~` ser harmløs ud og er ikke.
Begrænsningerne står i klassens doc-kommentar; ser en rigtig beskrivelse forkert ud, hører dens
form til i `WikiMarkupTests`.

**`expand=changelog` på `/search` er ikke målt.** Planen henter changeloggen pr. sag, hvilket
virker. Vil man optimere, så **mål først** — og kun for de sager der er ventende, hvilket i praksis
er nogle få.

**Statuslisten kræver en virkende forbindelse.** Er tokenet forkert, kan brugeren ikke vælge
statusser og ser en tom liste. Derfor står "Test forbindelse" **over** vælgeren på siden, og
vælgeren siger hvorfor den er tom.

**`/rest/api/2/project/{key}/statuses` giver statusser pr. issuetype, med dubletter.** Målt
2026-08-18 gav den elleve **distinkte** navne. En kilde der ikke flader ud og fjerner dubletter,
viser samme navn flere gange i vælgeren.
