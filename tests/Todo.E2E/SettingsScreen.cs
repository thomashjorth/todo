using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// The settings page, whose five groups fold. Only one is open at a time and none is open on
/// arrival, so every locator below the headings finds nothing until its group has been unfolded —
/// the panel is removed from the DOM, not hidden in it.
/// <para>
/// The methods that act on a group unfold it themselves through <see cref="OpenAsync"/>, which is
/// idempotent, so a caller that only uses those needs to know nothing about the fold. A caller that
/// reaches for a locator directly has to open its group first.
/// </para>
/// </summary>
public sealed class SettingsScreen(TodoApp app)
{
    /// <summary>The five groups, spelled the way each heading button's test id is built.</summary>
    public const string LanguageSection = "language";

    public const string DelegateSection = "delegate";

    public const string JiraSection = "jira";

    public const string AdoSection = "ado";

    public const string RetroSection = "retro";

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

    /// <summary>
    /// The group's own red line, written by a rejected save of a Jira setting or token. Separate
    /// from <see cref="JiraError"/> on purpose, the same way the two ADO lines are: this one is the
    /// app's own server refusing a setting, that one is Jira refusing a call.
    /// </summary>
    public ILocator JiraSettingsError => Page.GetByTestId("jira-settings-error");

    /// <summary>The switch that registers the app to start when the user signs in.</summary>
    public ILocator Autostart => Page.GetByTestId("autostart");

    /// <summary>
    /// The general group's own error line, apart from <see cref="SettingsError"/> for the same
    /// reason each source group has one: since the accordion, a message written to another group's
    /// line can be inside a folded section, so the user sees nothing at all.
    /// </summary>
    public ILocator AutostartError => Page.GetByTestId("autostart-error");

    /// <summary>The line the language select writes to, which autostart must not use.</summary>
    public ILocator SettingsError => Page.GetByTestId("settings-error");

    private ILocator AliasInput => Page.GetByTestId("alias-input");

    private ILocator DelegateInput => Page.GetByTestId("delegate-input");

    private IPage Page => app.Page;

    /// <summary>The heading button of one group, which is what folds and unfolds it.</summary>
    public ILocator SectionToggle(string section) => Page.GetByTestId($"{section}-section-toggle");

    /// <summary>
    /// The panel of one group. It exists only while the group is open, so a caller after "the group
    /// is folded" asserts a count of zero on this rather than on visibility.
    /// </summary>
    public ILocator SectionPanel(string section) =>
        Page.Locator($"#{section}-section-panel[role='region']");

    /// <summary>
    /// Unfolds one group, and does nothing if it is already open. Idempotent on purpose: the methods
    /// below call it, so a caller that opens a group and then uses one of them does not close it
    /// again. Waits for the heading to say it is expanded rather than for a field, because which
    /// field a group holds is the caller's business.
    /// </summary>
    public async Task OpenAsync(string section)
    {
        var toggle = SectionToggle(section);

        if (await toggle.GetAttributeAsync("aria-expanded") != "true")
        {
            await toggle.ClickAsync();
        }

        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(SectionPanel(section)).ToBeVisibleAsync();
    }

    /// <summary>Chooses "system", "da" or "en" — the values the API stores, not a browser locale.</summary>
    public async Task ChooseLanguageAsync(string value)
    {
        await OpenAsync(LanguageSection);

        await Language.SelectOptionAsync(value);
    }

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
        await OpenAsync(RetroSection);

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
        await OpenAsync(DelegateSection);

        await DelegateInput.FillAsync(name);
        await DelegateInput.PressAsync("Enter");
    }

    /// <summary>
    /// Types a token and saves it, then waits for the line that says one is held: the field is
    /// write-only, so the confirmation is the only evidence the round trip happened.
    /// </summary>
    public async Task StoreJiraTokenAsync(string token)
    {
        await OpenAsync(JiraSection);

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
        await OpenAsync(AdoSection);

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
        await OpenAsync(AdoSection);

        var row = WorkItemTypeRows.Filter(new() { HasText = type });

        await row.GetByTestId("remove-work-item-type").ClickAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0);
    }

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<JiraImportScreen> GoToJira() => app.GoToJira();

    public Task<AdoImportScreen> GoToAdo() => app.GoToAdo();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    /// <summary>
    /// Arrival is the language heading, not the language select: nothing is unfolded on arrival, so
    /// the select is not in the DOM yet. A locator that is absent in the state it is meant to prove
    /// would make every navigation here pass on any page at all.
    /// </summary>
    internal Task WaitUntilShownAsync() =>
        Assertions.Expect(SectionToggle(LanguageSection)).ToBeVisibleAsync();
}
