# Ugentlig gennemgang — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** En sjette skærm på `/review` der fører brugeren gennem fem grupper i GTD's rækkefølge,
plus en påmindelse på navigationslinket der er **beregnet** frem for udløst.

**Designet er `docs/plans/2026-08-27-weekly-review-design.md`.** Læs det først — de tolv
beslutninger står i dets afsnit 0 med hvem der traf dem, og hver af dem har en begrundelse der
falder den anden vej hvis man gætter.

**Arkitektur:** Forfaldstilstanden er en ren beregning i `Todo.Core` over (ugedag, klokkeslæt,
sidste gennemgang, nu) — ingen timer, intet baggrundsjob. Tre rækker i `Setting`, **ingen
migrering**. Skærmen genbruger `li[appTaskRow]` og læser `TaskStore`s rå liste, så en beslutning
flytter rækken ud af dens gruppe uden en linje ekstra.

**Teknologi:** .NET 10 minimal APIs, EF Core 10 + SQLite, Angular 22 med signal-stores, Tailwind 4,
xUnit, Playwright, Vitest.

**Planen kan være forkert.** Over fem leverancer fandt gennemgangene omkring hundrede planfejl og
nul udførelsesfejl. Mål frem for at tro på det der står her, og **ret planfilen** når en måling
modsiger den.

## Maskinens fælder der rammer netop denne plan

- `pwsh` findes ikke. `npm.cmd`/`ng.cmd`, aldrig `npm`/`ng`.
- **Brug `Edit`, ikke `sed` eller `Write`, på eksisterende filer.** Begge skriver LF i denne
  CRLF-arbejdskopi, og `git diff` viser derefter ingen ændring. Skal `Write` bruges, konvertér
  tilbage og verificér med `tr -dc '\r' < fil | wc -c` mod `tr -dc '\n' < fil | wc -c` — **ikke**
  med `grep -cv $'\r$'`, som giver et falsk bestået.
- **Kør `scripts\build-web.ps1` før E2E.** E2E-suiten bygger ikke Angular, så Playwright tester
  ellers den forrige frontend uden at noget ser forkert ud.
- **En umappet rute under `/api/` giver ikke 404.** GET giver `200` med `index.html` i kroppen
  (`'<' is an invalid start of a value`), POST og PUT giver `405`. Forvent det, når en rute-test
  skal fejle først.
- **`element.dataset.testid` compilerer ikke i en spec-fil.** Brug `getAttribute('data-testid')`.
- Én Vitest-fil køres med `--include` og en **sti**:
  `npm.cmd run test --prefix src\Todo.Web -- --watch=false --include src/app/review/review-store.spec.ts`.

## Rettelse til designet, målt før planen blev skrevet

Designets afsnit 5 siger at ugedags-`<select>`'en skal have `bg-gray-50 dark:bg-gray-800`. **Det er
forkert par.** Målt: appens to `<select>` har hver sit, og de matcher deres omgivelser —
sprogvælgeren på indstillingssiden er `bg-white dark:bg-gray-800` (sidens baggrund), statusvælgeren
i detaljepanelet er `bg-gray-50 dark:bg-gray-800` (panelets). Reglen er "mal en uigennemsigtig
baggrund der matcher det du står på", ikke ét fast par. Ugedagsvælgeren står på indstillingssiden
ved siden af sprogvælgeren og skal derfor have **`bg-white dark:bg-gray-800`**.

**Designfilen er allerede rettet** i samme commit som denne plan, så der er ingen Task 0 at køre —
afsnit 5 bærer nu det rigtige par og en note om hvad den sagde før. Nævnt her frem for slettet, fordi
det er en måling der modsagde designet, og planfilerne er repoets hukommelse.

---

## Task 1: `ReviewSchedule` — forfaldsberegningen

**Filer:**
- Opret: `src/Todo.Core/Review/ReviewDefaults.cs`
- Opret: `src/Todo.Core/Review/ReviewSchedule.cs`
- Opret: `src/Todo.Core/Review/ReviewStatus.cs`
- Ret: `src/Todo.Core/Time/IClock.cs`, `src/Todo.Core/Time/SystemClock.cs`
- Ret: `tests/Todo.TestSupport/Time/FixedClock.cs`
- Test: `tests/Todo.Core.Tests/Review/ReviewScheduleTests.cs`

**Trin 1 — skriv de fejlende tests.** Klokken skal have en **kendt, ikke-nul** forskel mellem
`LocalNow` og `UtcNow`, ellers består tidszonevagten på en UTC-maskine uanset implementeringen.

