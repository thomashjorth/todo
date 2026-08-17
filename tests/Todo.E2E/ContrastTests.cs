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
    private const int DaysWaited = 12;
    private const string OverdueTitle = "Betal regningen";
    private const string DueTodayTitle = "Send referatet";
    private const string WaitingTitle = "Svar revisoren";
    private const string CompletedTitle = "Ryd skrivebordet";
    private const string SomedayTitle = "Læs om typografi";
    private const string Requester = "Mette Kirkegaard";
    private const string WaitingOn = "Mette";
    private const string DoneSubTask = "Find det gamle referat";
    private const string OpenSubTask = "Skriv udkastet";
    private const string EmptyList = "Ingen opgaver.";
    private const string Me = "Thomas Hjorth Hansen";
    private const string MyAction = "Skriv referatet fra retroen";

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
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build());

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
