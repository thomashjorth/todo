using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Ai;
using Todo.Core.Settings;
using Todo.TestSupport.Ai;
using Todo.TestSupport.Builders;

namespace Todo.E2E;

/// <summary>
/// The three AI features against a fake Ollama, the whole chain real (ADR 21): the button, the
/// store, the endpoint, the prompt, the client and the clean-up on the way back, and then the
/// ordinary task routes that accepting goes through. The fake answers what each test tells it to,
/// so these measure the app around the model - whether the model answers well is the eval runner's.
/// </summary>
public class AiJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

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

    // After the host bound Ai:BaseUrl to RunningHost's closed port, so this is the last word.
    protected override void ConfigureServices(IServiceCollection services)
        => services.Configure<AiOptions>(o => o.BaseUrl = _ollama.BaseUrl);

    [Fact]
    public async Task A_typed_line_becomes_a_task_with_the_requester_and_deadline_the_model_read()
    {
        var friday = DateOnly.FromDateTime(DateTime.Now).AddDays(2);
        _ollama.Answer = $$"""{"title":"Ring til Anna om fakturaen","requester":"Anna","deadline":"{{friday:yyyy-MM-dd}}"}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;
        var parse = App.Page.GetByTestId("quick-entry-parse");

        await tasks.NewTaskInput.FillAsync("Ring til Anna om fakturaen på fredag");
        await parse.ClickAsync();

        var suggestion = App.Page.GetByTestId("quick-entry-suggestion");
        await Assertions.Expect(suggestion).ToContainTextAsync("Ring til Anna om fakturaen");
        await Assertions.Expect(suggestion).ToContainTextAsync("Opgavestiller: Anna");
        await Assertions.Expect(suggestion).ToContainTextAsync($"Deadline: {Deadlines.InDanish(friday)}");

        // Nothing is saved until the click.
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(0);

        await App.Page.GetByTestId("quick-entry-accept").ClickAsync();

        var row = tasks.RowShowing("Ring til Anna om fakturaen");
        await Assertions.Expect(row).ToContainTextAsync("Opgavestiller: Anna");
        await Assertions.Expect(row).ToContainTextAsync($"Deadline: {Deadlines.InDanish(friday)}");
        await Assertions.Expect(tasks.NewTaskInput).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(suggestion).ToHaveCountAsync(0);

        var prompt = PromptSent();
        Assert.Contains("Ring til Anna om fakturaen på fredag", prompt);
        Assert.Contains("Today is ", prompt);
    }

    [Fact]
    public async Task Splitting_a_task_adds_only_the_subtasks_left_ticked()
    {
        _ollama.Answer = """{"subTasks":["Find ejeren","Bestil certifikat","Genstart gatewayen"]}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.NewTaskInput.FillAsync("Opdatér certifikatet");
        await tasks.NewTaskInput.PressAsync("Enter");
        await tasks.RowTitled("Opdatér certifikatet").ClickAsync();
        await tasks.Detail.GetByTestId("split-task").ClickAsync();

        var choices = tasks.Detail.GetByTestId("split-choice");
        await Assertions.Expect(choices).ToHaveCountAsync(3);
        await choices.Nth(1).UncheckAsync();
        await tasks.Detail.GetByTestId("split-accept").ClickAsync();

        // The span, because the row also holds the delete button's ✕.
        await Assertions.Expect(tasks.SubTaskRows.Locator("span"))
            .ToHaveTextAsync(["Find ejeren", "Genstart gatewayen"]);
        await Assertions.Expect(tasks.Detail.GetByTestId("split-suggestions")).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.SubTaskProgress).ToHaveTextAsync("0/2");
    }

    [Fact]
    public async Task A_summary_replaces_the_note_of_an_imported_task_only_when_accepted()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder()
                .Titled("CSV-eksport fejler")
                .WithNote("Kunden oplever at eksporten fejler over 10.000 rækker.\n\nMvh Lene")
                .FromJira("SAAS-1")
                .Build(),
            new Setting { Key = SettingKeys.JiraBaseUrl, Value = "https://jira.test" });
        _ollama.Answer = """{"summary":"Eksporten fejler over 10.000 rækker og skal streame filen."}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.RowTitled("CSV-eksport fejler").ClickAsync();
        await tasks.Detail.GetByTestId("summarize-note").ClickAsync();

        await Assertions.Expect(tasks.Detail.GetByTestId("summary-text"))
            .ToHaveTextAsync("Eksporten fejler over 10.000 rækker og skal streame filen.");
        await Assertions.Expect(tasks.NoteRendered).ToContainTextAsync("Mvh Lene");

        await tasks.Detail.GetByTestId("summary-accept").ClickAsync();

        await Assertions.Expect(tasks.NoteRendered)
            .ToHaveTextAsync("Eksporten fejler over 10.000 rækker og skal streame filen.");
        await Assertions.Expect(tasks.Detail.GetByTestId("summary-suggestion")).ToHaveCountAsync(0);

        // The link is still the way back to the full description, which is why only an imported
        // task is offered a summary at all.
        await Assertions.Expect(App.Page.GetByTestId("external-link")).ToHaveCountAsync(1);
    }

    private string PromptSent()
    {
        var request = Assert.Single(_ollama.ChatRequests);

        return (string)request["messages"]![0]!["content"]!;
    }
}