```csharp
using Todo.Core.Review;
using Todo.Core.Time;

namespace Todo.Core.Tests.Review;

public class ReviewScheduleTests
{
    private static readonly TimeOnly ThreePm = new(15, 0);

    /// <summary>
    /// Local and UTC deliberately differ by two hours, so a comparison built on the wrong one
    /// fails on every machine rather than only outside UTC. Without the offset the guard below
    /// would pass in CI and say nothing.
    /// </summary>
    private sealed class OffsetClock(DateTime localNow) : IClock
    {
        public DateTime LocalNow { get; } = localNow;

        public DateTime UtcNow { get; } = localNow.AddHours(-2).ToUniversalTime();

        public DateOnly Today { get; } = DateOnly.FromDateTime(localNow);
    }

    [Fact]
    public void Never_reviewed_is_due_at_once()
    {
        // A Wednesday, so the most recent Friday 15:00 is five days back.
        var clock = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Local));

        var status = ReviewSchedule.For(DayOfWeek.Friday, ThreePm, lastCompletedAt: null, clock);

        Assert.True(status.Due);
        Assert.Null(status.LastCompletedAt);
    }

    [Fact]
    public void Not_due_before_the_chosen_time_on_the_chosen_day()
    {
        // Friday 14:59: this week's moment has not arrived, so last week's is the boundary - and
        // the review taken after it satisfies the period.
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 14, 59, 0, DateTimeKind.Local));
        var last = new DateTime(2026, 8, 21, 16, 0, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.False(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, last, clock).Due);
    }

    [Fact]
    public void Due_once_the_chosen_time_has_passed()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 15, 0, 0, DateTimeKind.Local));
        var last = new DateTime(2026, 8, 21, 16, 0, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.True(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, last, clock).Due);
    }

    /// <summary>
    /// The cadence decision from the design's section 2, and the guess falls the other way: a
    /// "seven days since the last one" rule would let the habit slide a day forward every week
    /// until the chosen weekday stopped meaning anything.
    /// </summary>
    [Fact]
    public void A_review_taken_mid_week_satisfies_this_period_but_not_the_next()
    {
        var tuesday = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();

        // Wednesday: the Tuesday review is inside the current period, so nothing is due.
        var midWeek = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Local));
        Assert.False(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, tuesday, midWeek).Due);

        // Friday 15:01, three days later: the boundary has moved, so it is due again.
        var friday = new OffsetClock(new DateTime(2026, 8, 28, 15, 1, 0, DateTimeKind.Local));
        Assert.True(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, tuesday, friday).Due);
    }

    /// <summary>
    /// The boundary is built from local time, because "Friday 15:00" is a local statement while the
    /// stored timestamp is UTC. OffsetClock puts UTC two hours behind, so a boundary built from
    /// UtcNow lands on the wrong side here.
    /// </summary>
    [Fact]
    public void The_boundary_is_local_time_rather_than_UTC()
    {
        // Local Friday 15:30, so UTC is 13:30 - before the chosen time. Due either way is not the
        // question: the question is which moment nextDueAt names.
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 15, 30, 0, DateTimeKind.Local));
        var last = new DateTime(2026, 8, 28, 15, 20, 0, DateTimeKind.Local).ToUniversalTime();

        var status = ReviewSchedule.For(DayOfWeek.Friday, ThreePm, last, clock);

        // Reviewed ten minutes after this week's moment, so the next one is a week out.
        Assert.False(status.Due);
        Assert.Equal(
            new DateTime(2026, 9, 4, 15, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            status.NextDueAt);
    }

    [Fact]
    public void Next_due_is_the_coming_occurrence_rather_than_the_past_one()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Local));

        var status = ReviewSchedule.For(DayOfWeek.Friday, ThreePm, lastCompletedAt: null, clock);

        Assert.Equal(
            new DateTime(2026, 8, 28, 15, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            status.NextDueAt);
    }

    [Fact]
    public void The_chosen_day_can_be_Sunday()
    {
        // 0 is a real weekday, not an absent value - the whole reason the contract needs default: 5.
        var clock = new OffsetClock(new DateTime(2026, 8, 30, 15, 1, 0, DateTimeKind.Local));

        Assert.True(ReviewSchedule.For(DayOfWeek.Sunday, ThreePm, lastCompletedAt: null, clock).Due);
    }
}
```

**Trin 2 — se dem fejle.**

```
dotnet test tests\Todo.Core.Tests --filter ReviewScheduleTests
```

Forventet: compilerfejl, `The type or namespace name 'Review' does not exist in the namespace
'Todo.Core'` og `'IClock' does not contain a definition for 'LocalNow'`.

**Trin 3 — implementér.** `IClock` får ét medlem:

```csharp
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Now, in the machine's own zone. Its own member rather than a conversion of
    /// <see cref="UtcNow"/>: a test that derived local time through TimeZoneInfo.Local would pass or
    /// fail depending on where it ran, and a guard that fails for the wrong reason is worse than no
    /// guard. <see cref="Today"/> is already local and has been since it was written; this adds the
    /// time of day it cannot carry.
    /// </summary>
    DateTime LocalNow { get; }

    DateOnly Today { get; }
}
```

`SystemClock` svarer `DateTime.Now`. `FixedClock` får et **valgfrit** klokkeslæt, så de
eksisterende kaldesteder bliver ved at compilere:

