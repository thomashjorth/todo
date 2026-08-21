using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Settings;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// WCAG AA over all five screens, in both colour schemes — <c>app.routes.ts</c> has exactly five
/// routes: the task list, the retro import, the Jira import, the Azure DevOps import and the
/// settings. The measurement runs in the browser because only it knows which background a given
/// piece of text ended up on.
///
/// The theories below number the screens in the order this class reaches them: the first covers the
/// task list, the retro import and the settings, so the Jira import is the fourth and the Azure
/// DevOps import the fifth. The expanded detail panel, the folded settings page and the two-column
/// layout are states rather than screens; they cost snapshots, not routes.
///
/// Everything except the two side-by-side theories opens the app at 480px, which is the whole reason
/// those two exist: the wide layout puts the same text under different ancestors, and an opaque
/// background stops the guard's walk up the tree.
/// </summary>
public class ContrastTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    /// <summary>Comfortably past the xl breakpoint, so no assertion turns on a scrollbar.</summary>
    private const int WideWidth = 1400;
    private const int DaysWaited = 12;
    private const string OverdueTitle = "Betal regningen";
    private const string DueTodayTitle = "Send referatet";
    private const string WaitingTitle = "Svar revisoren";
    private const string CompletedTitle = "Ryd skrivebordet";

    /// <summary>A task under way, so the row's in-progress marker is painted at all.</summary>
    private const string InProgressTitle = "Skriv indstillingerne om";
    private const string SomedayTitle = "Læs om typografi";
    private const string ConflictTitle = "Bestil nyt pas";

    /// <summary>
    /// A task imported from Jira, which is the only kind that renders the link on a row: the task
    /// endpoint computes <c>externalUrl</c> only when the source is Jira, so no other fixture puts
    /// that branch on screen at all.
    /// </summary>
    private const string JiraTitle = "Ret rapporten";

    /// <summary>
    /// Enough for <see cref="Todo.Core.Jira.JiraSettings.BrowseUrl"/> to produce a link. Nothing in
    /// this suite ever calls it — the app's own calls to Jira are intercepted below.
    /// </summary>
    private const string JiraBaseUrl = "https://jira.test";

    private const string JiraProjectKey = "SAAS";
    private const string Requester = "Mette Kirkegaard";
    private const string WaitingOn = "Mette";
    private const string DoneSubTask = "Find det gamle referat";
    private const string OpenSubTask = "Skriv udkastet";
    private const string EmptyList = "Ingen opgaver.";
    private const string Me = "Thomas Hjorth Hansen";
    private const string MyAction = "Skriv referatet fra retroen";

    /// <summary>
    /// Somebody tasks are handed to. Deliberately not <see cref="Me"/> and not <see cref="Requester"/>:
    /// a delegate is a third meaning — who you are waiting on — and a name shared with the alias list
    /// would let a row from one list stand in for the other.
    /// </summary>
    private const string DelegateName = "Flemming Overgaard";

    /// <summary>
    /// Everything @tailwindcss/typography gives a colour of its own: a heading, body text, a link,
    /// inline code, a fenced block, bullets, a quote and a table. The plugin brings a whole colour
    /// system with it plus the prose-invert swap, so an element left out of this note is a colour
    /// the guard cannot see. Normalised to LF because a textarea hands its value back that way
    /// whatever went in.
    /// </summary>
    private static readonly string Note = """
        ## Dagsorden

        **Husk** at gennemgå det hele inden vi går på, og kør `dotnet test` først.

        Dagsordenen ligger [her](https://example.com/dagsorden).

        - Lyd
        - Lys

        > Mette kommer ti minutter senere.

        ```bash
        dotnet test Todo.sln
        ```

        | Deltager | Rolle |
        | --- | --- |
        | Mette Kirkegaard | Produktejer |
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// Shaped like the board in <see cref="RetroImportJourneyTests"/>: one action of mine, one
    /// somebody else's, and a rating card to be skipped.
    /// </summary>
    private static readonly string Board = """
        "Content","Author","Created","Zone","Action Due Date","Action Owner"
        "Thomas Hjorth Hansen - Skriv referatet fra retroen","Mette Kirkegaard","7/17/26, 1:32 PM","Actions","24.7.2026","Thomas Hjorth Hansen"
        "Book et lokale til næste gang","Rasmus Bjerre","7/17/26, 1:33 PM","Actions","","Mette Kirkegaard"
        "9/10","Sofie Dalgaard","7/17/26, 1:34 PM","Mood","",""
        """;

    /// <summary>
    /// What a refused preview looks like on the wire: the code is the translation key, so this is
    /// also what decides which red sentence the screen paints.
    /// </summary>
    private const string Unreachable =
        """{ "code": "jira.unreachable", "message": "Jira could not be reached." }""";

    private const string NoIssues = """{ "rows": [], "total": 0 }""";

    /// <summary>
    /// The two duty branches' text, named so the waits below are on the string a user reads rather
    /// than on the element that will hold it.
    /// </summary>
    private const string OnDutyNotice = "Du har vagten — 2nd. level-sagerne er med.";

    private const string DutyLabel = "fra 2nd. level supporten";

    /// <summary>
    /// The preview row's own Open-the-issue button. Same label as the imported task's link, because
    /// it is the same key: one action on one kind of thing.
    /// </summary>
    private const string OpenIssueLabel = "Åbn sagen";

    /// <summary>
    /// What the row says when the shell could not be asked. An aborted request carries no error code,
    /// so <c>apiErrorMessage</c> falls back to the generic sentence — waited for by its text so the
    /// measurement never runs on an empty paragraph.
    /// </summary>
    private const string OpenFailed = "Noget gik galt. Prøv igen.";

    /// <summary>
    /// Two rows nothing can be done with, for different reasons: one the user is waiting on, one
    /// imported on an earlier run. Neither carries a deadline, so the row's deadline line is
    /// measured on the importable row below rather than here — both sides of that branch.
    ///
    /// <c>url</c> is spelled out on every row here for the same reason <c>isDuty</c> is: the field
    /// is required on the contract, so no branch hides the Open-the-issue button, but an answer
    /// without the field reads as undefined in the client and the button would carry an empty
    /// address. Intercepting the call is not enough — the body has to carry the field.
    /// </summary>
    private const string BlockedIssues = """
        {
          "rows": [
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
          "total": 2
        }
        """;

    /// <summary>
    /// <c>isDuty</c> is on the row on purpose, and it has to be spelled out here: the field is the
    /// only thing the duty label hangs on, so a preview answer that leaves it out reads as
    /// undefined in the client and paints no label at all. Intercepting the call is not enough —
    /// the body has to carry the field. Same shape of hole as the eleven branches slice 11 left
    /// behind, one level deeper.
    /// </summary>
    private const string OneImportableIssue = """
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
              "isDuty": true,
              "alreadyImported": false
            }
          ],
          "total": 1
        }
        """;

    /// <summary>
    /// Enough for <see cref="Todo.Core.Ado.AdoSettings.BrowseUrl"/> to produce a link, and spelled with
    /// the space the real collection has. Nothing in this suite ever calls it.
    /// </summary>
    private const string AdoBaseUrl = "https://ado.test/Fake%20Collection";

    private const string AdoProject = "Saas";

    private const string AdoUnreachable =
        """{ "code": "ado.unreachable", "message": "Azure DevOps could not be reached." }""";

    private const string AdoRefusedToken =
        """{ "code": "ado.refused", "message": "Azure DevOps refused the token." }""";

    private const string NoWorkItems = """{ "rows": [], "total": 0 }""";

    /// <summary>
    /// Two rows nothing can be done with, for different reasons: one the user is waiting on, one
    /// imported on an earlier run. The waiting one carries <c>waitingSince</c> and <em>no</em>
    /// deadline, the other carries a deadline — which is how both halves of the deadline branch get a
    /// colour on one screen. That branch is the one no Jira row has, because Jira's due date came off
    /// the issue while this one is the app's own arithmetic.
    ///
    /// <c>workItemType</c>, <c>state</c>, <c>isWaiting</c>, <c>alreadyImported</c> and <c>url</c> are
    /// spelled out on every row, and they have to be: intercepting the call is not enough, because a
    /// field the body leaves out reads as undefined in the client and its line renders nothing at all —
    /// while the route handler looks like it covered the case. There is no <c>isDuty</c>: Azure DevOps
    /// has no duty pool, so the label has no counterpart here.
    /// </summary>
    private const string BlockedWorkItems = """
        {
          "rows": [
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
          "total": 2
        }
        """;

    /// <summary>
    /// One row that can be imported, carrying the three optional fields the blocked rows leave out:
    /// a note, a requester and a proposed deadline.
    /// </summary>
    private const string OneImportableWorkItem = """
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
            }
          ],
          "total": 1
        }
        """;

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Every_screen_meets_WCAG_AA(ColorScheme scheme)
    {
        // One task per state, so no section of the list goes unmeasured — and the states only a
        // field can carry (a note, subtasks, an opgavestiller) seeded onto them, so the elements
        // those fields render are measured too.
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(OverdueTitle).Overdue()
                .RequestedBy(Requester).Build(),
            new TaskItemBuilder(Clock).Titled(DueTodayTitle).DueToday()
                .WithNote(Note)
                .WithSubTask(DoneSubTask, isDone: true)
                .WithSubTask(OpenSubTask)
                .Build(),
            new TaskItemBuilder(Clock).Titled(WaitingTitle)
                .WaitingFor(WaitingOn, Clock.UtcNow.AddDays(-DaysWaited)).Build(),
            new TaskItemBuilder(Clock).Titled(SomedayTitle).Someday().Build(),
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build(),
            // In progress, which puts a marker on the row in its own colour pair. Nothing else in
            // this fixture is in that status, so without this task the marker is never painted and
            // its blue is a colour no test has looked at.
            new TaskItemBuilder(Clock).Titled(InProgressTitle).InProgress().DueToday().Build(),
            // A start date after the deadline is allowed, and the panel says so in amber. The
            // row itself lands in Overskredet, because Overdue beats Deferred — the hint lives
            // in the panel regardless, and a colour no test renders is a colour unmeasured.
            new TaskItemBuilder(Clock).Titled(ConflictTitle).Overdue()
                .DeferredUntil(Clock.Today.AddDays(3)).Build(),
            // Imported from Jira, so the row carries the link that opens the issue. The base URL
            // has to be stored as well: the link is computed from it, and without one the branch
            // stays absent however the source is spelled.
            new TaskItemBuilder(Clock).Titled(JiraTitle).FromJira("SAAS-1").DueToday().Build(),
            new Setting { Key = SettingKeys.JiraBaseUrl, Value = JiraBaseUrl });

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1400 }, scheme);

        // A test run has no business asking the operating system to open a browser window, so the
        // note's link never leaves this process. The abort is also what puts the note's error line
        // on screen further down: a failed request is what that line is there for.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

        var failures = new List<string>();
        var tasks = App.Tasks;

        async Task Snapshot() => failures.AddRange(await App.ContrastFailuresAsync());

        // The health line only exists once /api/health has answered, and it is one of the four
        // colours this test was written around. Both of its branches render text, so waiting for
        // the prefix anchors the line without deciding which branch it ought to be.
        await Assertions.Expect(App.Health).ToContainTextAsync("API:");

        // The two switches reveal the completed and someday sections. Each measurement below waits
        // for the content it is there to measure: a switch only sets a signal, and a measurement
        // that raced the render would skip whole sections — which would later let this test go
        // green over text it never looked at.
        await tasks.ShowCompleted.CheckAsync();
        await tasks.ShowSomeday.CheckAsync();

        // Waiting on the text, not just the element, is what makes these waits real: a freshly
        // rendered component has its elements in place before the localized strings are
        // interpolated into them, and an element with no text yet is invisible to the measurement.
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);
        await Assertions.Expect(tasks.SomedayRows).ToContainTextAsync(SomedayTitle);
        await Assertions.Expect(tasks.RowFor(OverdueTitle))
            .ToContainTextAsync($"Opgavestiller: {Requester}");
        await Assertions.Expect(tasks.SubTaskProgress).ToHaveTextAsync("1/2");

        // The link sits outside the row's button and only on a Jira-sourced task. Waited for by its
        // text, because the button element exists before the localized label is interpolated into
        // it — and an element with no text yet is invisible to the measurement.
        await Assertions.Expect(tasks.ExternalLinkIn(JiraTitle)).ToHaveTextAsync("Åbn sagen");
        await Snapshot();

        // The detail panel is the largest single block of colour, and it only exists expanded.
        // RowShowing, not RowTitled: the deadline and the subtask count join this row's
        // accessible name.
        await tasks.RowShowing(DueTodayTitle).ClickAsync();
        await Assertions.Expect(tasks.Detail).ToContainTextAsync("Underopgaver");
        await Assertions.Expect(tasks.SubTaskRows).ToHaveCountAsync(2);
        await Assertions.Expect(tasks.SubTaskRows.Nth(0)).ToContainTextAsync(DoneSubTask);
        await Assertions.Expect(tasks.SubTaskRows.Nth(1)).ToContainTextAsync(OpenSubTask);

        // The rendered note is @tailwindcss/typography's colour system rather than the app's, so
        // every element it styles is waited for by name before anything is measured.
        await Assertions.Expect(tasks.NoteRendered.Locator("h2")).ToHaveTextAsync("Dagsorden");
        await Assertions.Expect(tasks.NoteRendered.Locator("code").First)
            .ToHaveTextAsync("dotnet test");
        await Assertions.Expect(tasks.NoteRendered.Locator("blockquote")).ToContainTextAsync("Mette");
        await Assertions.Expect(tasks.NoteRendered.Locator("pre"))
            .ToContainTextAsync("dotnet test Todo.sln");
        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(["Lyd", "Lys"]);
        await Assertions.Expect(tasks.NoteLink).ToHaveTextAsync("her");
        await Assertions.Expect(tasks.NoteTable.Locator("th").First).ToHaveTextAsync("Deltager");
        await Snapshot();

        // The editor takes the rendered note's place, so the textarea is a state of its own.
        await tasks.NoteEditButton.ClickAsync();
        await Assertions.Expect(tasks.NoteEditor).ToHaveValueAsync(Note);
        await Snapshot();

        await tasks.NoteEditor.PressAsync("Escape");
        await Assertions.Expect(tasks.NoteRendered.Locator("h2")).ToHaveTextAsync("Dagsorden");

        // The link's request is aborted above, so this is the app's own failure path — the line a
        // dead API would leave here — rather than a state staged from the outside.
        await tasks.NoteLink.ClickAsync();
        await Assertions.Expect(tasks.NoteLinkError).ToContainTextAsync("Prøv igen");
        await Snapshot();

        // Only the expanded row has a panel, and the row expanded above is not the waiting one:
        // without this second click the waiting-on label and field never render at all.
        await tasks.RowShowing(WaitingTitle).ClickAsync();
        await Assertions.Expect(tasks.WaitingOnInput).ToHaveValueAsync(WaitingOn);
        await Assertions.Expect(tasks.DetailFor(WaitingTitle)).ToContainTextAsync("Venter på");
        await Snapshot();

        // The conflicting start date renders one line and only while its row is open, so this
        // click is what puts the amber pair on screen at all. Waiting on the text rather than the
        // element: an interpolation that had not run yet would be measured as no text.
        await tasks.RowShowing(ConflictTitle).ClickAsync();
        await Assertions.Expect(tasks.DeferUntilConflict)
            .ToHaveTextAsync("Startdatoen ligger efter deadline, så opgaven vises som overskredet.");
        await Snapshot();

        // The search box narrows the list, and the line it leaves behind when nothing matches is a
        // branch of its own: "no tasks" and "nothing matched" are two different sentences in the same
        // place, and only one of them renders without a search. Typed here rather than trusted to
        // look like its neighbour - the empty-list message beside it carries the same classes, and
        // "same classes" is exactly the reasoning that leaves a branch unpainted.
        await tasks.Search.FillAsync("ingenting der findes");
        await Assertions.Expect(tasks.NoMatches)
            .ToHaveTextAsync("Ingen opgaver passer på søgningen.");
        await Snapshot();

        await tasks.Search.FillAsync(string.Empty);
        await Assertions.Expect(tasks.Rows.First).ToBeVisibleAsync();

        var import = await App.GoToImport();
        await Assertions.Expect(import.AnalyseButton).ToHaveTextAsync("Analysér");
        await Snapshot();

        // Everything under @if (analysed()) needs a board pasted and analysed. With no alias
        // stored yet, this pass covers the none-mine line and the disabled import button.
        await import.PasteAsync(Board);
        await import.AnalyseAsync();
        await Assertions.Expect(import.Rows).ToHaveCountAsync(2);
        await Assertions.Expect(import.Rows.First).ToContainTextAsync("Zone: Actions");
        await Assertions.Expect(import.Rows.First).ToContainTextAsync($"Ejer: {Me}");
        await Assertions.Expect(import.Skipped).ToHaveTextAsync("Sprang 1 afstemningskort over.");
        await Assertions.Expect(import.NoneMine).ToContainTextAsync("ejer");
        await Assertions.Expect(import.ImportButton).ToHaveTextAsync("Importér 0 opgaver");
        await Snapshot();

        var settings = await App.GoToSettings();
        await Assertions.Expect(settings.Heading).ToHaveTextAsync("Indstillinger");

        // The page arrives with all five groups folded, which is a screen state of its own: five
        // heading buttons and their chevrons, and nothing else. Snapshotted before anything is
        // opened, because it is not reachable again afterwards — one group is always open from here.
        await Snapshot();

        // A folded group's panel is out of the DOM, so the guard cannot see a colour in it: every
        // group has to be unfolded in turn to be measured at all. The language select is the whole
        // of the first one.
        await settings.OpenAsync(SettingsScreen.LanguageSection);
        await Assertions.Expect(settings.Language).ToBeVisibleAsync();
        await Snapshot();

        // The delegate group's empty branch, which is the default state — so this snapshot is the
        // one that measures it. Waited for by its text rather than by the element: a paragraph the
        // localized string has not been interpolated into yet is invisible to the measurement.
        await settings.OpenAsync(SettingsScreen.DelegateSection);
        await Assertions.Expect(settings.DelegatesEmpty).ToContainTextAsync("Ingen på listen endnu");
        await Assertions.Expect(settings.DelegateRows).ToHaveCountAsync(0);
        await Snapshot();

        // The other half of that branch, and it cannot be staged: nothing in this suite stubs
        // /api/settings, so a row exists only because the page saved a name through the real
        // backend. The empty sentence is gone by then, which is why it is measured above.
        await settings.AddDelegateAsync(DelegateName);
        await Assertions.Expect(settings.DelegatesEmpty).ToHaveCountAsync(0);
        await Snapshot();

        // The list's own red line, a third branch. Provoked through the field a user types in: the
        // screen drops a name it already holds only when it matches exactly, so a name differing
        // only in case travels to the API and comes back rejected.
        await settings.SubmitDelegateAsync(DelegateName.ToLowerInvariant());
        await Assertions.Expect(settings.DelegatesError).ToContainTextAsync("mere end én gang");
        await Snapshot();

        // An alias is the only thing that makes alias-row and its remove button exist.
        await settings.AddAliasAsync(Me);
        await Assertions.Expect(settings.AliasRows).ToContainTextAsync(Me);
        await Snapshot();

        // The screen drops a name it already holds, but only when it matches exactly — so a name
        // differing only in case travels to the API and comes back rejected. The red line is the
        // app's own, provoked through the field a user types in.
        await settings.SubmitAliasAsync(Me.ToLowerInvariant());
        await Assertions.Expect(settings.Error).ToContainTextAsync("mere end én gang");
        await Snapshot();

        // The screen is built anew on the way back, so the board has to be analysed again — now
        // with the alias in place, which claims my row and enables the import button.
        import = await settings.GoToImport();

        await import.PasteAsync(Board);
        await import.AnalyseAsync();
        await Assertions.Expect(import.NoneMine).ToHaveCountAsync(0);
        await Assertions.Expect(import.Row(MyAction)).ToContainTextAsync(MyAction);
        await Assertions.Expect(import.ImportButton).ToHaveTextAsync("Importér 1 opgave");
        await Snapshot();

        // Importing re-analyses on its own, and that is what marks the row as seen before.
        await import.ImportAsync();
        await Assertions.Expect(import.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");
        await Assertions.Expect(import.AlreadyImported).ToHaveTextAsync("importeret tidligere");
        await Snapshot();

        AssertNoFailures(failures, scheme);
    }

    /// <summary>
    /// The fourth screen, and the Jira half of the settings — eleven branches the journey above
    /// cannot reach, because every one of them needs an answer from Jira. Playwright cannot start a
    /// <c>FakeJira</c> inside the host's process, so the app's own calls are intercepted, the same
    /// grip <c>/api/system/open-link</c> is held with. Four different answers are needed, and they
    /// are given in this order because each one leaves the screen in the state the next branch
    /// depends on: a refusal, an empty list, a list where every row is blocked, and a list with one
    /// row that can be imported.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task The_Jira_screens_meet_WCAG_AA(ColorScheme scheme)
    {
        // A base URL and a project key but deliberately no token: an unconfigured screen is the
        // default state, and it is the branch the user meets first.
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.JiraBaseUrl, Value = JiraBaseUrl },
            new Setting { Key = SettingKeys.JiraProjectKey, Value = JiraProjectKey });

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1600 }, scheme);

        var failures = new List<string>();

        async Task Snapshot() => failures.AddRange(await App.ContrastFailuresAsync());

        // Mutable on purpose: one route handler that answers with whatever the journey has reached,
        // rather than four handlers whose precedence would decide the outcome.
        var answer = (Status: 400, Body: Unreachable);

        await App.Page.RouteAsync("**/api/jira/preview", route => route.FulfillAsync(new()
        {
            Status = answer.Status,
            ContentType = "application/json",
            Body = answer.Body,
        }));

        await App.Page.RouteAsync("**/api/jira/test", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = $$"""{ "displayName": "{{Me}}" }""",
        }));

        await App.Page.RouteAsync("**/api/jira/statuses", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{ "names": ["I gang", "Venter på kunde"] }""",
        }));

        await App.Page.RouteAsync("**/api/jira/import", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{ "imported": 1, "skipped": 0 }""",
        }));

        // A test run has no business asking the operating system to open a browser window. The abort
        // is also what paints the row's own red line further down: a failed open is what that line
        // exists for, and it cannot be reached any other way from here.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

        // Unconfigured: a sentence and a link to the page that fixes it, and no Load button at all.
        var jira = await App.GoToJira();

        await Assertions.Expect(jira.NotConfigured)
            .ToHaveTextAsync("Jira er ikke sat op, så der er ingen sager at hente.");
        await Assertions.Expect(jira.SettingsLink)
            .ToHaveTextAsync("Sæt Jira op under Indstillinger");
        await Snapshot();

        // The token is stored through the page rather than seeded, because storing it is what puts
        // the "a token is held" line and the Clear button on screen — two branches of their own.
        var settings = await jira.GoToSettings();

        // The group's own red line first, because storing a real token below is what clears it. It
        // is the only branch here nothing else paints, and there is exactly one way in: the token
        // route refuses a blank one, while PUT /api/settings validates no Jira field at all.
        await settings.OpenAsync(SettingsScreen.JiraSection);
        await settings.SaveJiraToken.ClickAsync();
        await Assertions.Expect(settings.JiraSettingsError).ToContainTextAsync("må ikke være tomt");
        await Snapshot();

        await settings.StoreJiraTokenAsync("not-a-real-token");
        await Assertions.Expect(settings.JiraTokenStored).ToContainTextAsync("aldrig");
        await Assertions.Expect(settings.ClearJiraToken).ToHaveTextAsync("Ryd token");
        await Snapshot();

        await settings.TestJiraConnection.ClickAsync();
        await Assertions.Expect(settings.JiraConnection).ToHaveTextAsync($"Forbundet som {Me}");
        await Snapshot();

        // The status list replaces the sentence that stands in for it, so the rows are a state the
        // screen has no other way into.
        await settings.LoadJiraStatuses.ClickAsync();
        await Assertions.Expect(settings.JiraStatusRows).ToHaveCountAsync(2);
        await Assertions.Expect(settings.JiraStatusesEmpty).ToHaveCountAsync(0);
        await Snapshot();

        // The duty switch, ticked through the page rather than seeded: the import screen's notice
        // renders only while it is on, and the real backend answers off by default — so without
        // this click that paragraph is a colour no test ever paints. Waited for as checked, because
        // the tick travels to the server and comes back before the signal is set.
        await settings.OnDuty.CheckAsync();
        await Assertions.Expect(settings.OnDuty).ToBeCheckedAsync();
        await Snapshot();

        // Configured now, so the fourth screen has a Load button where the sentence used to be.
        jira = await settings.GoToJira();

        await Assertions.Expect(jira.PreviewButton).ToHaveTextAsync("Hent sager");
        await Assertions.Expect(jira.NotConfigured).ToHaveCountAsync(0);

        // The notice the click above turned on. Waited for by its text: the paragraph exists before
        // the localized string is interpolated into it, and no text is invisible to the measurement.
        await Assertions.Expect(jira.OnDutyNotice).ToHaveTextAsync(OnDutyNotice);
        await Snapshot();

        // A refusal. The red line is the app's own failure path, reached through the button a user
        // presses rather than staged from the outside.
        await jira.PreviewAsync();
        await Assertions.Expect(jira.Error)
            .ToHaveTextAsync("Jira kunne ikke nås. Kontrollér basisURL og netværket.");
        await Snapshot();

        // An empty answer is an answer, and it has a sentence of its own that the refusal above
        // must not be mistaken for.
        answer = (200, NoIssues);

        await jira.PreviewAsync();
        await Assertions.Expect(jira.NoneAssigned).ToHaveTextAsync("Ingen sager er tildelt dig.");
        await Assertions.Expect(jira.Error).ToHaveCountAsync(0);
        await Snapshot();

        // Every row blocked: both reasons on their own row, both sentences under the count, and the
        // Import button in its disabled colours — which are a pair of their own.
        answer = (200, BlockedIssues);

        await jira.PreviewAsync();
        await Assertions.Expect(jira.Rows).ToHaveCountAsync(2);
        await Assertions.Expect(jira.Showing).ToHaveTextAsync("Viser 2 af 2 sager.");
        await Assertions.Expect(jira.NothingToSelect)
            .ToContainTextAsync("1 sag er udeladt af importen.");
        await Assertions.Expect(jira.NothingToSelect)
            .ToContainTextAsync("1 sag er importeret tidligere.");
        await Assertions.Expect(JiraImportScreen.ExcludedIn(jira.Row("Afventer svar")))
            .ToContainTextAsync("slået fra");
        await Assertions.Expect(JiraImportScreen.AlreadyImportedIn(jira.Row("Skriv testene")))
            .ToHaveTextAsync("importeret tidligere");
        await Assertions.Expect(jira.ImportButton).ToBeDisabledAsync();
        await Snapshot();

        // One row that can be imported, which is what enables the button — a different colour from
        // the disabled one measured above — and then the receipt.
        answer = (200, OneImportableIssue);

        await jira.PreviewAsync();
        await Assertions.Expect(jira.ImportButton).ToHaveTextAsync("Importér 1 sag");
        await Assertions.Expect(jira.ImportButton).ToBeEnabledAsync();
        await Assertions.Expect(jira.NothingToSelect).ToHaveCountAsync(0);

        // The duty label on that row, which the answer above carries isDuty for. Waited for rather
        // than assumed: an absent field renders nothing, and a measurement of nothing is a pass.
        await Assertions.Expect(JiraImportScreen.DutyIn(jira.Row("Ret rapporten")))
            .ToHaveTextAsync(DutyLabel);

        // The button that opens the issue before anyone imports it. No branch guards it — the
        // contract makes <c>url</c> required — so this colour is measured without a fixture state
        // of its own. Waited for by its text all the same: the element is there before the
        // localized label is interpolated into it, and no text is invisible to the measurement.
        await Assertions.Expect(JiraImportScreen.OpenIssueIn(jira.Row("Ret rapporten")))
            .ToHaveTextAsync(OpenIssueLabel);

        // And the line that says the open failed, which is a branch of its own: the paragraph renders
        // only while this row is the one that failed, so without the click below its colour is one no
        // test ever paints. Pressed rather than staged — the route above aborts the call, and an
        // aborted call is a failure to the client.
        await JiraImportScreen.OpenIssueIn(jira.Row("Ret rapporten")).ClickAsync();
        await Assertions.Expect(JiraImportScreen.OpenErrorIn(jira.Row("Ret rapporten")))
            .ToHaveTextAsync(OpenFailed);
        await Snapshot();

        await jira.ImportAsync();
        await Assertions.Expect(jira.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");
        await Snapshot();

        AssertNoFailures(failures, scheme);
    }

    /// <summary>
    /// The fifth screen, and the Azure DevOps half of the settings — twenty-odd branches neither
    /// journey above can reach, because almost every one of them needs an answer from Azure DevOps.
    /// Playwright cannot start a <c>FakeAdo</c> inside the host's process, so the app's own calls are
    /// intercepted, the same grip <c>/api/system/open-link</c> is held with. Four preview answers are
    /// needed and they are given in this order because each leaves the screen in the state the next
    /// branch depends on: a refusal, an empty list, a list where every row is blocked, and a list with
    /// one row that can be imported.
    ///
    /// Two branches are reached without any route handler at all, and deliberately: the "imported
    /// earlier" line is the real database answering, and the settings group's own red line is the real
    /// backend refusing an empty work item type list. A staged answer would have measured the same
    /// colour while proving nothing about either.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task The_Ado_screens_meet_WCAG_AA(ColorScheme scheme)
    {
        // A collection URL and a project but deliberately no token: an unconfigured screen is the
        // default state, and it is the branch the user meets first.
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoBaseUrl, Value = AdoBaseUrl },
            new Setting { Key = SettingKeys.AdoProject, Value = AdoProject });

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1600 }, scheme);

        var failures = new List<string>();

        async Task Snapshot() => failures.AddRange(await App.ContrastFailuresAsync());

        // Mutable on purpose: one route handler that answers with whatever the journey has reached,
        // rather than several handlers whose precedence would decide the outcome.
        var preview = (Status: 400, Body: AdoUnreachable);
        var connection = (Status: 400, Body: AdoRefusedToken);

        await App.Page.RouteAsync("**/api/ado/preview", route => route.FulfillAsync(new()
        {
            Status = preview.Status,
            ContentType = "application/json",
            Body = preview.Body,
        }));

        await App.Page.RouteAsync("**/api/ado/test", route => route.FulfillAsync(new()
        {
            Status = connection.Status,
            ContentType = "application/json",
            Body = connection.Body,
        }));

        await App.Page.RouteAsync("**/api/ado/states", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{ "names": ["Active", "Blocked", "PO Review"] }""",
        }));

        await App.Page.RouteAsync("**/api/ado/import", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{ "imported": 1, "skipped": 0 }""",
        }));

        // A test run has no business asking the operating system to open a browser window. The abort is
        // also what paints the row's own red line further down: a failed open is what that line exists
        // for, and it cannot be reached any other way from here.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

        // Unconfigured: a sentence and a link to the page that fixes it, and no Load button at all.
        var ado = await App.GoToAdo();

        await Assertions.Expect(ado.NotConfigured)
            .ToHaveTextAsync("Azure DevOps er ikke sat op, så der er ingen sager at hente.");
        await Assertions.Expect(ado.SettingsLink)
            .ToHaveTextAsync("Sæt Azure DevOps op under Indstillinger");
        await Snapshot();

        // The token is stored through the page rather than seeded, because storing it is what puts the
        // "a token is held" line and the Clear button on screen — two branches of their own.
        var settings = await ado.GoToSettings();

        await settings.StoreAdoTokenAsync("not-a-real-token");
        await Assertions.Expect(settings.AdoTokenStored).ToContainTextAsync("aldrig");
        await Assertions.Expect(settings.ClearAdoToken).ToHaveTextAsync("Ryd token");
        await Snapshot();

        // A refused connection first, because that is the store's own red line — and the successful
        // reply below clears it, so it has to be measured before. Two lines, two owners: this one is
        // Azure DevOps refusing a call, the one at the end of the group is our server refusing a
        // setting.
        await settings.TestAdoConnection.ClickAsync();
        await Assertions.Expect(settings.AdoError).ToContainTextAsync("afviste tokenet");
        await Snapshot();

        connection = (200, $$"""{ "displayName": "{{Me}}" }""");

        await settings.TestAdoConnection.ClickAsync();
        await Assertions.Expect(settings.AdoConnection).ToHaveTextAsync($"Forbundet som {Me}");
        await Assertions.Expect(settings.AdoError).ToHaveCountAsync(0);
        await Snapshot();

        // The state list replaces the sentence that stands in for it, so the rows are a state the
        // screen has no other way into.
        await settings.LoadAdoStates.ClickAsync();
        await Assertions.Expect(settings.AdoStateRows).ToHaveCountAsync(3);
        await Assertions.Expect(settings.AdoStatesEmpty).ToHaveCountAsync(0);
        await Snapshot();

        // The group's own red line, and it needs no interception: emptying the work item type list is
        // refused by the real backend with ado.workItemTypesRequired. Removing the last one is a click
        // that leaves the row where it is, so it is pressed directly rather than through the helper
        // that waits for the row to go.
        //
        // This is the last thing done to the settings on purpose: every other saveAdo call, and storing
        // a token, clears adoError on its way out.
        await settings.RemoveWorkItemTypeAsync("Bug");
        await settings.RemoveWorkItemTypeAsync("User Story");

        await settings.WorkItemTypeRows.GetByTestId("remove-work-item-type").ClickAsync();
        await Assertions.Expect(settings.AdoSettingsError).ToContainTextAsync("mindst én sagstype");
        await Assertions.Expect(settings.WorkItemTypeRows).ToHaveCountAsync(1);
        await Snapshot();

        // Configured now, so the fifth screen has a Load button where the sentence used to be — and the
        // notice that the deadline on every row is the app's own invention, which no Jira screen has.
        ado = await settings.GoToAdo();

        await Assertions.Expect(ado.PreviewButton).ToHaveTextAsync("Hent sager");
        await Assertions.Expect(ado.NotConfigured).ToHaveCountAsync(0);
        await Assertions.Expect(ado.DeadlineNotice).ToContainTextAsync("foreslår");
        await Snapshot();

        // A refusal. The red line is the app's own failure path, reached through the button a user
        // presses rather than staged from the outside.
        await ado.PreviewAsync();
        await Assertions.Expect(ado.Error)
            .ToHaveTextAsync("Azure DevOps kunne ikke nås. Kontrollér samlingens URL og netværket.");
        await Snapshot();

        // An empty answer is an answer, and it has a sentence of its own that the refusal above must
        // not be mistaken for.
        preview = (200, NoWorkItems);

        await ado.PreviewAsync();
        await Assertions.Expect(ado.NoneAssigned).ToHaveTextAsync("Ingen sager er tildelt dig.");
        await Assertions.Expect(ado.Error).ToHaveCountAsync(0);
        await Snapshot();

        // Every row blocked: both reasons on their own row, both sentences under the count, and the
        // Import button in its disabled colours — which are a pair of their own. This answer is also
        // where the waiting pair and the no-deadline half get their only colour.
        preview = (200, BlockedWorkItems);

        await ado.PreviewAsync();
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(2);
        await Assertions.Expect(ado.Showing).ToHaveTextAsync("Viser 2 af 2 sager.");
        await Assertions.Expect(ado.NothingToSelect)
            .ToContainTextAsync("1 sag er udeladt af importen.");
        await Assertions.Expect(ado.NothingToSelect)
            .ToContainTextAsync("1 sag er importeret tidligere.");

        var waiting = ado.Row("Afventer svar");
        var seenBefore = ado.Row("Skriv testene");

        await Assertions.Expect(AdoImportScreen.TypeIn(waiting)).ToHaveTextAsync("Type: User Story");
        await Assertions.Expect(AdoImportScreen.NoDeadlineIn(waiting))
            .ToContainTextAsync("Uden deadline");
        await Assertions.Expect(AdoImportScreen.WaitingIn(waiting))
            .ToHaveTextAsync("Importeres som ventende.");

        // Waited for by the year rather than by the element: the line renders before the date is
        // formatted into it, and a timestamp put through the deadline formatter answers with an empty
        // string — which is a colour of nothing.
        await Assertions.Expect(AdoImportScreen.WaitingSinceIn(waiting)).ToContainTextAsync("2026");
        await Assertions.Expect(AdoImportScreen.ExcludedIn(waiting)).ToContainTextAsync("slået fra");
        await Assertions.Expect(AdoImportScreen.DeadlineIn(seenBefore)).ToContainTextAsync("2026");
        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(seenBefore))
            .ToHaveTextAsync("importeret tidligere");
        await Assertions.Expect(ado.ImportButton).ToBeDisabledAsync();
        await Snapshot();

        // One row that can be imported, which is what enables the button — a different colour from the
        // disabled one measured above — plus the note and requester lines the blocked rows leave out.
        preview = (200, OneImportableWorkItem);

        await ado.PreviewAsync();
        await Assertions.Expect(ado.ImportButton).ToHaveTextAsync("Importér 1 sag");
        await Assertions.Expect(ado.ImportButton).ToBeEnabledAsync();
        await Assertions.Expect(ado.NothingToSelect).ToHaveCountAsync(0);

        var importable = ado.Row("Ret rapporten");

        await Assertions.Expect(AdoImportScreen.RequesterIn(importable))
            .ToHaveTextAsync($"Opgavestiller: {Requester}");
        await Assertions.Expect(AdoImportScreen.NoteIn(importable))
            .ToHaveTextAsync("Beskrivelsen følger med.");

        // The button that opens the work item before anyone imports it. No branch guards it — the
        // contract makes <c>url</c> required — so this colour is measured without a fixture state of
        // its own. Waited for by its text all the same: the element is there before the localized label
        // is interpolated into it, and no text is invisible to the measurement.
        await Assertions.Expect(AdoImportScreen.OpenItemIn(importable))
            .ToHaveTextAsync(OpenIssueLabel);

        // And the line that says the open failed, which is a branch of its own: the paragraph renders
        // only while this row is the one that failed. Pressed rather than staged — the route above
        // aborts the call, and an aborted call is a failure to the client.
        await AdoImportScreen.OpenItemIn(importable).ClickAsync();
        await Assertions.Expect(AdoImportScreen.OpenErrorIn(importable)).ToHaveTextAsync(OpenFailed);
        await Snapshot();

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");
        await Snapshot();

        AssertNoFailures(failures, scheme);
    }

    /// <summary>
    /// An empty list is a screen the journey above cannot reach: tasks.empty renders only while
    /// nothing at all is shown.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task An_empty_task_list_meets_WCAG_AA(ColorScheme scheme)
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 800 }, scheme);

        await Assertions.Expect(App.Health).ToContainTextAsync("API:");
        await Assertions.Expect(App.Page.GetByText(EmptyList, new() { Exact = true }))
            .ToBeVisibleAsync();

        AssertNoFailures(await App.ContrastFailuresAsync(), scheme);
    }

    /// <summary>
    /// Side by side, which no other measurement in this class can see: every one of them opens the
    /// app at 480px, so the two-column layout was unmeasured colour for colour. It is a state of the
    /// task list rather than a sixth route — count screens and the answer is still five.
    /// <para>
    /// What is genuinely new here is not a palette but a set of ancestors: the panel has left its
    /// row and now sits in a column of its own, and an opaque background stops the guard's walk up
    /// the tree, so every piece of text inside it is measured against a different parent than
    /// before. The selected row's accent is deliberately not among the findings — it is a border,
    /// and the guard measures text; a border only has to reach 3:1 as a graphical object.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task The_side_by_side_layout_meets_WCAG_AA(ColorScheme scheme)
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(OverdueTitle).Overdue()
                .RequestedBy(Requester).Build(),
            new TaskItemBuilder(Clock).Titled(DueTodayTitle).DueToday()
                .WithNote(Note)
                .WithSubTask(DoneSubTask, isDone: true)
                .WithSubTask(OpenSubTask)
                .Build(),
            new TaskItemBuilder(Clock).Titled(WaitingTitle)
                .WaitingFor(WaitingOn, Clock.UtcNow.AddDays(-DaysWaited)).Build(),
            new TaskItemBuilder(Clock).Titled(SomedayTitle).Someday().Build(),
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build(),
            new TaskItemBuilder(Clock).Titled(InProgressTitle).InProgress().DueToday().Build(),
            new TaskItemBuilder(Clock).Titled(ConflictTitle).Overdue()
                .DeferredUntil(Clock.Today.AddDays(3)).Build());

        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 }, scheme);

        var failures = new List<string>();
        var tasks = App.Tasks;

        async Task Snapshot() => failures.AddRange(await App.ContrastFailuresAsync());

        await Assertions.Expect(App.Health).ToContainTextAsync("API:");
        await tasks.ShowCompleted.CheckAsync();
        await tasks.ShowSomeday.CheckAsync();
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);
        await Assertions.Expect(tasks.SomedayRows).ToContainTextAsync(SomedayTitle);

        // Nobody clicked: side by side the first task on screen is selected on arrival, and this is
        // the state the app opens in. Waited for by the panel's own text, because the elements are
        // in place before the localized strings are interpolated into them - and text that is not
        // there yet is invisible to the measurement.
        await Assertions.Expect(tasks.DetailColumn).ToBeVisibleAsync();
        await Assertions.Expect(tasks.Detail).ToContainTextAsync("Underopgaver");
        await Snapshot();

        // The note and the subtasks belong to another task than the auto-selected one, so without
        // this click the rendered markdown is never painted in the column.
        await tasks.RowShowing(DueTodayTitle).ClickAsync();
        await Assertions.Expect(tasks.SubTaskRows).ToHaveCountAsync(2);
        await Assertions.Expect(tasks.NoteRendered.Locator("h2")).ToHaveTextAsync("Dagsorden");
        await Assertions.Expect(tasks.NoteBullets).ToHaveTextAsync(["Lyd", "Lys"]);
        await Snapshot();

        // The waiting-on label and field only render for a task in that status, and the panel shows
        // one task at a time whatever the width.
        await tasks.RowShowing(WaitingTitle).ClickAsync();
        await Assertions.Expect(tasks.WaitingOnInput).ToHaveValueAsync(WaitingOn);
        await Snapshot();

        // The start-date hint, in the column rather than in the row it used to hang under.
        await tasks.RowShowing(ConflictTitle).ClickAsync();
        await Assertions.Expect(tasks.DeferUntilConflict).ToBeVisibleAsync();
        await Snapshot();

        AssertNoFailures(failures, scheme);
    }

    /// <summary>
    /// The prompt in the empty right-hand column, which auto-selection does not remove: a completed
    /// task's row has no panel behind it, so with only completed tasks in view there is nothing to
    /// select. Its own theory because the fixture has to hold nothing else — the layout above can
    /// never reach this state once anything selectable exists.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task The_side_by_side_prompt_meets_WCAG_AA(ColorScheme scheme)
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build());

        await OpenAppAsync(new() { Width = WideWidth, Height = 800 }, scheme);
        var tasks = App.Tasks;

        await Assertions.Expect(App.Health).ToContainTextAsync("API:");
        await tasks.ShowCompleted.CheckAsync();

        // The completed row is the teeth: without it the prompt would also show on an empty list,
        // which is a different state and would leave "completed is not selectable" unmeasured.
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);
        await Assertions.Expect(tasks.DetailEmpty).ToContainTextAsync("Vælg en opgave");

        AssertNoFailures(await App.ContrastFailuresAsync(), scheme);
    }

    /// <summary>
    /// The message carries every line, because this list is the work list: Assert.Empty prints
    /// only the first five, which is not enough to fix anything by.
    /// </summary>
    private static void AssertNoFailures(IEnumerable<string> found, ColorScheme scheme)
    {
        var distinct = found.Distinct().Order().ToList();

        Assert.True(distinct.Count == 0,
            $"{distinct.Count} contrast failures in {scheme}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, distinct));
    }
}
