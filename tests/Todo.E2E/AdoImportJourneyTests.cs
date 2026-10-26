using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Settings;
using Todo.TestSupport.Ado;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// The Azure DevOps import screen, driven from the browser. Two of the six journeys here intercept
/// <c>/api/ado/preview</c> — not because a fake is out of reach, but because a route handler is the
/// only way to stage a <em>specific field combination</em>: a body where one row has a deadline and
/// another does not is something the real server can never send, since the day count is one setting
/// for all rows. Those two are about what the screen does with an answer, and
/// <c>AdoTaskSourceTests</c> already owns what an answer looks like.
///
/// Do not read the interception as "Playwright cannot use a fake". It can, and the last three
/// journeys here prove it: <c>FakeAdo</c> is its own Kestrel on 127.0.0.1 and <c>RunningHost</c>
/// starts the app in the test process, so the host's own <c>HttpClient</c> reaches it. That sentence
/// was carried as a justification for three slices before slice 12 measured it. The count is written
/// out rather than said as "most", which it was until the reconciliation added two more real ones and
/// turned the word false without anything being able to see it.
///
/// <c>/api/ado/import</c> is deliberately <em>not</em> intercepted in any of the three journeys that
/// import — the deadline is the server's arithmetic on its own clock, the closure is its re-decision,
/// and the reopening is its mapping. No link leaves the test browser: <c>TodoApp</c> replaces
/// <c>window.open</c> on every page it opens.
/// </summary>
public class AdoImportJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    /// <summary>
    /// A collection URL with a space in it, spelled the way a user would paste it out of the browser.
    /// The real instance's collection is "Edora Software", so the space is not hypothetical — and the
    /// app leaves this half alone while escaping the project, which is where a browse URL is built
    /// from both.
    /// </summary>
    private const string BaseUrl = "https://ado.test/Fake%20Collection";

    private const string Project = "Saas";
    private const string ImportedTitle = "Ret rapporten";
    private const string ImportedKey = "15664";
    private const string WaitingTitle = "Afventer svar";
    private const string SeenBeforeTitle = "Skriv testene";
    private const string WaitingReason = "Du venter på den, og ventende sager er slået fra.";
    private const string SeenBeforeReason = "importeret tidligere";
    private const string ThisWeekSection = "Denne uge";

    /// <summary>
    /// How many of <see cref="FakeAdo"/>'s nine work items pass the default type filter. Bug, User Story
    /// and Task are in; the Test Suite and the three Test Plans are the 17 % of noise decision B exists
    /// to keep out.
    /// </summary>
    private const int FilteredRows = 5;

    private const string StoryTitle = "Som bruger vil jeg kunne filtrere";
    /// <summary>
    /// The fake's own name for it rather than the number again. <c>FakeAdo.MineKey</c> says it is
    /// "named rather than spelled per test", and this file spelled it anyway — two spellings of one
    /// fact that the compiler cannot see disagree, which is the trap the id-type lesson in CLAUDE.md
    /// is about. The alias stays because the journeys read better calling it the story.
    /// </summary>
    private const string StoryKey = FakeAdo.MineKey;
    private const string TestSuiteTitle = "Testsuite for login";
    private const string TestPlanTitle = "Testplan for eksport";

    /// <summary>
    /// Another of the fake's importable work items, and the one the reconciliation journeys hold up
    /// beside <see cref="StoryTitle"/>. It is never handed over and never finished here, so it is the
    /// row that says the offers land where they were earned rather than on everything imported.
    /// </summary>
    private const string BlockedTitle = "Kunden kan ikke logge ind";

    /// <summary>
    /// The state <see cref="BlockedTitle"/> already stands in, and the one the reopening journey moves
    /// the story into. Sharing a state between the two rows is the point: with it in the waiting list
    /// they differ only in whether the local task is finished, so a screen that offered both — or
    /// neither — could not be mistaken for a right one.
    /// </summary>
    private const string BlockedState = "Blocked";

    /// <summary>
    /// The hand-over sentence, spelled out rather than shared with the resolved one. The two are
    /// different strings precisely so a journey cannot pass on the wrong finding, and a shared
    /// constant would give that back.
    /// </summary>
    private const string HandedOverLabel = "Tildelt en anden i Azure DevOps — luk opgaven her.";

    /// <summary>
    /// The reopening's two sentences, named rather than left inline — for symmetry with the one
    /// above, which is the whole of the reason: three sentences of the same kind, one of them a
    /// constant and two of them literals, reads as a forgotten refactor and invites somebody to
    /// "finish" it in whichever direction they guess.
    ///
    /// Two constants and not one, because the offer and what it leads to are separately wrong-able:
    /// <c>reopenTo</c> is a mapping through <c>AdoEndpoints.StatusFor</c>, and the suggestion does not
    /// depend on it.
    /// </summary>
    private const string ReopeningLabel = "Ikke færdig i Azure DevOps — genåbn opgaven her.";

    private const string ReopensAsWaitingLabel = "Genåbnes som ventende.";

    /// <summary>
    /// Three work items. The first can be imported, the second is excluded because the user is waiting
    /// on it, and the third was imported on an earlier run — the last two are what make the two
    /// different reasons measurable, since a fixture with one blocked row could not tell them apart.
    ///
    /// <c>workItemType</c> is on every row and <c>deadline</c> on only two, and both of those are
    /// deliberate. The type is new against Jira and is what the import re-applies the type filter to.
    /// The deadline is the one branch no Jira screen has, and a mixed answer is what puts <em>both</em>
    /// of its halves on one screen — worth knowing that the real server cannot answer this way, because
    /// the day count is one setting for every row: the branch is per row in the template, so measuring
    /// it per row is honest, but do not read this body as a shape the server produces.
    ///
    /// <c>url</c> is spelled out on every row, and it has to be: the field is required on the contract,
    /// so nothing hides the Open-the-item button, but an answer that leaves it out reads as undefined
    /// in the client and the button would open an empty address. Intercepting the call is
    /// not enough — the body has to carry the field.
    /// </summary>
    private const string ThreeWorkItems = """
        {
          "rows": [
            {
              "key": "15664",
              "title": "Ret rapporten",
              "url": "https://ado.test/Fake%20Collection/Saas/_workitems/edit/15664",
              "note": "<div>Tallene i tabellen er fra sidste kvartal.</div>",
              "deadline": "2026-08-20",
              "requester": "Mette Kirkegaard",
              "state": "Active",
              "workItemType": "Bug",
              "isWaiting": false,
              "alreadyImported": false,
              "reconciled": false
            },
            {
              "key": "16901",
              "title": "Afventer svar",
              "url": "https://ado.test/Fake%20Collection/Saas/_workitems/edit/16901",
              "state": "Blocked",
              "workItemType": "User Story",
              "isWaiting": true,
              "waitingSince": "2026-08-05T09:12:00Z",
              "alreadyImported": false,
              "reconciled": false,
              "excluded": "ado.excludedWaiting"
            },
            {
              "key": "17170",
              "title": "Skriv testene",
              "url": "https://ado.test/Fake%20Collection/Saas/_workitems/edit/17170",
              "deadline": "2026-08-20",
              "state": "PO Review",
              "workItemType": "Task",
              "isWaiting": false,
              "alreadyImported": true,
              "reconciled": false
            }
          ],
          "total": 3
        }
        """;

    /// <summary>
    /// One importable work item, for the journey that only opens a row rather than importing it. The
    /// deadline is 30 September and deliberately not what the server would derive, as a reminder to
    /// anyone reusing this body: the value is the client's to <em>show</em> and never to send back.
    /// The journey that imports uses a real <see cref="FakeAdo"/> instead, where the date can only come
    /// from the server's own clock.
    /// </summary>
    private const string OneImportableWorkItem = """
        {
          "rows": [
            {
              "key": "15664",
              "title": "Ret rapporten",
              "url": "https://ado.test/Fake%20Collection/Saas/_workitems/edit/15664",
              "note": "<div>Tallene i tabellen er fra sidste kvartal.</div>",
              "deadline": "2026-09-30",
              "requester": "Mette Kirkegaard",
              "state": "Active",
              "workItemType": "Bug",
              "isWaiting": false,
              "alreadyImported": false,
              "reconciled": false
            }
          ],
          "total": 1
        }
        """;

    /// <summary>2026-08-17 is a Monday, so today plus three days is still inside "Denne uge".</summary>
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    /// <summary>
    /// Without a collection, a project and a token there is nothing to fetch, and the screen says so in
    /// words with a way to the page that fixes it. The absent Load button is asserted too: the sentence
    /// alone would also be there on a screen that had both, and then the guard would be about nothing.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_screen_says_so_and_links_to_the_settings()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var ado = await App.GoToAdo();

        await Assertions.Expect(ado.NotConfigured)
            .ToHaveTextAsync("Azure DevOps er ikke sat op, så der er ingen sager at hente.");
        await Assertions.Expect(ado.PreviewButton).ToHaveCountAsync(0);

        // And the notice about the invented deadline is part of the configured half, not this one:
        // there is nothing to propose a deadline for yet.
        await Assertions.Expect(ado.DeadlineNotice).ToHaveCountAsync(0);

        await ado.SettingsLink.ClickAsync();

        await Assertions.Expect(new SettingsScreen(App).SectionToggle(SettingsScreen.LanguageSection))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// A blocked row stays on screen with its reason rather than disappearing: hidden, the work item
    /// would look like something Azure DevOps had lost, and the "import waiting items too" setting
    /// would be invisible. The two reasons are asserted as different strings, because one shared
    /// sentence for both would leave the user unable to tell a setting from a duplicate.
    ///
    /// This is also where the fields Jira has no counterpart for are measured: the work item type on
    /// every row, and both halves of the deadline branch.
    /// </summary>
    [Fact]
    public async Task A_preview_shows_the_blocked_rows_switched_off_with_their_own_reasons()
    {
        await ConfigureAdoAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1400 });
        await AnswerPreviewWithAsync(ThreeWorkItems);

        var ado = await App.GoToAdo();

        // The notice that the deadline on every row is the app's own suggestion. It has no Jira
        // counterpart, because Jira's due date came off the issue.
        await Assertions.Expect(ado.DeadlineNotice).ToContainTextAsync("foreslår");

        await ado.PreviewAsync();

        await Assertions.Expect(ado.Rows).ToHaveCountAsync(3);
        await Assertions.Expect(ado.Showing).ToHaveTextAsync("Viser 3 af 3 sager.");

        var importable = ado.Row(ImportedTitle);
        var waiting = ado.Row(WaitingTitle);
        var seenBefore = ado.Row(SeenBeforeTitle);

        // The one that can be imported: ticked by default, so the Import button means something
        // without a click. Asserted first, because a screen where nothing at all was tickable would
        // satisfy every disabled-checkbox claim below.
        await Assertions.Expect(AdoImportScreen.PickOf(importable)).ToBeEnabledAsync();
        await Assertions.Expect(AdoImportScreen.PickOf(importable)).ToBeCheckedAsync();

        // The type, which is the field that made this slice's contract differ from Jira's. Asserted on
        // two rows with different types, because one row would pass on a template that printed a
        // constant.
        await Assertions.Expect(AdoImportScreen.TypeIn(importable)).ToHaveTextAsync("Type: Bug");
        await Assertions.Expect(AdoImportScreen.TypeIn(waiting)).ToHaveTextAsync("Type: User Story");

        // Both halves of the deadline branch, on the two rows that carry each. Matched on the day and
        // the year rather than on the whole localized string, the same way formatDeadline's own spec
        // does — the failure this catches is a date that never interpolated, not a month name.
        await Assertions.Expect(AdoImportScreen.DeadlineIn(importable))
            .ToContainTextAsync(new Regex(@"\b20\b"));
        await Assertions.Expect(AdoImportScreen.DeadlineIn(importable)).ToContainTextAsync("2026");
        await Assertions.Expect(AdoImportScreen.NoDeadlineIn(importable)).ToHaveCountAsync(0);

        await Assertions.Expect(AdoImportScreen.NoDeadlineIn(waiting))
            .ToContainTextAsync("Uden deadline");
        await Assertions.Expect(AdoImportScreen.DeadlineIn(waiting)).ToHaveCountAsync(0);

        // The requester and the note, both of which only the first row carries. The note says *that*
        // a description came along rather than showing it, because Azure DevOps hands over raw HTML.
        await Assertions.Expect(AdoImportScreen.RequesterIn(importable))
            .ToHaveTextAsync("Opgavestiller: Mette Kirkegaard");
        await Assertions.Expect(AdoImportScreen.NoteIn(importable))
            .ToHaveTextAsync("Beskrivelsen følger med.");
        await Assertions.Expect(AdoImportScreen.RequesterIn(waiting)).ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.NoteIn(waiting)).ToHaveCountAsync(0);

        // The waiting pair, and the date inside it. Asserted on the year rather than on the formatted
        // string: the failure Task 5 measured was a line reading "Venter siden " with no date at all,
        // because a timestamp through formatDeadline's date-only regex answers with an empty string.
        await Assertions.Expect(AdoImportScreen.WaitingIn(waiting))
            .ToHaveTextAsync("Importeres som ventende.");
        await Assertions.Expect(AdoImportScreen.WaitingSinceIn(waiting)).ToContainTextAsync("2026");
        await Assertions.Expect(AdoImportScreen.WaitingIn(importable)).ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.WaitingSinceIn(importable)).ToHaveCountAsync(0);

        await Assertions.Expect(AdoImportScreen.ExcludedIn(waiting)).ToHaveTextAsync(WaitingReason);
        await Assertions.Expect(AdoImportScreen.PickOf(waiting)).ToBeDisabledAsync();
        await Assertions.Expect(AdoImportScreen.PickOf(waiting)).Not.ToBeCheckedAsync();

        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(seenBefore))
            .ToHaveTextAsync(SeenBeforeReason);
        await Assertions.Expect(AdoImportScreen.PickOf(seenBefore)).ToBeDisabledAsync();
        await Assertions.Expect(AdoImportScreen.PickOf(seenBefore)).Not.ToBeCheckedAsync();

        // Neither reason may stand in for the other, and the strings above only prove that while they
        // differ. Said out loud so a later edit that made them one sentence fails here.
        Assert.NotEqual(WaitingReason, SeenBeforeReason);

        // Nothing-to-select is the sentence for a preview where every row is blocked, and this one has
        // a tickable row. Absent rather than untested: it would otherwise be pure decoration.
        await Assertions.Expect(ado.NothingToSelect).ToHaveCountAsync(0);
        await Assertions.Expect(ado.NoneAssigned).ToHaveCountAsync(0);

        // A row here carries two lines more than a Jira row — the type and the deadline — and that is
        // exactly the kind of thing that pushes a 480 px column sideways. Compared with clientWidth
        // rather than with 480: a vertical scrollbar makes the layout 465 wide, and a fixed number
        // would fail for that reason instead of this one.
        var pageWidth = await App.ClientWidthAsync();
        var scrolledWidth = await App.ScrollWidthAsync();

        Assert.True(scrolledWidth <= pageWidth,
            $"The preview rows push the page sideways: scrollWidth was {scrolledWidth} against a "
            + $"clientWidth of {pageWidth}.");
    }

    /// <summary>
    /// The preview's own button: a work item can be read <em>before</em> anyone decides to import it.
    /// The URL comes from the answer's <c>url</c> field and is asserted as what reached
    /// <c>window.open</c> rather than through an attribute — a button has no href to read.
    ///
    /// It is a &lt;button&gt; rather than an &lt;a href&gt; because the app window has neither an
    /// address bar nor a back button, and the tag name is the only thing stopping that simplification.
    /// </summary>
    [Fact]
    public async Task Opening_a_previewed_work_item_opens_it_in_the_browser_without_importing_it()
    {
        await ConfigureAdoAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await AnswerPreviewWithAsync(OneImportableWorkItem);

        var ado = await App.GoToAdo();
        await ado.PreviewAsync();

        var row = ado.Row(ImportedTitle);
        var openItem = AdoImportScreen.OpenItemIn(row);

        await Assertions.Expect(openItem).ToHaveTextAsync("Åbn sagen");
        Assert.Equal("BUTTON", await openItem.EvaluateAsync<string>("el => el.tagName"));

        await App.Page.EvaluateAsync("window.stampedBeforeTheClick = true");

        await openItem.ClickAsync();

        Assert.Equal(
            $"{BaseUrl}/{Project}/_workitems/edit/{ImportedKey}",
            await App.OpenedLinkAsync());

        Assert.True(
            await App.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The click took the window with it, and this window has no way back.");

        // The button sits outside the row's <label> on purpose, and this is the assertion that measures
        // it: a label's text becomes the accessible name of the control inside it, so a button moved in
        // there would leave the checkbox announced as "… Åbn sagen".
        //
        // Said this way rather than as "the tick survived the click", which cannot fail: a <button>
        // inside a <label> is interactive content, so the browser skips the label's activation
        // behaviour and the checkbox does not toggle.
        await Assertions
            .Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = "Åbn sagen" }))
            .ToHaveCountAsync(0);

        // And the tick is still there, which is what makes the claim above about this row: a count of
        // zero would also hold on a screen with no checkbox at all.
        await Assertions.Expect(AdoImportScreen.PickOf(row)).ToBeCheckedAsync();
    }

    /// <summary>
    /// The whole journey, against a real <see cref="FakeAdo"/> rather than an intercepted preview — and
    /// that is worth stating, because both the plan and <c>CLAUDE.md</c> say Playwright cannot use one.
    /// It can: <c>FakeAdo</c> is its own Kestrel on 127.0.0.1, and <c>RunningHost</c> starts the app
    /// <em>in this process</em>, so the host's own <c>HttpClient</c> reaches it. Stored as the
    /// collection URL, nothing needs stubbing at all: the WIQL, the type filter, the note mapping, the
    /// dedup and the derived deadline are all the shipping code path.
    ///
    /// Three things only this shape can measure. The <b>type filter</b> end to end: the fake serves
    /// three Test Plans and a Test Suite, and none of them may reach the task list. The
    /// <b>deadline</b>: Azure DevOps has no due date field, so the date is the server's arithmetic on
    /// its own clock — three days from the fixed Monday, which lands inside "Denne uge" and could not
    /// come from anywhere else. And <b>"imported earlier"</b>: the second preview really asks the
    /// database, so the line is the app's own dedup rather than a field a fixture put there.
    ///
    /// Only <c>/api/ado/import</c> is intercepted, and only to read the body before letting it through.
    /// </summary>
    [Fact]
    public async Task Importing_derives_the_deadline_on_the_server_and_the_next_preview_says_so()
    {
        await using var fake = await FakeAdo.StartAsync();

        await ConfigureAdoAsync(fake);
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1800 });

        var posted = new TaskCompletionSource<JsonElement?>();

        await App.Page.RouteAsync("**/api/ado/import", async route =>
        {
            posted.TrySetResult(route.Request.PostDataJSON());
            await route.ContinueAsync();
        });

        var ado = await App.GoToAdo();
        await ado.PreviewAsync();

        // Five of the fake's nine work items pass the default type filter: a Test Suite and three Test
        // Plans are kept out. Asserted as a count first, because every claim below would also hold on a
        // screen showing more rows than it should.
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);
        await Assertions.Expect(ado.Showing).ToHaveTextAsync($"Viser {FilteredRows} af {FilteredRows} sager.");
        await Assertions.Expect(ado.Row(TestSuiteTitle)).ToHaveCountAsync(0);
        await Assertions.Expect(ado.Row(TestPlanTitle)).ToHaveCountAsync(0);

        // The type on a row is the fake's own answer here rather than a staged string, so this is the
        // one place the field is measured through the real mapping.
        await Assertions.Expect(AdoImportScreen.TypeIn(ado.Row(StoryTitle)))
            .ToHaveTextAsync("Type: User Story");

        // The count on the button is the selection made visible.
        await Assertions.Expect(ado.ImportButton).ToHaveTextAsync($"Importér {FilteredRows} sager");

        await ado.ImportAsync();

        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync($"{FilteredRows} importeret, 0 sprunget over");

        // What went on the wire, read rather than inferred. A row carries the state and the type because
        // both are facts the server re-decides from; it carries no deadline at all, which is the
        // contract's way of saying the date is not the client's to send.
        var body = await posted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var rows = body?.GetProperty("rows").EnumerateArray().ToList() ?? [];

        Assert.Equal(FilteredRows, rows.Count);

        var story = rows.Single(row => row.GetProperty("title").GetString() == StoryTitle);

        Assert.Equal(StoryKey, story.GetProperty("key").GetString());
        Assert.Equal("User Story", story.GetProperty("workItemType").GetString());
        Assert.Equal("Active", story.GetProperty("state").GetString());
        Assert.False(story.TryGetProperty("deadline", out _),
            "AdoImportRow has no deadline field: the date is derived from the server's clock, so a "
            + "client that sent one would decide when a task is due.");

        // Importing re-previews on its own, and that is what marks the rows as seen before. The answer
        // is the fake's, unchanged — only the database moved, so this line is the dedup speaking.
        var previewed = ado.Row(StoryTitle);

        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(previewed))
            .ToHaveTextAsync(SeenBeforeReason);
        await Assertions.Expect(AdoImportScreen.PickOf(previewed)).ToBeDisabledAsync();

        // Every row on screen is blocked now, which is a sentence of its own rather than an empty list.
        await Assertions.Expect(ado.NothingToSelect)
            .ToContainTextAsync($"{FilteredRows} sager er importeret tidligere.");
        await Assertions.Expect(ado.ImportButton).ToBeDisabledAsync();

        var tasks = await ado.GoToTasks();

        // The leg the journey is for: three days from the fixed clock's Monday. Said from the other side
        // as well, because "it is in Denne uge" would also hold on a screen that showed it twice — and
        // there is nothing else in this database to fill another section.
        await Assertions.Expect(tasks.RowsIn(ThisWeekSection)).ToHaveCountAsync(FilteredRows);
        await Assertions
            .Expect(tasks.RowsIn(ThisWeekSection).Filter(new() { HasText = StoryTitle }))
            .ToHaveCountAsync(1);
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(FilteredRows);

        // The type filter, from the far end. A Test Plan that slipped through the query would be a task
        // here, and nothing above would have said so.
        await Assertions.Expect(tasks.RowFor(TestSuiteTitle)).ToHaveCountAsync(0);

        // And the link on an imported task, which is the Azure DevOps branch of the task endpoint's
        // externalUrl. It needs two stored settings rather than Jira's one — the collection URL and the
        // project — because that is what a browse URL is built from; without either the branch stays
        // absent however the source is spelled. The two halves are escaped differently: the collection
        // arrives already carrying %20, the project is a name the app escapes itself.
        var link = tasks.ExternalLinkIn(StoryTitle);

        await Assertions.Expect(link).ToHaveTextAsync("Åbn sagen");
        Assert.Equal("BUTTON", await link.EvaluateAsync<string>("el => el.tagName"));

        await link.ClickAsync();

        Assert.Equal(
            $"{fake.BaseUrl}/{Uri.EscapeDataString(FakeAdo.Project)}/_workitems/edit/{StoryKey}",
            await App.OpenedLinkAsync());
    }

    /// <summary>
    /// The hand-over, end to end against a real <see cref="FakeAdo"/> with nothing intercepted — and it
    /// is the only shape that can measure the thing this feature rests on: <b>reconciliation asks a
    /// different question from the fetch</b>. The fetch is
    /// <c>[System.AssignedTo] = @Me</c> over a type list and can never name a key, so a work item that
    /// has become somebody else's simply falls out of it; only a batch read by id still hands it over.
    /// A route handler could put the sentence on screen, but it would be staging our own answer to the
    /// question rather than asking it.
    ///
    /// <see cref="FakeAdo.HandOver"/> writes no field, because assignment is not one here: it changes
    /// what <c>@Me</c> answers and nothing else, the same place the real server keeps it.
    ///
    /// The reading of "showing N of N" is the teeth. Five rows stay on screen while the fetch's own
    /// total drops to four, which is the screen saying out loud that the fifth came from somewhere
    /// else — a count of rows alone would be five both before and after.
    /// </summary>
    [Fact]
    public async Task A_work_item_handed_to_somebody_else_offers_to_close_the_task_it_was_imported_as()
    {
        await using var fake = await FakeAdo.StartAsync();

        await ConfigureAdoAsync(fake);
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1800 });

        var ado = await App.GoToAdo();

        await ado.PreviewAsync();
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync($"{FilteredRows} importeret, 0 sprunget over");

        // The task the rest of this journey is about really exists, which is what makes the closure
        // below a closure rather than a label.
        var tasks = await App.GoToTasks();

        await Assertions.Expect(tasks.RowFor(StoryTitle)).ToHaveCountAsync(1);

        fake.HandOver(StoryKey);

        ado = await App.GoToAdo();
        await ado.PreviewAsync();

        // Still five rows, and the fetch answered four of them. Both halves are needed: the count
        // alone held before the hand-over too, and the total alone would hold on a screen that had
        // lost the row entirely.
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);
        await Assertions.Expect(ado.Showing)
            .ToHaveTextAsync($"Viser {FilteredRows - 1} af {FilteredRows - 1} sager.");

        // Read rather than inferred: the reconciliation named the key in a WIQL, which the fetch's
        // query has no clause for at all.
        Assert.Contains(
            fake.WiqlRequests,
            query => query.Contains($"[System.Id] IN ({StoryKey})", StringComparison.Ordinal));

        var handedOver = ado.Row(StoryTitle);

        // The two closing sentences are different strings on purpose, so this cannot pass on the
        // resolved one — which is the label the older journey in ImportClosureJourneyTests measures.
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(handedOver))
            .ToHaveTextAsync(HandedOverLabel);
        await Assertions.Expect(AdoImportScreen.PickOf(handedOver)).ToBeEnabledAsync();
        await Assertions.Expect(AdoImportScreen.PickOf(handedOver)).ToBeCheckedAsync();

        // The negative half, and it measures the template alone. Without it a screen that put the
        // offer on every imported row would pass.
        //
        // It cannot also stand for "a reconciliation that called every key a hand-over", which this
        // comment claimed until the review measured it: replacing the whole reason expression in
        // AdoEndpoints with role == Done ? Resolved : Unassigned leaves both journeys green. The
        // reason is structural rather than a gap in the fixture. ReconciledRowsAsync is only handed
        // the imported keys the fetch did not answer with, and 15664 is a fetched row — the line
        // above asserts as much — so the hand-over branch can never reach it. Measuring that branch
        // needs a second reconciled key, which would lengthen the journey for a rule
        // AdoEndpointsTests already guards.
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(ado.Row(BlockedTitle)))
            .ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(ado.Row(BlockedTitle)))
            .ToHaveTextAsync(SeenBeforeReason);

        await Assertions.Expect(ado.ImportButton).ToHaveTextAsync("Luk 1 opgave");

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync("0 importeret, 0 sprunget over. 1 opgave lukket.");

        // The row is gone rather than merely quiet, and that is the server's rule rather than the
        // screen's: a task that is done here is never reconciled, so there is no key left to ask
        // about. Importing re-previews on its own, so this is the answer to a fresh question.
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows - 1);
        await Assertions.Expect(ado.Row(StoryTitle)).ToHaveCountAsync(0);

        // And the only assertion the user would recognise.
        tasks = await App.GoToTasks();
        await tasks.ShowCompleted.CheckAsync();

        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(1);
        await Assertions
            .Expect(tasks.CompletedRows.Filter(new() { HasText = StoryTitle }))
            .ToHaveCountAsync(1);
    }

    /// <summary>
    /// The other half of the feature: a task finished <em>here</em> while Azure DevOps is not done
    /// with it is offered back, and taking the offer clears the completion time.
    ///
    /// What is new is the <b>chain</b> — screen to wire to server — and not either end of it. Both
    /// ends were already guarded, and saying otherwise would credit this journey with somebody else's
    /// work: <c>AdoEndpointsTests.Reopening_puts_the_status_back_and_clears_the_completion_time</c>
    /// asserts the very <c>Assert.Null(task.CompletedAt)</c> this ends on, and four Vitest cover the
    /// store and the template. What no test had was the two of them joined by a real request.
    ///
    /// The task is finished through the app's own toggle rather than by accepting a source-driven
    /// closure, and that is a choice worth knowing. Accepting a closure first would measure more —
    /// it would run the closing leg again — but ImportClosureJourneyTests already owns that leg
    /// against this same fake, so the extra would mostly be a second copy of it, and it would make
    /// this journey three imports long. What the shorter form buys instead is that
    /// <c>CompletedAt</c> is written by <c>PUT /api/tasks/{id}</c>: the reopening is measured
    /// clearing a field somebody else set, rather than undoing its own write.
    ///
    /// The state change is load-bearing twice over, which is why it is Blocked rather than any other
    /// open state. It decides what the suggestion says it will do — <c>reopenTo</c> is the state's
    /// role through <c>AdoEndpoints.StatusFor</c>, so a wrong mapping says "åben" here — and it
    /// decides which status the import actually writes. Note the pair it puts on one screen: 15664 is
    /// in the very same state and is skipped for it, because waiting items are switched off, while
    /// this row is offered anyway. That is <c>IncludeWaiting</c> deliberately not being asked of a
    /// task the user already owns, measured rather than asserted in a comment.
    ///
    /// <c>completedAt</c> is on the task contract and rendered nowhere, so the last claim is read off
    /// the response the screen itself received. Read before as well as after: "still null" would pass
    /// on a reopening that never touched the field.
    /// </summary>
    [Fact]
    public async Task A_task_finished_here_is_reopened_when_Azure_DevOps_is_not_done_with_it()
    {
        await using var fake = await FakeAdo.StartAsync();

        await ConfigureAdoAsync(fake);
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1800 });

        var ado = await App.GoToAdo();

        await ado.PreviewAsync();
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync($"{FilteredRows} importeret, 0 sprunget over");

        var tasks = await App.GoToTasks();

        await tasks.RowFor(StoryTitle).GetByTestId("complete-toggle").ClickAsync();
        await Assertions.Expect(tasks.RowFor(StoryTitle)).ToHaveCountAsync(0);

        var finished = await TaskOnTheWireAsync(StoryTitle, () => tasks.ShowCompleted.CheckAsync());

        Assert.Equal("done", finished.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, finished.GetProperty("completedAt").ValueKind);

        // Azure DevOps moves the work item on, and the user's waiting list is what gives the state
        // its meaning here. Two changes rather than one, because a state alone cannot be waiting.
        fake.MoveTo(StoryKey, BlockedState);
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoWaitingStates, Value = $"[\"{BlockedState}\"]" });

        ado = await App.GoToAdo();
        await ado.PreviewAsync();

        var reopening = ado.Row(StoryTitle);

        await Assertions.Expect(AdoImportScreen.SuggestsReopeningIn(reopening))
            .ToHaveTextAsync(ReopeningLabel);
        await Assertions.Expect(AdoImportScreen.ReopenToIn(reopening))
            .ToHaveTextAsync(ReopensAsWaitingLabel);
        await Assertions.Expect(AdoImportScreen.PickOf(reopening)).ToBeEnabledAsync();
        await Assertions.Expect(AdoImportScreen.PickOf(reopening)).ToBeCheckedAsync();

        // Same state, opposite outcome, on one screen.
        var skipped = ado.Row(BlockedTitle);

        await Assertions.Expect(AdoImportScreen.SuggestsReopeningIn(skipped)).ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.ExcludedIn(skipped)).ToHaveTextAsync(WaitingReason);

        await Assertions.Expect(ado.ImportButton).ToHaveTextAsync("Genåbn 1 opgave");

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync("0 importeret, 0 sprunget over. 1 opgave genåbnet.");

        // The offer is gone on the fresh preview the import ran, and the row now reads like its
        // neighbour: waiting, imported before, switched off. That is the app reading the local status
        // back rather than remembering what it just did.
        //
        // The positive claim goes first, and the order is the barrier rather than a preference. The
        // receipt above is not one: importSelected sets it <em>before</em> awaiting reload(), so a
        // count of zero read straight after it could be the old rows still standing. That it cannot
        // be today rests on AdoStore.preview() not clearing rows until the answer is in, which is a
        // detail of the store rather than of this journey. WaitingReason can only be true of the
        // fresh answer, so asserting it first makes the absence below honest whatever the store does
        // in between.
        await Assertions.Expect(AdoImportScreen.ExcludedIn(ado.Row(StoryTitle)))
            .ToHaveTextAsync(WaitingReason);
        await Assertions.Expect(AdoImportScreen.SuggestsReopeningIn(ado.Row(StoryTitle)))
            .ToHaveCountAsync(0);

        var reopened = await TaskOnTheWireAsync(StoryTitle, async () => tasks = await App.GoToTasks());

        // The status the state mapped to, not merely "not done": a reopening that always wrote Open
        // would pass on the weaker claim while the screen had promised "ventende".
        Assert.Equal("waitingFor", reopened.GetProperty("status").GetString());

        // The teeth. A journey that stopped at the status would pass with CompletedAt left standing,
        // and that field is read by both the completed filter and the weekly review.
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("completedAt").ValueKind);

        // Said from the screen as well, because the wire alone would hold on a list that never
        // rendered it. Show-completed is still on from earlier, so an empty completed section is a
        // claim rather than a filter.
        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(0);
        await Assertions
            .Expect(tasks.WaitingRows.Filter(new() { HasText = StoryTitle }))
            .ToHaveCountAsync(1);
    }

    /// <summary>
    /// One task as the screen's own request answered it. <c>completedAt</c> is on the contract and on
    /// no screen, so reading the browser's copy of the list response is the only way a journey can see
    /// it — and it is a stronger claim than asking the host directly, because it is the body the
    /// screen was actually handed.
    ///
    /// The waiter is registered before the action runs rather than after, which is what
    /// <c>RunAndWaitForResponseAsync</c> is for: polling afterwards would race the render, and the
    /// first poll that succeeds ends the wait.
    /// </summary>
    private async Task<JsonElement> TaskOnTheWireAsync(string title, Func<Task> action)
    {
        // The action comes first in Playwright's own signature, and the predicate second — which
        // reads backwards next to every other wait in this suite.
        //
        // A readability trap and emphatically not a silent one, which is what this note said until
        // the review measured it: the reversed form does not compile against the string overload, it
        // does not compile at all. Swapping the two arguments and building gives
        // CS1593 "Delegate 'Func<Task>' does not take 1 arguments" and CS1503 "Argument 2: cannot
        // convert from 'System.Func<Task>' to 'string'". So nobody can ship the mistake; the cost is
        // one confused reading, not a predicate that quietly matches nothing.
        var response = await App.Page.RunAndWaitForResponseAsync(
            action,
            answer => answer.Request.Method == "GET"
                && answer.Url.Contains("/api/tasks?", StringComparison.Ordinal));

        var body = await response.JsonAsync()
            ?? throw new InvalidOperationException("The task list answered with no JSON body.");

        var items = body.GetProperty("items").EnumerateArray().ToList();

        return items.SingleOrDefault(
                item => item.GetProperty("title").GetString() == title,
                default)
            is { ValueKind: not JsonValueKind.Undefined } found
            ? found
            : throw new InvalidOperationException(
                $"No task titled \"{title}\" in a list of {items.Count}, so the field this asserts "
                + "on would have been read off the wrong row.");
    }

    /// <summary>
    /// Enough settings for the screen to offer the Load button and for the task endpoint to compute a
    /// browse URL. Written straight to the database rather than typed in: what a token does to the
    /// settings page is <see cref="SettingsJourneyTests"/>' business, not this suite's.
    /// </summary>
    private Task ConfigureAdoAsync() => Host.AddAndSaveChangesAsync(
        new Setting { Key = SettingKeys.AdoBaseUrl, Value = BaseUrl },
        new Setting { Key = SettingKeys.AdoProject, Value = Project },
        new Setting { Key = SettingKeys.AdoToken, Value = "not-a-real-token" });

    /// <summary>
    /// The same three settings, but pointed at a fake on loopback so the whole chain is real. The
    /// collection URL is the fake's own, which already carries <c>%20</c> for the space in its name.
    /// </summary>
    private Task ConfigureAdoAsync(FakeAdo fake) => Host.AddAndSaveChangesAsync(
        new Setting { Key = SettingKeys.AdoBaseUrl, Value = fake.BaseUrl },
        new Setting { Key = SettingKeys.AdoProject, Value = FakeAdo.Project },
        new Setting { Key = SettingKeys.AdoToken, Value = FakeAdo.Token });

    private Task AnswerPreviewWithAsync(string json)
        => App.Page.RouteAsync("**/api/ado/preview", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = json,
        }));
}