```csharp
public sealed class FixedClock(DateOnly today, TimeOnly? at = null) : IClock
{
    public DateTime UtcNow { get; } = today.ToDateTime(at ?? TimeOnly.MinValue, DateTimeKind.Utc);

    public DateTime LocalNow { get; } = today.ToDateTime(at ?? TimeOnly.MinValue, DateTimeKind.Local);

    public DateOnly Today { get; } = today;
}
```

`ReviewDefaults`, med standarden **ét** sted som `AdoDefaults`:

```csharp
namespace Todo.Core.Review;

/// <summary>
/// What an absent settings row reads as. One spelling, because the reader and the writer have to
/// agree: ReviewSettingsReader turns the absence of a row into these values, and SettingsEndpoints
/// stores a row only when the request asks for something else. Two spellings would let a saved
/// default become a stored row, and a stored row is what the two language tests in
/// SettingsEndpointsTests assert cannot appear.
/// </summary>
public static class ReviewDefaults
{
    /// <summary>
    /// Friday, which is Allen's own recommendation - the week is still open enough to act on what
    /// the review turns up. The user's decision 2026-08-27, and it is a setting they can move.
    ///
    /// Written down twice on purpose: here, and as <c>default: 5</c> on
    /// SettingsRequest.reviewWeekday in the contract. The contract's copy is what makes an absent
    /// field bind to Friday rather than to 0 - and 0 is Sunday, a real weekday, so without it a save
    /// that never mentioned the review would move the habit to Sunday.
    /// </summary>
    public const DayOfWeek Weekday = DayOfWeek.Friday;

    /// <summary>Mid-afternoon, so the reminder does not arrive before the day has happened.</summary>
    public static readonly TimeOnly TimeOfDay = new(15, 0);
}
```

`ReviewStatus` er en record med `bool Due`, `DateTime? LastCompletedAt`, `DateTime NextDueAt`.

```csharp
namespace Todo.Core.Review;

using Todo.Core.Time;

public static class ReviewSchedule
{
    /// <summary>
    /// Whether the weekly review is owed, computed rather than fired. Nothing runs at midnight and
    /// there is no timer - the same rule as DeadlineBuckets.For, and for the same reason: the app has
    /// no tray and no background sync, so a moment that "fires" on Friday at 15:00 hits nothing when
    /// the window is shut. This survives the app having been closed for three weeks.
    ///
    /// The cadence is a fixed weekday rather than seven days since the last one, and the guess falls
    /// the other way. A review taken on Tuesday satisfies the period that is running, so the marker
    /// goes; when Friday 15:00 arrives the boundary has moved past Tuesday and it is owed again. A
    /// seven-day rule would let the habit slide a day forward every week until the weekday the user
    /// chose stopped meaning anything.
    ///
    /// Local time throughout, converted to UTC only to compare: "Friday 15:00" is a local statement
    /// while lastCompletedAt is UTC by the repo's convention. One frame, not two.
    /// </summary>
    public static ReviewStatus For(
        DayOfWeek weekday, TimeOnly timeOfDay, DateTime? lastCompletedAt, IClock clock)
    {
        var now = clock.LocalNow;
        var todayAt = now.Date + timeOfDay.ToTimeSpan();

        // How many days back the most recent occurrence is. On the chosen day it is today when the
        // time has passed and a full week back when it has not - so the boundary never sits in the
        // future, which is what lets a null lastCompletedAt read as due.
        var daysBack = ((int)now.DayOfWeek - (int)weekday + 7) % 7;
        var due = daysBack == 0 && now < todayAt
            ? todayAt.AddDays(-7)
            : todayAt.AddDays(-daysBack);

        var dueUtc = DateTime.SpecifyKind(due, DateTimeKind.Local).ToUniversalTime();

        return new ReviewStatus(
            Due: lastCompletedAt is null || lastCompletedAt < dueUtc,
            LastCompletedAt: lastCompletedAt,
            NextDueAt: DateTime.SpecifyKind(due.AddDays(7), DateTimeKind.Local).ToUniversalTime());
    }
}
```

**Trin 4 — se dem bestå.** `dotnet test tests\Todo.Core.Tests --filter ReviewScheduleTests` →
7 passed.

**Trin 5 — se de to skarpe vagter fælde.** Kræv den **ordrette** fejltekst i rapporten.

1. Skift kadencen: erstat `daysBack`-grenene med `lastCompletedAt < now.AddDays(-7)`.
   Forventet: `A_review_taken_mid_week_satisfies_this_period_but_not_the_next` fejler på den anden
   påstand (`Assert.True() Failure`).
2. Byg grænsen af UTC: `var now = clock.UtcNow;`.
   Forventet: `The_boundary_is_local_time_rather_than_UTC` fejler på `Assert.Equal` med to
   tidsstempler to timer fra hinanden.

Ret tilbage efter hver.

**Trin 6 — commit.**

```bash
git add src/Todo.Core/Review src/Todo.Core/Time tests/Todo.TestSupport/Time tests/Todo.Core.Tests/Review
git commit -m "✨ Gennemgangens forfald beregnes af ugedag og klokkeslæt frem for at blive udløst"
```

