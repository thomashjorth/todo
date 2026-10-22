using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Ai;
using Todo.Core.Settings;
using Todo.TestSupport.Ai;
using Todo.TestSupport.Builders;

namespace Todo.E2E;

/// <summary>
/// The second round of AI features against a fake Ollama, the whole chain real wherever the app
/// owns it (ADR 21). The fake's vectors count shared words (<see cref="FakeOllama.VectorOf"/>), so
/// "close in meaning" here means "sharing words": these journeys measure the app around the model.
/// The one interception is the Jira preview, which would otherwise need a fake Jira for a journey
/// about the warning beside its rows.
/// </summary>
public class AiScreensJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    private const string RetroCsv = """
        "Content","Author","Zone","Action Owner"
        "We should get better at testing","Thomas","Went badly","Thomas"
        """;

    private FakeOllama _ollama = null!;

    public override async ValueTask InitializeAsync()
    {
        _ollama = await FakeOllama.StartAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _ollama.DisposeAsync();
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Configure<AiOptions>(o =>
        {
            o.BaseUrl = _ollama.BaseUrl;
            o.EmbeddingModel = FakeOllama.EmbeddingModel;
        });

    /// <summary>
    /// Alt+H asks, the suggestion stands above the original, and the task arrives with the suggested
    /// title. The re-analysis after the import then calls the row imported, which is the proof that
    /// the key stayed the original's.
    /// </summary>
    [Fact]
    public async Task A_retro_note_is_imported_as_the_suggested_action_under_its_own_key()
    {
        await Host.AddAndSaveChangesAsync(new UserAlias { Value = "Thomas" });
        _ollama.Answer = """{"actions":[{"note":1,"title":"Write integration tests","owner":null}]}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var retro = await App.GoToImport();

        await retro.PasteAsync(RetroCsv);
        await retro.AnalyseAsync();
        await Assertions.Expect(App.Page.GetByTestId("retro-suggest")).ToBeVisibleAsync();
        await App.Page.Keyboard.PressAsync("Alt+h");

        await Assertions.Expect(App.Page.GetByTestId("retro-suggested-title")).ToHaveTextAsync("Write integration tests");
        await Assertions.Expect(App.Page.GetByTestId("retro-original"))
            .ToHaveTextAsync("Original: We should get better at testing");

        await retro.ImportAsync();
        await Assertions.Expect(retro.AlreadyImported).ToHaveCountAsync(1);

        var tasks = await App.GoToTasks();
        await Assertions.Expect(tasks.RowShowing("Write integration tests")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The words typed are not in the task, so only the meaning can find it - and typing again is a
    /// new search, which drops what the meaning added.
    /// </summary>
    [Fact]
    public async Task Search_by_meaning_adds_a_task_the_words_alone_do_not_find()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Certifikat forny").Build(),
            new TaskItemBuilder().Titled("Køb mælk").Build());
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;
        await Assertions.Expect(App.Page.GetByTestId("search-by-meaning")).ToBeVisibleAsync();

        await tasks.Search.FillAsync("forny certifikat nu");
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(0);

        await App.Page.Keyboard.PressAsync("Alt+b");

        await Assertions.Expect(tasks.RowTitled("Certifikat forny")).ToBeVisibleAsync();
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(App.Page.GetByTestId("search-meaning-note")).ToBeVisibleAsync();

        await tasks.Search.FillAsync("forny certifikat nu!");
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_jira_row_resembling_a_task_of_your_own_says_so()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Ret login timeout for mange roller").Build(),
            new Setting { Key = SettingKeys.JiraBaseUrl, Value = "https://jira.test" },
            new Setting { Key = SettingKeys.JiraProjectKey, Value = "SAAS" },
            new Setting { Key = SettingKeys.JiraToken, Value = "not-a-real-token" });
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await App.Page.RouteAsync("**/api/jira/preview", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {
                  "rows": [
                    { "key": "SAAS-2", "title": "Login timeout for mange roller", "url": "https://jira.test/browse/SAAS-2",
                      "status": "I gang", "isWaiting": false, "isDuty": false, "alreadyImported": false,
                      "reconciled": false, "suggestsClosing": false, "suggestsReopening": false },
                    { "key": "SAAS-3", "title": "Køb mælk til kontoret", "url": "https://jira.test/browse/SAAS-3",
                      "status": "I gang", "isWaiting": false, "isDuty": false, "alreadyImported": false,
                      "reconciled": false, "suggestsClosing": false, "suggestsReopening": false }
                  ],
                  "total": 2
                }
                """,
        }));
        var jira = await App.GoToJira();

        await jira.PreviewAsync();

        await Assertions.Expect(jira.Row("SAAS-2").GetByTestId("jira-resembles"))
            .ToHaveTextAsync("Ligner: Ret login timeout for mange roller");
        await Assertions.Expect(App.Page.GetByTestId("jira-resembles")).ToHaveCountAsync(1);
    }

    /// <summary>The facts reach the model from the database, so a seeded waiting task is in the prompt.</summary>
    [Fact]
    public async Task The_week_s_overview_is_written_from_the_list()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Svar revisoren").WaitingFor("Revisoren", DateTime.UtcNow.AddDays(-9)).Build());
        _ollama.Answer = """{"points":["Svar revisoren har ventet 9 dage og bør rykkes."]}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await App.GoToReview();
        await Assertions.Expect(App.Page.GetByTestId("review-overview")).ToBeVisibleAsync();

        await App.Page.Keyboard.PressAsync("Alt+u");

        await Assertions.Expect(App.Page.GetByTestId("review-overview-point"))
            .ToHaveTextAsync(["Svar revisoren har ventet 9 dage og bør rykkes."]);
        var prompt = (string)Assert.Single(_ollama.ChatRequests)["messages"]![0]!["content"]!;
        Assert.Contains("Svar revisoren - waiting on Revisoren for 9 days", prompt);
    }

    /// <summary>Each new letter on the screens it lives on, against everything else rendered there.</summary>
    [Theory]
    [InlineData("tasks", "Alt+B")]
    [InlineData("review", "Alt+U")]
    public async Task Every_shortcut_on_an_ai_screen_is_its_own(string screen, string expected)
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await Assertions.Expect(App.Page.GetByTestId("quick-entry-parse")).ToBeVisibleAsync();

        if (screen == "review")
        {
            await App.GoToReview();
            await Assertions.Expect(App.Page.GetByTestId("review-overview")).ToBeVisibleAsync();
        }

        var shortcuts = await App.Page.EvaluateAsync<string[]>(
            "[...document.querySelectorAll('[aria-keyshortcuts]')].map(e => e.getAttribute('aria-keyshortcuts'))");

        Assert.Contains(expected, shortcuts);
        var twice = shortcuts.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twice.Count == 0, $"Claimed twice on {screen}: {string.Join(", ", twice)}");
    }
}
