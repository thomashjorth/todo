using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Settings;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// The Jira import screen, driven from the browser. Playwright cannot start a <c>FakeJira</c> inside
/// the host's process, so the app's own calls are intercepted instead: this suite is about what the
/// screen does with an answer, and <c>JiraTaskSourceTests</c> already owns what the answer looks
/// like. <c>/api/system/open-link</c> is aborted rather than answered — letting it through would ask
/// the operating system to open a real browser window on the machine running the tests.
/// </summary>
public class JiraImportJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string BaseUrl = "https://jira.test";
    private const string ProjectKey = "SAAS";
    private const string ImportedTitle = "Ret rapporten";
    private const string WaitingTitle = "Afventer svar";
    private const string SeenBeforeTitle = "Skriv testene";
    private const string WaitingReason = "Du venter på den, og ventende sager er slået fra.";
    private const string SeenBeforeReason = "importeret tidligere";

    private const string DutyTitle = "Nedbrud på betalingerne";

    /// <summary>
    /// The status the duty pool parks its issues in. It goes in <em>both</em> saved lists below, and
    /// that overlap is the rule this suite is here for: the same status means "waiting for the pool"
    /// with the switch off and "waiting for you" with it on. A status only in the duty list would
    /// import as Open whatever the switch said, and the last assertion could not fail.
    /// </summary>
    private const string DutyStatus = "Afventer general";

    private const string DutyLabel = "fra 2nd. level supporten";
    private const string OnDutyNotice = "Du har vagten — 2nd. level-sagerne er med.";
    private const string TodaySection = "I dag";

    /// <summary>
    /// Three issues: one that can be imported, one excluded because the user is waiting on it, and
    /// one imported on an earlier run. The last two are what make the two different reasons
    /// measurable — a fixture with only one blocked row could not tell them apart.
    ///
    /// <c>url</c> is spelled out on every row, and it has to be: the field is required on the
    /// contract, so nothing hides the Open-the-issue button, but an answer that leaves the field out
    /// reads as undefined in the client and the button would ask the shell for an empty address.
    /// Intercepting the call is not enough — the body has to carry the field. The addresses match
    /// <see cref="BaseUrl"/>, which is what a real server would compute them from.
    /// </summary>
    private const string ThreeIssues = """
        {
          "rows": [
            {
              "key": "SAAS-1",
              "title": "Ret rapporten",
              "url": "https://jira.test/browse/SAAS-1",
              "note": "Tallene i tabellen er fra sidste kvartal.",
              "deadline": "2026-08-24",
              "requester": "Mette Kirkegaard",
              "status": "I gang",
              "isWaiting": false,
              "alreadyImported": false
            },
            {
              "key": "SAAS-2",
              "title": "Afventer svar",
              "url": "https://jira.test/browse/SAAS-2",
              "status": "Venter på kunde",
              "isWaiting": true,
              "waitingSince": "2026-08-05T09:12:00Z",
              "alreadyImported": false,
              "excluded": "jira.excludedWaiting"
            },
            {
              "key": "SAAS-3",
              "title": "Skriv testene",
              "url": "https://jira.test/browse/SAAS-3",
              "status": "I gang",
              "isWaiting": false,
              "alreadyImported": true
            }
          ],
          "total": 3
        }
        """;

    /// <summary>
    /// One issue from the duty pool, with a deadline of the fixed clock's today so the task list has
    /// a named section to look for it in. <c>isDuty</c> is spelled out because the label hangs on
    /// that field alone — an answer without it renders nothing, and <c>isWaiting</c> is false because
    /// a duty row never waits. The import re-derives both from the status anyway; what is on the
    /// wire here is only what the screen was given to draw.
    /// </summary>
    private const string OneDutyIssue = """
        {
          "rows": [
            {
              "key": "SAAS-9",
              "title": "Nedbrud på betalingerne",
              "url": "https://jira.test/browse/SAAS-9",
              "deadline": "2026-08-17",
              "status": "Afventer general",
              "isWaiting": false,
              "isDuty": true,
              "alreadyImported": false
            }
          ],
          "total": 1
        }
        """;

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    /// <summary>
    /// Without a token there is nothing to fetch, and the screen says so in words with a way to the
    /// page that fixes it. The absent Load button is asserted too: the sentence alone would also be
    /// there on a screen that had both, and then the guard would be about nothing.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_screen_says_so_and_links_to_the_settings()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var jira = await App.GoToJira();

        await Assertions.Expect(jira.NotConfigured)
            .ToHaveTextAsync("Jira er ikke sat op, så der er ingen sager at hente.");
        await Assertions.Expect(jira.PreviewButton).ToHaveCountAsync(0);

        await jira.SettingsLink.ClickAsync();

        await Assertions.Expect(new SettingsScreen(App).Language).ToBeVisibleAsync();
    }

    /// <summary>
    /// A blocked row stays on screen with its reason rather than disappearing: hidden, the issue
    /// would look like something Jira had lost, and the "import waiting issues too" setting would
    /// be invisible. The two reasons are asserted as different strings, because one shared sentence
    /// for both would leave the user unable to tell a setting from a duplicate.
    /// </summary>
    [Fact]
    public async Task A_preview_shows_the_blocked_rows_switched_off_with_their_own_reasons()
    {
        await ConfigureJiraAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        await AnswerPreviewWithAsync(ThreeIssues);

        var jira = await App.GoToJira();
        await jira.PreviewAsync();

        await Assertions.Expect(jira.Rows).ToHaveCountAsync(3);
        await Assertions.Expect(jira.Showing).ToHaveTextAsync("Viser 3 af 3 sager.");

        var importable = jira.Row(ImportedTitle);
        var waiting = jira.Row(WaitingTitle);
        var seenBefore = jira.Row(SeenBeforeTitle);

        // The one that can be imported: ticked by default, so the Import button means something
        // without a click. Asserted first, because a screen where nothing at all was tickable
        // would satisfy every disabled-checkbox claim below.
        await Assertions.Expect(JiraImportScreen.PickOf(importable)).ToBeEnabledAsync();
        await Assertions.Expect(JiraImportScreen.PickOf(importable)).ToBeCheckedAsync();

        await Assertions.Expect(JiraImportScreen.ExcludedIn(waiting)).ToHaveTextAsync(WaitingReason);
        await Assertions.Expect(JiraImportScreen.PickOf(waiting)).ToBeDisabledAsync();
        await Assertions.Expect(JiraImportScreen.PickOf(waiting)).Not.ToBeCheckedAsync();

        await Assertions.Expect(JiraImportScreen.AlreadyImportedIn(seenBefore))
            .ToHaveTextAsync(SeenBeforeReason);
        await Assertions.Expect(JiraImportScreen.PickOf(seenBefore)).ToBeDisabledAsync();
        await Assertions.Expect(JiraImportScreen.PickOf(seenBefore)).Not.ToBeCheckedAsync();

        // Neither reason may stand in for the other, and the strings above only prove that while
        // they differ. Said out loud so a later edit that made them one sentence fails here.
        Assert.NotEqual(WaitingReason, SeenBeforeReason);

        // Nothing-to-select is the sentence for a preview where every row is blocked, and this one
        // has a tickable row. Absent rather than untested: it would otherwise be pure decoration.
        await Assertions.Expect(jira.NothingToSelect).ToHaveCountAsync(0);
        await Assertions.Expect(jira.NoneAssigned).ToHaveCountAsync(0);

        // Every row carries an Open-the-issue button now, and a control more per row is exactly the
        // kind of thing that pushes the column sideways. Compared with clientWidth rather than with
        // 480: a vertical scrollbar makes the layout 465 wide, and a fixed number would fail for
        // that reason instead of this one.
        var pageWidth = await App.ClientWidthAsync();
        var scrolledWidth = await App.ScrollWidthAsync();

        Assert.True(scrolledWidth <= pageWidth,
            $"The preview rows push the page sideways: scrollWidth was {scrolledWidth} against a "
            + $"clientWidth of {pageWidth}.");
    }

    /// <summary>
    /// Only the selectable rows go on the wire, and the receipt says how many landed. The request
    /// body is read rather than inferred from the receipt: the receipt is the server's number, so a
    /// screen that had posted all three rows would print exactly the same line.
    /// </summary>
    [Fact]
    public async Task Importing_sends_only_the_selected_rows_and_the_screen_says_how_many()
    {
        await ConfigureJiraAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        await AnswerPreviewWithAsync(ThreeIssues);

        var posted = new TaskCompletionSource<JsonElement?>();

        await App.Page.RouteAsync("**/api/jira/import", async route =>
        {
            posted.TrySetResult(route.Request.PostDataJSON());

            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{ "imported": 1, "skipped": 0 }""",
            });
        });

        var jira = await App.GoToJira();
        await jira.PreviewAsync();

        // The count on the button is the selection made visible: three rows came back and one of
        // them is offered.
        await Assertions.Expect(jira.ImportButton).ToHaveTextAsync("Importér 1 sag");

        await jira.ImportAsync();

        await Assertions.Expect(jira.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");

        var body = await posted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var rows = body?.GetProperty("rows").EnumerateArray().ToList() ?? [];

        var row = Assert.Single(rows);

        Assert.Equal("SAAS-1", row.GetProperty("key").GetString());
        Assert.Equal(ImportedTitle, row.GetProperty("title").GetString());
    }

    /// <summary>
    /// The link on an imported task, which only a Jira-sourced task has: the endpoint computes
    /// <c>externalUrl</c> from the source, so no other fixture can render this button. It is a
    /// &lt;button&gt; rather than an &lt;a href&gt; because the Photino window has neither an
    /// address bar nor a back button — the tag name is the only thing stopping that simplification.
    /// </summary>
    [Fact]
    public async Task The_link_on_an_imported_task_asks_the_shell_for_the_issue()
    {
        await ConfigureJiraAsync();
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(ImportedTitle).FromJira("SAAS-1").DueToday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var opened = new TaskCompletionSource<string?>();

        await App.Page.RouteAsync("**/api/system/open-link", async route =>
        {
            opened.TrySetResult(route.Request.PostDataJSON()?.GetProperty("url").GetString());
            await route.AbortAsync();
        });

        var link = App.Tasks.ExternalLinkIn(ImportedTitle);

        await Assertions.Expect(link).ToHaveTextAsync("Åbn sagen");
        Assert.Equal("BUTTON", await link.EvaluateAsync<string>("el => el.tagName"));

        await App.Page.EvaluateAsync("window.stampedBeforeTheClick = true");

        await link.ClickAsync();

        Assert.Equal(
            $"{BaseUrl}/browse/SAAS-1",
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(
            await App.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The click took the window with it, and this window has no way back.");
    }

    /// <summary>
    /// The preview's own button, which is what this slice is for: an issue can be read <em>before</em>
    /// anyone decides to import it. The URL comes from the answer's <c>url</c> field, and the field
    /// is asserted through the request body rather than through an attribute — a button has no href
    /// to read, and the body is what actually reaches the shell.
    ///
    /// It is a &lt;button&gt; rather than an &lt;a href&gt; because the Photino window has neither
    /// an address bar nor a back button, and the tag name is the only thing stopping that
    /// simplification. <c>/api/system/open-link</c> is aborted rather than answered: letting it
    /// through would open a real browser window on the machine running the tests.
    /// </summary>
    [Fact]
    public async Task Opening_a_previewed_issue_asks_the_shell_for_it_without_importing_it()
    {
        await ConfigureJiraAsync();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await AnswerPreviewWithAsync(OneDutyIssue);

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

        var jira = await App.GoToJira();
        await jira.PreviewAsync();

        var row = jira.Row(DutyTitle);
        var openIssue = JiraImportScreen.OpenIssueIn(row);

        await Assertions.Expect(openIssue).ToHaveTextAsync("Åbn sagen");
        Assert.Equal("BUTTON", await openIssue.EvaluateAsync<string>("el => el.tagName"));

        await App.Page.EvaluateAsync("window.stampedBeforeTheClick = true");

        await openIssue.ClickAsync();

        Assert.Equal(
            $"{BaseUrl}/browse/SAAS-9",
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(
            await App.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The click took the window with it, and this window has no way back.");

        // And the failure is said out loud, beside the row that was pressed. The abort above is what
        // makes this free: an aborted request is a failure to the client, so the error path is
        // reached without any staging of its own.
        //
        // Worth knowing for anyone reading this later: a *successful* open cannot be measured here at
        // all, because the call is always aborted. After a click the failing branch is the only one
        // there is, and "no message" is the state before the click — which is what the row is
        // asserted empty for further up in the Vitest suite.
        await Assertions.Expect(JiraImportScreen.OpenErrorIn(row))
            .ToHaveTextAsync("Noget gik galt. Prøv igen.");

        // The button sits outside the row's <label> on purpose, and this is the assertion that
        // measures it: a label's text becomes the accessible name of the control inside it, so a
        // button moved in there would leave the checkbox announced as "… Åbn sagen".
        //
        // Said this way rather than as "the tick survived the click", which was the first version
        // and could not fail: a <button> inside a <label> is interactive content, so the browser
        // skips the label's activation behaviour and the checkbox does not toggle. Measured —
        // moving the button inside the label left that assertion green.
        await Assertions
            .Expect(row.GetByRole(AriaRole.Checkbox, new() { Name = "Åbn sagen" }))
            .ToHaveCountAsync(0);

        // And the tick is still there, which is what makes the claim above about this row: a
        // count of zero would also hold on a screen with no checkbox at all.
        await Assertions.Expect(JiraImportScreen.PickOf(row)).ToBeCheckedAsync();
    }

    /// <summary>
    /// The whole duty rotation in one journey: switch it on, preview, see the pool's issue labelled,
    /// import it, and find it among the deadline sections instead of under "Venter på".
    ///
    /// The last leg is the one that matters, and it is the only assertion here that would catch the
    /// decision being reversed. The requirement is not that a label appears — it is that the issue
    /// stays out of "Venter på", because it waits for the pool and the user <em>is</em> the pool. A
    /// journey that stopped at the label would measure the decoration and leave the rule untested.
    ///
    /// <c>/api/jira/import</c> is deliberately <em>not</em> intercepted: the server re-derives the
    /// role from the settings as they stand, and that derivation is the subject. Only the preview is
    /// answered from here, because a real Jira cannot be reached from the browser.
    /// </summary>
    [Fact]
    public async Task A_duty_issue_imports_into_the_deadline_sections_rather_than_among_the_waiting()
    {
        await ConfigureJiraAsync();
        await Host.AddAndSaveChangesAsync(
            new Setting
            {
                Key = SettingKeys.JiraWaitingStatuses,
                Value = $"[\"{DutyStatus}\"]",
            },
            new Setting
            {
                Key = SettingKeys.JiraDutyStatuses,
                Value = $"[\"{DutyStatus}\"]",
            });

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        await AnswerPreviewWithAsync(OneDutyIssue);

        // Nothing here clicks the issue's link, but a stray click must not reach the machine.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

        // Ticked through the page rather than seeded: the switch is the user's whole say in this,
        // and the server reads it back from the settings on both the preview and the import.
        var settings = await App.GoToSettings();

        await settings.OnDuty.CheckAsync();
        await Assertions.Expect(settings.OnDuty).ToBeCheckedAsync();

        var jira = await settings.GoToJira();

        // The only place the state is visible. Words rather than an end date, because an end date
        // would need something to run at midnight.
        await Assertions.Expect(jira.OnDutyNotice).ToHaveTextAsync(OnDutyNotice);

        await jira.PreviewAsync();

        var row = jira.Row(DutyTitle);

        await Assertions.Expect(JiraImportScreen.DutyIn(row)).ToHaveTextAsync(DutyLabel);

        // Context, not a block: the pool's issue is offered like any other actionable row. Asserted
        // because a labelled row that could not be ticked would satisfy the label claim above and
        // still make the import below impossible.
        await Assertions.Expect(JiraImportScreen.PickOf(row)).ToBeEnabledAsync();
        await Assertions.Expect(JiraImportScreen.PickOf(row)).ToBeCheckedAsync();
        await Assertions.Expect(JiraImportScreen.ExcludedIn(row)).ToHaveCountAsync(0);

        await jira.ImportAsync();
        await Assertions.Expect(jira.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");

        var tasks = await jira.GoToTasks();

        // The leg the whole journey is for. With the switch on, the status is actionable: the issue
        // belongs to the day's work under its deadline. Turn the switch off, or let the import park
        // a duty row as WaitingFor, and this is what fails.
        await Assertions.Expect(tasks.RowsIn(TodaySection)).ToContainTextAsync(DutyTitle);

        // Said from the other side as well, because "it is in I dag" would also hold on a screen
        // that showed it twice — and "Venter på" is the section the rule is about staying out of.
        await Assertions.Expect(tasks.WaitingRows).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.WaitingSection).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Enough settings for the screen to offer the Load button and for the task endpoint to compute
    /// a browse URL. Written straight to the database rather than typed in: what a token does to the
    /// settings page is <see cref="SettingsJourneyTests"/>' business, not this suite's.
    /// </summary>
    private Task ConfigureJiraAsync() => Host.AddAndSaveChangesAsync(
        new Setting { Key = SettingKeys.JiraBaseUrl, Value = BaseUrl },
        new Setting { Key = SettingKeys.JiraProjectKey, Value = ProjectKey },
        new Setting { Key = SettingKeys.JiraToken, Value = "not-a-real-token" });

    private Task AnswerPreviewWithAsync(string json)
        => App.Page.RouteAsync("**/api/jira/preview", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = json,
        }));
}