---

## Task 2: Kontrakten og den genererede klient

**Filer:**
- Ret: `contracts/openapi.yaml`
- Genereret: `src/Todo.Web/src/app/api/todo-client.ts` (committes)

**Trin 1 — læg to felter på `SettingsRequest` og `SettingsResponse`.** Læs
`adoDefaultDeadlineDays`' beskrivelse i kontrakten først: den forklarer hvorfor `default:` er
bærende, og **hvorfor `SettingsResponse` bevidst ikke har en**. Gentag begge halvdele.

```yaml
        reviewWeekday:
          type: integer
          format: int32
          minimum: 0
          maximum: 6
          default: 5
          description: >-
            The weekday the weekly review is suggested on, 0 = Sunday. 5 is Friday.

            The default is load-bearing for the same reason adoDefaultDeadlineDays' is, and the
            trap here is sharper: this is a full replacement, System.Text.Json gives 0 for an
            absent int, and 0 is Sunday — a real weekday, not a missing value. Without the default
            a save that never mentioned the review would move the habit to Sunday. NSwag turns the
            default into a property initializer, which the deserialiser leaves alone for an absent
            field, so absent binds to Friday and a deliberate Sunday stays Sunday.

            SettingsResponse deliberately carries no default, the same as adoDefaultDeadlineDays:
            an initializer there would let a handler that never assigned the field answer 5 and
            make the test for it unfailable.
        reviewTimeOfDay:
          type: string
          pattern: '^([01][0-9]|2[0-3]):[0-5][0-9]$'
          default: '15:00'
          description: >-
            The time of day the review is suggested at, HH:mm in the machine's own zone. Local
            rather than UTC on purpose: "Friday at three" is a statement about the user's
            afternoon, and reviewLastCompletedAt is the UTC half of the comparison.
```

Begge navne skal **også** i `required` på `SettingsResponse` — de er ikke nullable.

**Trin 2 — læg de to ruter på.** `GET /api/review` og `POST /api/review/complete`, begge med
`ReviewStatusResponse` (`due`, `lastCompletedAt` nullable, `nextDueAt`), tag `Review`.
`reviewLastCompletedAt` er **ikke** på `SettingsResponse` — den bor kun her.

**Trin 3 — generér.**

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

**Trin 4 — verificér.** `dotnet test tests\Todo.Api.Tests --filter "Freshness|ContractDrift"`.
Forventet: friskheden er grøn (hashen er opdateret), **drift-testen fejler** på to operationer
serveren ikke har endnu. Det er rigtigt og lukkes i Task 3–4.

**Trin 5 — commit.** `git add contracts src/Todo.Web/src/app/api` →
`📝 Kontrakten kender gennemgangens to indstillinger og dens to ruter`

---

## Task 3: `ReviewSettingsReader` og de to indstillinger

**Filer:**
- Opret: `src/Todo.Core/Review/ReviewSettings.cs`, `src/Todo.Core/Review/ReviewSettingsReader.cs`
- Ret: `src/Todo.Core/Settings/SettingKeys.cs`
- Ret: `src/Todo.Host/Endpoints/SettingsEndpoints.cs`, `src/Todo.Host/TodoHost.cs` (registrér læseren)
- Test: `tests/Todo.Api.Tests/ReviewSettingsTests.cs`

Læs **`AdoSettingsReader.DeadlineDays`** før du skriver læseren. Reglen er felt for felt den samme:
standarden i læselaget, en bevidst værdi bevares, en ulæselig eller uden-for-interval-værdi læses
som standarden.

**Trin 1 — de fejlende tests.** Fire påstande, og den tredje er den der vagter en test i en anden fil:

```csharp
[Fact] public async Task An_absent_weekday_row_reads_as_Friday()
[Fact] public async Task A_deliberate_Sunday_is_preserved_rather_than_folded_to_the_default()
[Fact] public async Task Saving_the_default_weekday_stores_no_row()      // Assert.Empty(StoredSettings())
[Fact] public async Task An_unreadable_time_of_day_reads_as_three_in_the_afternoon()
```

Plus wire-formatet på det **rå** JSON, fordi drift-testen kun sammenligner stier og metoder:

```csharp
[Fact]
public async Task Wire_format_uses_the_names_the_contract_declares()
{
    var body = await (await Client.GetAsync("/api/settings")).Content.ReadAsStringAsync();

    Assert.Contains("\"reviewWeekday\":5", body);
    Assert.Contains("\"reviewTimeOfDay\":\"15:00\"", body);
}
```

**Trin 2 — se dem fejle.** Forventet: `An_absent_weekday_row_reads_as_Friday` fejler, fordi
`SettingsResponse.ReviewWeekday` er `0`; wire-testen fejler på at `"reviewWeekday"` ikke findes.

