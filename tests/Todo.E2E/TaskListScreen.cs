using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class TaskListScreen(TodoApp app)
{
    public ILocator NewTaskInput => Page.GetByTestId("new-task-input");

    public ILocator Rows => Page.GetByTestId("task-row");

    public ILocator Detail => Page.GetByTestId("task-detail");

    public ILocator DeadlineInput => Detail.Locator("input[type=date]");

    public ILocator CompleteToggle => Page.GetByTestId("complete-toggle");

    public ILocator ShowCompleted => Page.GetByTestId("show-completed");

    public ILocator CompletedRows => Page.GetByTestId("completed-section").GetByTestId("task-row");

    public ILocator SubTaskRows => Page.GetByTestId("subtask-row");

    public ILocator NewSubTaskInput => Page.GetByTestId("new-subtask-input");

    public ILocator SubTaskProgress => Page.GetByTestId("subtask-progress");

    private IPage Page => app.Page;

    public ILocator Section(string heading) => Page.GetByTestId("task-section").Filter(new()
    {
        Has = Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })
    });

    public ILocator RowsIn(string heading) => Section(heading).GetByTestId("task-row");

    public ILocator RowTitled(string title)
        => Rows.GetByRole(AriaRole.Button, new() { Name = title, Exact = true });

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    internal Task WaitUntilShownAsync() => Assertions.Expect(NewTaskInput).ToBeVisibleAsync();
}
