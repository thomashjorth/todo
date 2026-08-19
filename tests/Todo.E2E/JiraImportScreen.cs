using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class JiraImportScreen(TodoApp app)
{
    /// <summary>
    /// The screen's own heading. Level 2 rather than its text, because the text is localized and
    /// the h1 above it belongs to the shell — the level is what makes this one unambiguous.
    /// </summary>
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    /// <summary>Says there is nothing to fetch yet, which is the state before a token is stored.</summary>
    public ILocator NotConfigured => Page.GetByTestId("jira-not-configured");

    public ILocator SettingsLink => Page.GetByTestId("jira-settings-link");

    public ILocator PreviewButton => Page.GetByTestId("jira-preview");

    public ILocator Rows => Page.GetByTestId("jira-row");

    public ILocator Showing => Page.GetByTestId("jira-showing");

    /// <summary>Why every row on screen is untickable, which is not the same as an empty answer.</summary>
    public ILocator NothingToSelect => Page.GetByTestId("jira-nothing-to-select");

    public ILocator NoneAssigned => Page.GetByTestId("jira-none-assigned");

    public ILocator Error => Page.GetByTestId("jira-import-error");

    public ILocator ImportButton => Page.GetByTestId("jira-import");

    public ILocator Receipt => Page.GetByTestId("jira-receipt");

    private IPage Page => app.Page;

    public ILocator Row(string text) => Rows.Filter(new() { HasText = text });

    /// <summary>
    /// Why a single row is being skipped. Scoped to the row rather than the page: the two reasons
    /// carry the same class and only differ in which row they sit on.
    /// </summary>
    public static ILocator ExcludedIn(ILocator row) => row.GetByTestId("jira-excluded");

    public static ILocator AlreadyImportedIn(ILocator row)
        => row.GetByTestId("jira-already-imported");

    public static ILocator PickOf(ILocator row) => row.Locator("input[type=checkbox]");

    public Task PreviewAsync() => PreviewButton.ClickAsync();

    public Task ImportAsync() => ImportButton.ClickAsync();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    public Task<SettingsScreen> GoToSettings() => app.GoToSettings();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Heading).ToBeVisibleAsync();
}