**Trin 3 — implementér.** Nøglerne `review.weekday` og `review.timeOfDay` i `SettingKeys`. Læseren
selekterer på `review.`-præfikset som `AdoSettingsReader` gør på `ado.`. I `SettingsEndpoints`:

```csharp
// The default is the absence of a row, so asking for the default removes it - the same rule as
// adoDefaultDeadlineDays, and the same two whole-table tests in SettingsEndpointsTests go red on a
// row left behind. It works because the contract declares default: 5, so an absent field arrives as
// Friday and a deliberate Sunday arrives as Sunday. Without it both would arrive as 0, and 0 is
// Sunday rather than "missing" - so a language change would have moved the habit.
await StoreAsync(
    db,
    SettingKeys.ReviewWeekday,
    request.ReviewWeekday == (int)ReviewDefaults.Weekday
        ? null
        : request.ReviewWeekday.ToString(CultureInfo.InvariantCulture));
```

Validering: `reviewWeekday` uden for 0–6 afvises med en ny `ErrorCodes.ReviewWeekdayInvalid`, og et
uparsebart klokkeslæt med `ErrorCodes.ReviewTimeOfDayInvalid`. **Begge koder skal have en
`errors.*`-nøgle i *begge* sprogfiler**, ellers fejler `ErrorCodeTranslationTests` — og nøglerne må
**ikke** indeholde `{{value}}`, som `api-error-message.ts` sender urenderet videre.

`ReadAllAsync` udvides med de to felter, læst af `ReviewSettings` frem for af rækkerne.

**Trin 4 — se dem bestå**, og kør `dotnet test tests\Todo.Api.Tests` **helt**: Task 3 er det sted
`Assert.Empty(StoredSettings())` vælter, hvis standarden gemmes.

**Trin 5 — se vagten fælde.** Fjern `? null :` fra `StoreAsync`-kaldet ovenfor, så standarden
gemmes ubetinget. **Målt: fem røde, ikke tre** — planen forudsagde tre, og de to ekstra er fundet:

```
SettingsEndpointsTests.Clearing_the_language_removes_the_row_rather_than_storing_null   Assert.Empty() Failure: Collection was not empty
SettingsEndpointsTests.Choosing_a_language_twice_overwrites_the_one_row                 Assert.Single() Failure: The collection contained 2 items
SettingsEndpointsTests.An_empty_list_of_delegates_removes_the_row                       Assert.Single() Failure: The collection contained 2 items
AdoSettingsEndpointsTests.Asking_for_the_default_deadline_leaves_no_row_behind          Assert.Empty() Failure: Collection was not empty
ReviewSettingsTests.Saving_the_default_weekday_stores_no_row                            Assert.Empty() Failure: Collection was not empty
```

Tre filer, som mellem sig hverken nævner gennemgangen eller hinanden. **Det er tellet på at reglen er
bærende** — og at den er vagtet fra flere retninger end nogen der skriver en ny indstilling ville
kigge efter.

**Og en fælde undervejs, målt frem for gættet:** `DateTimeStyles.RoundtripKind | AdjustToUniversal`
er en **ulovlig** kombination der kaster `ArgumentException` i runtime frem for at fejle i
compileren — den gav `500` på hver læsning af `GET /api/settings`. `RoundtripKind` **alene** er det
der gør et lagret `Z` til `DateTimeKind.Utc`. Fejlen var kun synlig i hostens log, ikke i testens
udskrift, som bare sagde `Response status code does not indicate success: 500`.

**Trin 6 — commit.** `✨ Ugedag og klokkeslæt er indstillinger, og standarden gemmer ingen række`

---

## Task 4: `/api/review` og `/api/review/complete`

**Filer:**
- Opret: `src/Todo.Host/Endpoints/ReviewEndpoints.cs`
- Ret: `src/Todo.Host/TodoHost.cs` (`MapReview()`)
- Ret: `src/Todo.Core/Settings/SettingKeys.cs` (`review.lastCompletedAt`)
- Test: `tests/Todo.Api.Tests/ReviewEndpointsTests.cs`

**Trin 1 — de fejlende tests.** Den anden er vagten på hele endpoint-adskillelsen:

```csharp
[Fact] public async Task A_fresh_database_owes_a_review()

/// <summary>
/// The completion timestamp does not go through PUT /api/settings, and this is the guard on that.
/// A full replacement reads an absent field as clear, so were it a field on SettingsRequest, saving
/// any other setting would wipe it - exactly the trap the Jira token has its own route for.
/// </summary>
[Fact] public async Task Completing_the_review_survives_a_later_settings_save()
{
    await Client.PostAsync("/api/review/complete", null);
    await PutJsonAsync(new { language = "en" });

    var after = await Client.GetFromJsonAsync<ReviewStatusResponse>("/api/review");

    Assert.False(after!.Due);
    Assert.NotNull(after.LastCompletedAt);
}

[Fact] public async Task Completing_the_review_writes_the_clock()  // FixedClock, exact timestamp
[Fact] public async Task Wire_format_uses_the_names_the_contract_declares()  // "due":true osv.
```

