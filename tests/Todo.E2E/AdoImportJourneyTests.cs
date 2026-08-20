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
/// The Azure DevOps import screen, driven from the browser. Playwright cannot start a <c>FakeAdo</c>
/// inside the host's process, so the app's own calls to <c>/api/ado/preview</c> are intercepted:
/// this suite is about what the screen does with an answer, and <c>AdoTaskSourceTests</c> already owns
/// what an answer looks like. <c>/api/ado/import</c> is deliberately <em>not</em> intercepted in the
/// journey that imports — the deadline is the server's arithmetic on its own clock, and that
/// derivation is the subject. <c>/api/system/open-link</c> is aborted rather than answered: letting it
/// through would ask the operating system to open a real browser window on the machine running the
/// tests, and the abort is per test rather than per file.
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
    private const string StoryKey = "16901";
    private const string TestSuiteTitle = "Testsuite for login";
    private const string TestPlanTitle = "Testplan for eksport";

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
    /// in the client and the button would ask the shell for an empty address. Intercepting the call is
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
              "alreadyImported": false
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
              "alreadyImported": true
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
              "alreadyImported": false
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

        await Assertions.Expect(new SettingsScreen(App).Language).ToBeVisibleAsync();
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
    /// The URL comes from the answer's <c>url</c> field and is asserted through the request body rather
    /// than through an attribute — a button has no href to read, and the body is what actually reaches
    /// the shell.
    ///
    /// It is a &lt;button&gt; rather than an &lt;a href&gt; because the Photino window has neither an
    /// address bar nor a back button, and the tag name is the only thing stopping that simplification.
    /// </summary>
    [Fact]
    public async Task Opening_a_previewed_work_item_asks_the_shell_for_it_without_importing_it()
    {
        await ConfigureAdoAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await AnswerPreviewWithAsync(OneImportableWorkItem);

        var opened = new TaskCompletionSource<string?>();

        // A body without the field is answered with a sentence rather than left to throw inside the
        // handler: GetProperty on an absent "url" takes the completion source down with it, and the
        // test then fails as a bare timeout that says nothing about which field went missing. The
        // sentinel is a failing value, never a passing one.
        await App.Page.RouteAsync("**/api/system/open-link", async route =>
        {
            var body = route.Request.PostDataJSON();

            opened.TrySetResult(
                body is { } json && json.TryGetProperty("url", out var url)
                    ? url.GetString() ?? "<url was null>"
                    : "<the body carried no url>");

            await route.AbortAsync();
        });

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
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(
            await App.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The click took the window with it, and this window has no way back.");

        // And the failure is said out loud, beside the row that was pressed. The abort above is what
        // makes this free: an aborted request is a failure to the client, so the error path is reached
        // without any staging of its own — and an aborted call carries no error code, so the sentence
        // is apiErrorMessage's generic fallback rather than a code's own text.
        await Assertions.Expect(AdoImportScreen.OpenErrorIn(row))
            .ToHaveTextAsync("Noget gik galt. Prøv igen.");

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

        // Nothing here clicks an item's link, but a stray click must not reach the machine — and the
        // abort is a property of this test rather than of the file.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

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

        var opened = new TaskCompletionSource<string?>();

        await App.Page.UnrouteAsync("**/api/system/open-link");
        await App.Page.RouteAsync("**/api/system/open-link", async route =>
        {
            var asked = route.Request.PostDataJSON();

            opened.TrySetResult(
                asked is { } json && json.TryGetProperty("url", out var url)
                    ? url.GetString() ?? "<url was null>"
                    : "<the body carried no url>");

            await route.AbortAsync();
        });

        await link.ClickAsync();

        Assert.Equal(
            $"{fake.BaseUrl}/{Uri.EscapeDataString(FakeAdo.Project)}/_workitems/edit/{StoryKey}",
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));
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
