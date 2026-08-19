using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class RetroImportScreen(TodoApp app)
{
    public ILocator Csv => Page.GetByTestId("retro-csv");

    public ILocator AnalyseButton => Page.GetByTestId("retro-analyse");

    public ILocator Rows => Page.GetByTestId("retro-row");

    public ILocator AlreadyImported => Page.GetByTestId("retro-already-imported");

    public ILocator Skipped => Page.GetByTestId("retro-skipped");

    public ILocator NoneMine => Page.GetByTestId("retro-none-mine");

    public ILocator Error => Page.GetByTestId("retro-error");

    public ILocator ImportButton => Page.GetByTestId("retro-import");

    public ILocator Receipt => Page.GetByTestId("retro-receipt");

    private IPage Page => app.Page;

    public ILocator Row(string text) => Rows.Filter(new() { HasText = text });

    public static ILocator PickOf(ILocator row) => row.Locator("input[type=checkbox]");

    public Task PasteAsync(string csv) => Csv.FillAsync(csv);

    public Task AnalyseAsync() => AnalyseButton.ClickAsync();

    public Task ImportAsync() => ImportButton.ClickAsync();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    public Task<JiraImportScreen> GoToJira() => app.GoToJira();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Csv).ToBeVisibleAsync();
}
