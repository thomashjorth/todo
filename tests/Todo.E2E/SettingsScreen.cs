using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class SettingsScreen(TodoApp app)
{
    public ILocator Language => Page.GetByTestId("language-select");

    public ILocator AliasRows => Page.GetByTestId("alias-row");

    public ILocator Error => Page.GetByTestId("alias-error");

    private ILocator AliasInput => Page.GetByTestId("alias-input");

    private IPage Page => app.Page;

    /// <summary>Chooses "system", "da" or "en" — the values the API stores, not a browser locale.</summary>
    public Task ChooseLanguageAsync(string value) => Language.SelectOptionAsync(value);

    public async Task AddAliasAsync(string name)
    {
        await AliasInput.FillAsync(name);
        await AliasInput.PressAsync("Enter");

        await Assertions.Expect(AliasRows.Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

    public Task<RetroImportScreen> GoToImport() => app.GoToImport();

    public Task<TaskListScreen> GoToTasks() => app.GoToTasks();

    internal Task WaitUntilShownAsync() => Assertions.Expect(Language).ToBeVisibleAsync();
}
