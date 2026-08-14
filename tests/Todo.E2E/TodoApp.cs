using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

/// <summary>
/// Owns the page and the routes. Navigation waits for the destination before it hands a test the
/// screen, so no test can look for an element that has not rendered yet.
/// </summary>
public sealed class TodoApp
{
    private TodoApp(IPage page) => Page = page;

    public IPage Page { get; }

    public ILocator Heading => Page.GetByRole(AriaRole.Heading, new() { Name = "Mandalorian ToDo", Exact = true });

    public ILocator Health => Page.GetByTestId("health");

    public TaskListScreen Tasks => new(this);

    public static async Task<TodoApp> OpenAsync(
        IBrowser browser, RunningHost host, ViewportSize? viewport = null)
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        var page = await browser.NewPageAsync(new() { ViewportSize = viewport });
        await page.GotoAsync(host.BaseUrl);

        var app = new TodoApp(page);
        await app.Tasks.WaitUntilShownAsync();

        return app;
    }

    public async Task<TaskListScreen> GoToTasks()
    {
        await Page.GetByTestId("nav-tasks").ClickAsync();

        var screen = new TaskListScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<RetroImportScreen> GoToImport()
    {
        await Page.GetByTestId("nav-import").ClickAsync();

        var screen = new RetroImportScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public async Task<SettingsScreen> GoToSettings()
    {
        await Page.GetByTestId("nav-settings").ClickAsync();

        var screen = new SettingsScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    /// <summary>
    /// Reloads and hands back the task list, so a test can tell what the app saved from what it
    /// only holds in memory.
    /// </summary>
    public async Task<TaskListScreen> ReloadAsync()
    {
        await Page.ReloadAsync();

        var screen = new TaskListScreen(this);
        await screen.WaitUntilShownAsync();

        return screen;
    }

    public Task<int> ScrollWidthAsync()
        => Page.EvaluateAsync<int>("document.documentElement.scrollWidth");

    /// <summary>
    /// The width the page has to lay out in, which a vertical scrollbar makes narrower than
    /// the viewport.
    /// </summary>
    public Task<int> ClientWidthAsync()
        => Page.EvaluateAsync<int>("document.documentElement.clientWidth");
}