**Trin 2 — se dem fejle.** Forventet: **ikke** 404. `GET /api/review` giver `200` med
`index.html` i kroppen, altså `'<' is an invalid start of a value`; `POST /api/review/complete`
giver `405 Method Not Allowed`, fordi `MapFallbackToFile` gør krav på stien for GET.

**Trin 3 — implementér.** Begge handlere læser `ReviewSettingsReader` og `IClock` og svarer
`ReviewSchedule.For(...)`. `POST` skriver `clock.UtcNow` som ISO-8601 med `InvariantCulture` og
svarer den nye tilstand, så klienten ikke skal hente igen.

**Trin 4 — se dem bestå**, og drift-testen med: `dotnet test tests\Todo.Api.Tests`.

**Trin 5 — se vagten fælde.** Læg `reviewLastCompletedAt` på `SettingsRequest` og skriv den fra
`PUT /api/settings`. Forventet: `Completing_the_review_survives_a_later_settings_save` fejler på
`Assert.False(after.Due)` — altså `Assert.False() Failure`.

**Trin 6 — commit.** `✨ Gennemgangen har to ruter, og afslutningen ligger uden for indstillingerne`

---

## Task 5: `showSomeday` bliver et visningsfilter

**Filer:**
- Ret: `src/Todo.Web/src/app/tasks/task-store.ts`
- Ret: `src/Todo.Web/src/app/tasks/task-store.spec.ts`

Designets afsnit 4 har begrundelsen og de tre afviste alternativer. **`showCompleted` bliver
uændret en indlæsningsparameter** — Fuldført vokser for evigt, Måske er kurateret.

**Trin 1 — skriv den nye påstand og omskriv race-vagten.**

```ts
it('should have parked tasks without the switch being on', async () => {
  const load = store.load();
  TestBed.inject(HttpTestingController)
    .expectOne('/api/tasks?includeCompleted=false&includeSomeday=true')
    .flush(new Blob([JSON.stringify({ items: [] })]));
  await load;

  expect(store.showSomeday()).toBe(false);
});
```

Race-vagten skal **blive** — `update`, `remove` og `load` kan stadig race — men drives af to
`setShowCompleted`-kald frem for af én af hver. Skriv i kommentaren **hvorfor** den blev omskrevet,
ellers ligner den næste gang en test der har mistet sin pointe.

**Trin 2 — se den fejle.** Forventet: `expectOne` finder
`includeSomeday=false` og fejler med *Expected one matching request … found none*.

**Trin 3 — implementér.** `load()` sender `true` for `includeSomeday` uanset signalet; signalet
bliver kun læst af `task-list.ts`' `someday()`. `setShowSomeday` genindlæser ikke længere.

**Trin 4 — kør hele Vitest-suiten**, ikke kun filen: `someday()`-gaten i `task-list.spec.ts` og
E2E's `show-someday` hænger på det samme.

**Trin 5 — se vagten fælde.** Rul `load()` tilbage til `this.showSomeday()`. Forventet:
`should have parked tasks without the switch being on` fejler som ovenfor.

**Trin 6 — commit.** `♻️ Måske hentes altid, og fluebenet er nu kun et visningsfilter`

---

## Task 6: `ReviewStore` og de fem grupper

**Filer:**
- Opret: `src/Todo.Web/src/app/review/review-store.ts`, `review-groups.ts`
- Test: `src/Todo.Web/src/app/review/review-store.spec.ts`, `review-groups.spec.ts`

`ReviewStore` holder **kun** forfaldstilstanden (`due`, `lastCompletedAt`, `nextDueAt`, `complete()`).
Grupperne er en ren funktion over `TodoTask[]` plus `today`, så de kan testes uden HTTP.

**Trin 1 — de fejlende tests.** De tre skarpe:

```ts
it('should keep the five groups in review order', …)
it('should leave a task that starts in three weeks out of this week', …)
it('should ignore an active search', …)   // store.query.set('x') ændrer intet
```

**Trin 2 — se dem fejle** (modulet findes ikke).

**Trin 3 — implementér.** `reviewGroups(tasks, today)` læser `task.bucket` — serverens
`DeadlineBuckets.For`-svar — frem for at udlede den igen, og `task.status` for de to statusgrupper.
Venter på sorteres på `waitingSince`, ældste først. Udskudt filtreres på
`deferUntil <= today + 7 dage`.

**Trin 4 — se dem bestå.**

**Trin 5 — se to vagter fælde.** (a) Udvid Udskudt til hele bucket'en → `three weeks out` fejler.
(b) Lad grupperingen læse `matching()` → `ignore an active search` fejler.

**Trin 6 — commit.** `✨ Gennemgangens fem grupper er en ren funktion over listen`

---

## Task 7: Skærmen, `appReviewSection` og ruten

**Filer:**
- Opret: `review-section.ts` + `.html`, `review.ts` + `.html`
- Ret: `src/Todo.Web/src/app/app.routes.ts`
- Ret: `src/Todo.Web/public/i18n/da.json`, `en.json`
- Test: `src/Todo.Web/src/app/review/review.spec.ts`

