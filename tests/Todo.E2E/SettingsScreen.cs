using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class SettingsScreen(TodoApp app)
{
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    public ILocator Language => Page.GetByTestId("language-select");

    public ILocator AliasRows => Page.GetByTestId("alias-row");

    public ILocator Error => Page.GetByTestId("alias-error");

    public ILocator JiraBaseUrl => Page.GetByTestId("jira-base-url");

    public ILocator JiraToken => Page.GetByTestId("jira-token");

    public ILocator SaveJiraToken => Page.GetByTestId("jira-save-token");

    /// <summary>Says a token is held. Only a stored token puts it or the Clear button on screen.</summary>
    public ILocator JiraTokenStored => Page.GetByTestId("jira-token-stored");

    public ILocator ClearJiraToken => Page.GetByTestId("jira-clear-token");

    public ILocator TestJiraConnection => Page.GetByTestId("jira-test");

    /// <summary>The name Jira reports for the token's owner, which only a reply puts here.</summary>
    public ILocator JiraConnection => Page.GetByTestId("jira-connection");

    public ILocator LoadJiraStatuses => Page.GetByTestId("jira-load-statuses");

    public ILocator JiraStatusRows => Page.GetByTestId("jira-status-row");

    public ILocator JiraStatusesEmpty => Page.GetByTestId("jira-statuses-empty");

    /// <summary>
    /// The duty switch. Ticking it is what makes the pool's issues actionable — the server re-reads
    /// it on every preview and every import, so this checkbox is the whole decision.
    /// </summary>
    public ILocator OnDuty => Page.GetByTestId("jira-on-duty");

    public ILocator JiraError => Page.GetByTestId("jira-error");

    private ILocator AliasInput => Page.GetByTestId("alias-input");

    private IPage Page => app.Page;

    /// <summary>Chooses "system", "da" or "en" — the values the API stores, not a browser locale.</summary>
    public Task ChooseLanguageAsync(string value) => Language.SelectOptionAsync(value);

    public async Task AddAliasAsync(string name)
    {
        await SubmitAliasAsync(name);

        await Assertions.Expect(AliasRows.Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Types a name and submits it without waiting for a row: the API can reject one, and the
    /// rejection is what a caller is sometimes after.
    /// </summary>
    public async Task SubmitAliasAsync(string name)
    {
        await AliasInput.FillAsync(name);
        await AliasInput.PressAsync("Enter");
    }

    /// <summary>
    /// Types a token and saves it, then waits for the line that says one is held: the field is
    /// write-only, so the confirmation is the only evidence the round trip happened.
    /// </summary>
    public async Task StoreJiraTokenAsync(string token)
    {
        await JiraToken.FillAsync(token);
        await SaveJiraToken.ClickAsync();

        await Assertions.Expect(JiraTokenStored).ToBeVisibleAsync();
    }

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<JiraImportScreen> GoToJira() => app.GoToJira();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Language).ToBeVisibleAsync();
}
