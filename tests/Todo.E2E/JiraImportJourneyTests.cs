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

    /// <summary>
    /// Three issues: one that can be imported, one excluded because the user is waiting on it, and
    /// one imported on an earlier run. The last two are what make the two different reasons
    /// measurable — a fixture with only one blocked row could not tell them apart.
    /// </summary>
    private const string ThreeIssues = """
        {
          "rows": [
            {
              "key": "SAAS-1",
              "title": "Ret rapporten",
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
              "status": "Venter på kunde",
              "isWaiting": true,
              "waitingSince": "2026-08-05T09:12:00Z",
              "alreadyImported": false,
              "excluded": "jira.excludedWaiting"
            },
            {
              "key": "SAAS-3",
              "title": "Skriv testene",
              "status": "I gang",
              "isWaiting": false,
              "alreadyImported": true
            }
          ],
          "total": 3
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