`appReviewSection` er en **kopi af mønstret** i `settings-section.ts`, ikke en genbrug af typen:
overskriften bærer et antal, og `heading` er ikke bare en nøgle. Læs den fil først — især hvorfor
chevronen er et tekstglyf med `aria-hidden` frem for en SVG, og hvorfor vælgeren er
`section[appReviewSection]`.

**Trin 1 — de fejlende tests.**

```ts
it('should open the first non-empty group on arrival', …)
it('should have nothing open when every group is empty', …)
it('should close the open group when its heading is clicked again', …)
```

**Trin 2–4 —** se dem fejle, implementér, se dem bestå. Skærmens host er
`class="block xl:max-w-2xl"` som de fire andre ikke-liste-skærme.

**Trin 5 — se vagten fælde.** Ankom med `null` frem for den første ikke-tomme gruppe. Forventet:
`should open the first non-empty group on arrival` fejler.

**Nøgler i *begge* sprogfiler**, ellers fejler paritetstesten: `review.heading`,
`review.groups.overdue|waiting|startingThisWeek|noDeadline|someday`, `review.empty`,
`review.groupEmpty`, `review.done`, `review.due`, `nav.review`.

**Trin 6 — commit.** `✨ Gennemgangen er en skærm med fem foldede grupper i beslutningsrækkefølge`

---

## Task 8: Navigationslinket, markøren og `Alt+G`

**Filer:**
- Ret: `src/Todo.Web/src/app/app.html`, `app.ts`
- Ret: `tests/Todo.E2E/KeyboardJourneyTests.cs` (`BadgeCount` 9 → 10)
- Ret: `tests/Todo.E2E/ContrastTests.cs` (`25` → 26)
- Test: `tests/Todo.E2E/ReviewJourneyTests.cs`

`Alt+G` er frit: alt-lagets optagne er `O I J A S N K V M`, og Chrome på Windows binder intet
`Alt+G`.

**Trin 1 — de fejlende tests.**

```csharp
/// <summary>
/// A new letter gets its own keystroke test even though the machinery is shared and covered: the
/// one risk the machinery cannot vouch for is whether the browser eats the combination. Alt+D,
/// Alt+E and Alt+F are all taken by Chrome.
/// </summary>
[Fact] public async Task Alt_G_follows_the_review_link()

[Fact] public async Task The_marker_appears_when_a_review_is_owed_and_goes_when_it_is_taken()
```

**Trin 2 — se dem fejle.** Forventet: `element(s) not found` på `nav-review`.

**Trin 3 — implementér.** Linket kopierer `nav-settings`' form. Markøren:

```html
@if (review.due()) {
  <span data-testid="review-due" aria-hidden="true" class="ml-1 text-amber-700 dark:text-amber-300"
    >●</span
  >
  <span class="sr-only">{{ 'review.due' | transloco }}</span>
}
```

Tekstglyf frem for inline-SVG, af samme grund som accordion-chevronen: en SVG uden titel bidrager
intet til det tilgængelige navn, så mutationen der fjerner `aria-hidden` ville fælde **ingenting**.

**Trin 4 — flyt de to tal og kør E2E.** Husk `scripts\build-web.ps1` først. `BadgeCount` 9 → 10
bærer **tre** assertions: badge-optællingen, kollisionsvagten og den såede total, hvor `BadgeCount`
er et led i aritmetikken. `ContrastTests`' `25` → 26 er en bar literal.

**Trin 5 — se to vagter fælde.** (a) Skift bogstavet i skabelonen til `q` →
`Alt_G_follows_the_review_link` fejler med `"none"` som fokuseret element. (b) Læg
`data-testid="review-due"` på et element der altid renderes → påstanden om at markøren forsvinder
fejler. **Mål den efter en rundtur, ikke lige efter trykket** — en Playwright-påstand om at
ingenting ændrede sig kan ikke gøres race-fri ved at polle.

**Trin 6 — commit.** `✨ Navigationslinket bærer en markør når ugens gennemgang mangler`

---

## Task 9: Indstillingsgruppen for ugedag og klokkeslæt

**Filer:**
- Ret: `src/Todo.Web/src/app/settings/settings.html`, `settings.ts` (`SettingsSectionName`),
  `settings-store.ts`
- Ret: begge sprogfiler
- Test: `src/Todo.Web/src/app/settings/settings.spec.ts`

**`<select>`'en skal have `bg-white dark:bg-gray-800`** — sidens baggrund, som sprogvælgeren ved
siden af den. Se planens rettelse øverst; designets afsnit 5 havde det forkerte par.

Klokkeslættet er `<input type="time">`. `'time'` står **allerede** i kontrastvagtens hvidliste over
felttyper der maler deres værdi, så feltets værdi bliver målt uden en ændring.

