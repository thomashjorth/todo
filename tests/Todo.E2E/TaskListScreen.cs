using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class TaskListScreen(TodoApp app)
{
    public ILocator NewTaskInput => Page.GetByTestId("new-task-input");

    public ILocator Rows => Page.GetByTestId("task-row");

    /// <summary>The box that narrows the list to the tasks whose title or note holds the term.</summary>
    public ILocator Search => Page.GetByTestId("task-search");

    /// <summary>
    /// The line a search that found nothing leaves behind. A different element from the empty-list
    /// message on purpose: "no tasks" in front of a search would read as if the list had been lost.
    /// </summary>
    public ILocator NoMatches => Page.GetByTestId("no-matches");

    /// <summary>
    /// The detail panel, wherever it is: inside its row in one column, in the right-hand column
    /// side by side. Exactly one exists either way — the template renders it behind an
    /// <c>@if</c> on the breakpoint rather than hiding one copy with CSS — so this locator needs
    /// to know nothing about the layout, and neither does anything built on it.
    /// </summary>
    public ILocator Detail => Page.GetByTestId("task-detail");

    /// <summary>The right-hand column, which only exists above the <c>xl</c> breakpoint.</summary>
    public ILocator DetailColumn => Page.GetByTestId("detail-column");

    /// <summary>
    /// The column the list itself is in. It exists at every width — only its scrolling is behind
    /// the breakpoint — so a journey that measures the scroll has to be the one that sets a wide
    /// viewport; finding this element proves nothing about the layout on its own.
    /// </summary>
    public ILocator TaskColumn => Page.GetByTestId("task-column");

    /// <summary>
    /// The prompt the right-hand column shows when nothing is selectable. Reachable even with
    /// auto-selection on, because a completed task's row has no panel behind it: with only
    /// completed tasks in view there is nothing to select.
    /// </summary>
    public ILocator DetailEmpty => Page.GetByTestId("detail-empty");

    public ILocator TitleInput => Detail.GetByTestId("title-input");

    // Efter startdatoen er der to date-felter i panelet, så input[type=date] rammer dem begge.
    public ILocator DeadlineInput => Detail.GetByTestId("deadline-input");

    public ILocator DeferUntilInput => Detail.GetByTestId("defer-until-input");

    /// <summary>
    /// The line that says a start date after the deadline changes nothing. It only exists in the
    /// expanded panel, so nothing measures its colours until a test opens the row.
    /// </summary>
    public ILocator DeferUntilConflict => Detail.GetByTestId("defer-until-conflict");

    public ILocator CompleteToggle => Page.GetByTestId("complete-toggle");

    public ILocator ShowCompleted => Page.GetByTestId("show-completed");

    public ILocator ShowSomeday => Page.GetByTestId("show-someday");

    public ILocator StatusSelect => Detail.Locator("select");

    public ILocator WaitingOnInput => Detail.GetByTestId("waiting-on-input");

    /// <summary>
    /// The suggestions behind the who field, in the one shared &lt;datalist&gt;. The popup itself is
    /// browser chrome and cannot be driven from Playwright, but these options are DOM — so this is
    /// the assertion that proves the names reached the screen at all. Never visible: a
    /// &lt;datalist&gt; is not rendered, so count and attribute are the only honest questions.
    /// </summary>
    public ILocator DelegateOptions => Page.Locator("datalist#delegate-names option");

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
    /// The digit a row's own button claims, if it claims one. On the attribute rather than on its
    /// value, so a count of zero is an honest question: from the tenth row the directive registers
    /// nothing and leaves no <c>aria-keyshortcuts</c> behind at all — not an empty one.
    /// <para>
    /// The row's title button, not any button in the row: an open panel puts two more shortcut
    /// buttons inside the same &lt;li&gt;, so a journey that counts these has to leave the row shut.
    /// </para>
    /// </summary>
    public ILocator RowShortcutFor(string title)
        => RowFor(title).GetByRole(AriaRole.Button, new() { Name = title })
            .And(RowFor(title).Locator("[aria-keyshortcuts]"));

    /// <summary>
    /// The detail under one named row. Only one row is expanded at a time, so waiting for this
    /// rather than <see cref="Detail"/> is what tells a click apart from the detail it replaced.
    /// <para>
    /// One column only, and that is the whole point of it: side by side the panel is not a
    /// descendant of any row, so this never matches. A journey above the <c>xl</c> breakpoint wants
    /// <see cref="Detail"/> or <see cref="DetailColumn"/> — and a wide test that reaches for this
    /// one fails with "element not found", which reads like a missing panel rather than a locator
    /// asking the wrong question.
    /// </para>
    /// </summary>
    public ILocator DetailFor(string title) => RowFor(title).GetByTestId("task-detail");

    /// <summary>
    /// The button that asks the shell to open the issue in Jira. Only a task whose source is Jira
    /// has one, and it sits outside the row's button so its label stays out of the accessible name
    /// <see cref="RowTitled"/> matches in full.
    /// </summary>
    public ILocator ExternalLinkIn(string title) => RowFor(title).GetByTestId("external-link");

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<JiraImportScreen> GoToJira() => app.GoToJira();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(NewTaskInput).ToBeVisibleAsync();
}
