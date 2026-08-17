using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class TaskListScreen(TodoApp app)
{
    public ILocator NewTaskInput => Page.GetByTestId("new-task-input");

    public ILocator Rows => Page.GetByTestId("task-row");

    public ILocator Detail => Page.GetByTestId("task-detail");

    // Efter startdatoen er der to date-felter i panelet, så input[type=date] rammer dem begge.
    public ILocator DeadlineInput => Detail.GetByTestId("deadline-input");

    public ILocator CompleteToggle => Page.GetByTestId("complete-toggle");

    public ILocator ShowCompleted => Page.GetByTestId("show-completed");

    public ILocator ShowSomeday => Page.GetByTestId("show-someday");

    public ILocator StatusSelect => Detail.Locator("select");

    public ILocator WaitingOnInput => Detail.GetByTestId("waiting-on-input");

    public ILocator CompletedRows => Page.GetByTestId("completed-section").GetByTestId("task-row");

    public ILocator WaitingSection => Page.GetByTestId("waiting-section");

    public ILocator SomedaySection => Page.GetByTestId("someday-section");

    public ILocator WaitingRows => WaitingSection.GetByTestId("task-row");

    public ILocator SomedayRows => SomedaySection.GetByTestId("task-row");

    public ILocator SubTaskRows => Page.GetByTestId("subtask-row");

    public ILocator NewSubTaskInput => Page.GetByTestId("new-subtask-input");

    public ILocator SubTaskProgress => Page.GetByTestId("subtask-progress");

    public ILocator NoteRendered => Detail.GetByTestId("note-rendered");

    public ILocator NoteEditor => Detail.GetByTestId("note-editor");

    public ILocator NoteEditButton => Detail.GetByTestId("note-edit");

    /// <summary>Says that opening a note's link failed, which only a failed request puts here.</summary>
    public ILocator NoteLinkError => Detail.GetByTestId("note-link-error");

    public ILocator NoteBullets => NoteRendered.Locator("ul > li");

    public ILocator NoteLink => NoteRendered.Locator("a");

    public ILocator NoteTable => NoteRendered.Locator("table");

    private IPage Page => app.Page;

    public ILocator Section(string heading) => Page.GetByTestId("task-section").Filter(new()
    {
        Has = Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })
    });

    public ILocator RowsIn(string heading) => Section(heading).GetByTestId("task-row");

    public ILocator RowTitled(string title)
        => Rows.GetByRole(AriaRole.Button, new() { Name = title, Exact = true });

    /// <summary>
    /// The button of a row that says more than its title: a deadline or an opgavestiller joins
    /// the accessible name that <see cref="RowTitled"/> matches in full.
    /// </summary>
    public ILocator RowShowing(string title)
        => Rows.GetByRole(AriaRole.Button, new() { Name = title });

    /// <summary>The row itself, which holds the lines that sit outside its button.</summary>
    public ILocator RowFor(string title) => Rows.Filter(new()
    {
        Has = Page.GetByRole(AriaRole.Button, new() { Name = title })
    });

    public ILocator WaitingDaysFor(string title) => RowFor(title).GetByTestId("waiting-days");

    /// <summary>
    /// The detail under one named row. Only one row is expanded at a time, so waiting for this
    /// rather than <see cref="Detail"/> is what tells a click apart from the detail it replaced.
    /// </summary>
    public ILocator DetailFor(string title) => RowFor(title).GetByTestId("task-detail");

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(NewTaskInput).ToBeVisibleAsync();
}