**Trin 1–4 —** test, se fejle, implementér, se bestå. `saveReview(changes)` svarer ind i gruppens
egen fejllinje, symmetrisk med `saveJira` og `saveAdo`. Husk `SettingsChanges` og `put`'s `next`-
objekt: **hvert** felt skal med, ellers rydder enhver redigering det andet.

**Trin 5 — se vagten fælde.** Udelad `reviewWeekday` fra `next` i `put`. Forventet: en gemt ugedag
går tilbage til fredag, når klokkeslættet gemmes.

**Trin 6 — commit.** `✨ Indstillingssiden har en sjette gruppe til gennemgangens ugedag og tid`

---

## Task 10: Rejsen der beviser genbrugsvalget

**Filer:** `tests/Todo.E2E/ReviewJourneyTests.cs`, `tests/Todo.E2E/ReviewScreen.cs`

```csharp
/// <summary>
/// The payoff of reusing li[appTaskRow]: TaskStore reloads after a save, so the row leaves its
/// group the moment the decision is made. A review with its own copy of the list would go stale
/// instead, and this is the guard on that - see the design's section 3.
/// </summary>
[Fact]
public async Task Deciding_from_the_review_moves_the_row_out_of_its_group()
```

Sæt en Måske-opgave med `TaskItemBuilder`, åbn gennemgangen, åbn Måske-gruppen, sæt status til
Åben, og påstå at rækken er **ude** af Måske-gruppen. `ReviewScreen` følger `SettingsScreen`s form,
og `OpenGroupAsync` er idempotent — et felt i en lukket gruppe findes **ikke** i DOM'en.

**Se den fælde:** giv gennemgangen sin egen kopi af listen. Forventet: rækken bliver stående, og
påstanden fejler med `Locator expected to have count '0' But was: '1'`.

**Commit.** `✅ En beslutning i gennemgangen flytter rækken ud af dens gruppe`

---

## Task 11: `ContrastTests` får sin sjette skærm

**Filer:** `tests/Todo.E2E/ContrastTests.cs`

Den dyreste opgave, og prisen står i designets afsnit 6: en `@if`-gren er **umålt** indtil fixturet
har en opgave i den tilstand **og** rejsen åbner gruppen. Så:

- Én fixture-opgave i hver af de fem grupper — overskredet, ventende, udskudt inden for syv dage,
  uden deadline, Måske.
- Fem klik i `Every_screen_meets_WCAG_AA`, ét snapshot pr. gruppe.
- **Den tomme gennemgang som sin egen skærmtilstand**, snapshottet *først*: den er en tilstand
  rejsen aldrig kan nå tilbage til, når først en gruppe er åbnet — samme regning som
  indstillingssiden, der gik fra én tilstand til fem, da den blev en accordion.

**Se den fælde:** mal en gruppeoverskrift `text-gray-400 dark:text-gray-600`. Forventet: fejl i
**begge** temaer, én pr. gruppenavn, læst `span text "Overskredet" 2,60:1 needs 4,5`. Bemærk at
fejllinjen navngiver elementet der **bærer teksten** og ikke sektionen, så der står ikke et testid
i firkantet parentes — læs hele listen.

**Commit.** `✅ Kontrastvagten går appens sjette skærm igennem i begge temaer`

---

## Task 12: Dokumentation og tallene

**Filer:** `CLAUDE.md`, `docs/HANDOFF.md`,
`docs/plans/2026-08-13-todo-app-design.md`

1. **`CLAUDE.md`s sætning "`app.routes.ts` har præcis fem ruter" er nu forkert** og skal rettes til
   seks, sammen med kontrastvagtens beskrivelse. Lad den ikke stå og lyve — den er auto-indlæst i
   hver session.
2. `Alt+G` ind i genvejsafsnittet, med begrundelsen for at bogstavet er frit.
3. **En ny regel værd at skrive ned:** en `<select>` maler sin egen baggrund i den farve den *står
   på*, ikke i ét fast par — `bg-white dark:bg-gray-800` på siden, `bg-gray-50 dark:bg-gray-800` i
   panelet. Det var planens egen rettelse, og designet havde det forkert.
4. `HANDOFF.md`: gennemgangen ud af "Ønsket, men ikke placeret" og ind under "Uden for skiverne";
   "Næste skridt" tilbage til skive 13; de tre nye målinger fra designets afsnit 8 ind under
   "Målinger kun du kan lave".
5. Designdokumentets afsnit 11 — GTD-hullet om ugentlig gennemgang er lukket. Afsnit 9's "Ønsket,
   men ikke placeret endnu" får gennemgangen som leveret, med begrundelsen for at den ikke fik et
   skivenummer.

**Kør `Check.cmd` og skriv de fire tal ind i begge filer**, med **hvorfor** hvert af dem flyttede
sig. Et ændret tal uden en begrundelse betyder at en test er tabt eller duplikeret — det er hele
reglen, og regnskabet er drevet fra virkeligheden fire gange her.

**Commit.** `📝 Gennemgangen er leveret, og de fire testtal er målt frem for påstået`
