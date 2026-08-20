using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class SettingsScreen(TodoApp app)
{
    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Level = 2 });

    public ILocator Language => Page.GetByTestId("language-select");

    public ILocator AliasRows => Page.GetByTestId("alias-row");

    public ILocator Error => Page.GetByTestId("alias-error");

    /// <summary>
    /// The people tasks are handed to. Only a stored list puts a row here, and the list comes from
    /// the real backend: nothing in this suite stubs <c>/api/settings</c>, so a row exists because a
    /// name was saved through the page rather than because an answer was staged.
    /// </summary>
    public ILocator DelegateRows => Page.GetByTestId("delegate-row");

    /// <summary>
    /// The sentence that stands in for the list while it is empty — the default state, and the other
    /// half of the branch <see cref="DelegateRows"/> measures.
    /// </summary>
    public ILocator DelegatesEmpty => Page.GetByTestId("delegates-empty");

    public ILocator DelegatesError => Page.GetByTestId("delegates-error");

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

    public ILocator AdoBaseUrl => Page.GetByTestId("ado-base-url");

    public ILocator AdoProject => Page.GetByTestId("ado-project");

    public ILocator AdoToken => Page.GetByTestId("ado-token");

    public ILocator SaveAdoToken => Page.GetByTestId("ado-save-token");

    /// <summary>Says a token is held. Only a stored token puts it or the Clear button on screen.</summary>
    public ILocator AdoTokenStored => Page.GetByTestId("ado-token-stored");

    public ILocator ClearAdoToken => Page.GetByTestId("ado-clear-token");

    public ILocator TestAdoConnection => Page.GetByTestId("ado-test");

    /// <summary>The name Azure DevOps reports for the token's owner, which only a reply puts here.</summary>
    public ILocator AdoConnection => Page.GetByTestId("ado-connection");

    public ILocator LoadAdoStates => Page.GetByTestId("ado-load-states");

    public ILocator AdoStateRows => Page.GetByTestId("ado-state-row");

    public ILocator AdoStatesEmpty => Page.GetByTestId("ado-states-empty");

    public ILocator AdoIncludeWaiting => Page.GetByTestId("ado-include-waiting");

    /// <summary>
    /// The work item types the import is filtered to. Never empty as it stands: an absent row reads
    /// as the three defaults, and emptying the list is refused rather than folded back.
    /// </summary>
    public ILocator WorkItemTypeRows => Page.GetByTestId("ado-work-item-type-row");

    public ILocator AdoDeadlineDays => Page.GetByTestId("ado-deadline-days");

    /// <summary>
    /// The group's own red line, written by a rejected save. Separate from <see cref="AdoError"/> on
    /// purpose: this one is the app's own server refusing a setting, that one is Azure DevOps refusing
    /// a call.
    /// </summary>
    public ILocator AdoSettingsError => Page.GetByTestId("ado-settings-error");

    public ILocator AdoError => Page.GetByTestId("ado-error");

    private ILocator AliasInput => Page.GetByTestId("alias-input");

    private ILocator DelegateInput => Page.GetByTestId("delegate-input");

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
    /// Types a name onto the delegate list and waits for its row: the round trip is a real PUT
    /// against the real backend, so the row is the only evidence the name was stored.
    /// </summary>
    public async Task AddDelegateAsync(string name)
    {
        await SubmitDelegateAsync(name);

        await Assertions.Expect(DelegateRows.Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Types a name and submits it without waiting for a row: the API rejects a duplicate, and the
    /// rejection is what a caller is sometimes after.
    /// </summary>
    public async Task SubmitDelegateAsync(string name)
    {
        await DelegateInput.FillAsync(name);
        await DelegateInput.PressAsync("Enter");
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

    /// <summary>
    /// Types a token and saves it, then waits for the line that says one is held: the field is
    /// write-only, so the confirmation is the only evidence the round trip happened.
    /// </summary>
    public async Task StoreAdoTokenAsync(string token)
    {
        await AdoToken.FillAsync(token);
        await SaveAdoToken.ClickAsync();

        await Assertions.Expect(AdoTokenStored).ToBeVisibleAsync();
    }

    /// <summary>
    /// Takes one work item type off the list and waits for the row to go. Removing the <em>last</em>
    /// one is refused by the server, so a caller after that refusal must not use this — it would wait
    /// for a row that is still there.
    /// </summary>
    public async Task RemoveWorkItemTypeAsync(string type)
    {
        var row = WorkItemTypeRows.Filter(new() { HasText = type });

        await row.GetByTestId("remove-work-item-type").ClickAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0);
    }

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<JiraImportScreen> GoToJira() => app.GoToJira();

    public Task<AdoImportScreen> GoToAdo() => app.GoToAdo();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Language).ToBeVisibleAsync();
}
