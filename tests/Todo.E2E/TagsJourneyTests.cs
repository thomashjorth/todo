using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Ai;
using Todo.Core.Tags;
using Todo.TestSupport.Ai;
using Todo.TestSupport.Builders;

namespace Todo.E2E;

/// <summary>
/// Tags end to end: defined under Settings, suggested in the background after a task is created,
/// ticked in the panel, and filtered by. The fake Ollama answers the suggestion; everything else is
/// the real host.
/// </summary>
public class TagsJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
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

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Configure<AiOptions>(o =>
        {
            o.BaseUrl = _ollama.BaseUrl;
            o.EmbeddingModel = FakeOllama.EmbeddingModel;
        });

    /// <summary>
    /// The whole wish in one journey: the tags are the user's own, the task is created at once, the
    /// suggestion arrives afterwards without taking the focus, Alt+Y adds it, and the filter finds it.
    /// </summary>
    [Fact]
    public async Task A_new_task_is_created_at_once_and_its_suggested_tags_are_added_with_Alt_Y()
    {
        _ollama.Answer = """{"tags":["Indkøb"]}""";
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var settings = await App.GoToSettings();
        await settings.OpenAsync(SettingsScreen.TagsSection);
        foreach (var name in new[] { "Indkøb", "Privat" })
        {
            await App.Page.GetByTestId("tag-input").FillAsync(name);
            await App.Page.GetByTestId("tag-input").PressAsync("Enter");
            await Assertions.Expect(App.Page.GetByTestId("tag-row").Filter(new() { HasText = name })).ToBeVisibleAsync();
        }

        var tasks = await App.GoToTasks();
        await tasks.NewTaskInput.FillAsync("Køb mælk");
        await tasks.NewTaskInput.PressAsync("Enter");

        // Typing the next line while the suggestion is on its way: it never takes the field away.
        await tasks.NewTaskInput.PressSequentiallyAsync("Ring til");

        await Assertions.Expect(tasks.RowTitled("Køb mælk")).ToBeVisibleAsync();
        await Assertions.Expect(App.Page.GetByTestId("tag-suggestion-names")).ToHaveTextAsync("Indkøb");
        await Assertions.Expect(tasks.NewTaskInput).ToBeFocusedAsync();
        await Assertions.Expect(tasks.NewTaskInput).ToHaveValueAsync("Ring til");

        await App.Page.Keyboard.PressAsync("Alt+y");

        await Assertions.Expect(App.Page.GetByTestId("tag-suggestion")).ToHaveCountAsync(0);
        var tagged = tasks.Rows.Filter(new() { Has = App.Page.GetByTestId("row-tags") });
        await Assertions.Expect(tagged).ToHaveCountAsync(1);
        await Assertions.Expect(tagged.GetByTestId("row-tags")).ToHaveTextAsync("Indkøb");

        await App.Page.GetByTestId("tag-filter-option").Filter(new() { HasText = "Indkøb" }).ClickAsync();

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.RowShowing("Køb mælk")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The user's own report (2026-10-26): the last task carrying "estimat" was deleted, and its button
    /// stayed above the list, where pressing it could only empty it. The tag itself stays defined.
    /// </summary>
    [Fact]
    public async Task Deleting_the_last_task_carrying_a_tag_takes_its_button_out_of_the_filter()
    {
        var estimat = new Tag { Name = "estimat" };
        var privat = new Tag { Name = "Privat" };
        var tagged = new TaskItemBuilder().Titled("Adyen talk").Build();
        tagged.Tags.Add(estimat);
        var other = new TaskItemBuilder().Titled("Køb mælk").Build();
        other.Tags.Add(privat);
        await Host.AddAndSaveChangesAsync(estimat, privat, tagged, other);
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;
        var buttons = App.Page.GetByTestId("tag-filter-option");
        await Assertions.Expect(buttons).ToHaveTextAsync(["estimat", "Privat"]);

        await tasks.RowShowing("Adyen talk").ClickAsync();
        await tasks.Detail.GetByTestId("delete-task").ClickAsync();

        await Assertions.Expect(tasks.RowShowing("Adyen talk")).ToHaveCountAsync(0);
        await Assertions.Expect(buttons).ToHaveTextAsync(["Privat"]);
    }

    /// <summary>Ticked in the panel and saved at once; two pressed filters narrow to the task with both.</summary>
    [Fact]
    public async Task Tags_ticked_in_the_panel_narrow_the_list_together()
    {
        var privat = new Tag { Name = "Privat" };
        var indkob = new Tag { Name = "Indkøb" };
        var both = new TaskItemBuilder().Titled("Køb mælk").Build();
        both.Tags.Add(privat);
        await Host.AddAndSaveChangesAsync(privat, indkob, both, new TaskItemBuilder().Titled("Ring til tandlægen").Build());
        both.Tags.Clear();
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        await tasks.RowShowing("Køb mælk").ClickAsync();
        // The panel and its tags render after the click; pressing before them would find no letter.
        await Assertions.Expect(tasks.Detail.GetByTestId("task-tag")).ToHaveCountAsync(2);
        await App.Page.Keyboard.PressAsync("Alt+Shift+G");
        var first = tasks.Detail.GetByTestId("task-tag").First;
        await Assertions.Expect(first).ToBeFocusedAsync();
        await tasks.Detail.GetByRole(AriaRole.Checkbox, new() { Name = "Indkøb" }).CheckAsync();
        await Assertions.Expect(tasks.RowShowing("Køb mælk").GetByTestId("row-tags")).ToHaveTextAsync("IndkøbPrivat");

        await App.Page.Keyboard.PressAsync("Alt+l");
        await App.Page.Keyboard.PressAsync("Space");
        await App.Page.GetByTestId("tag-filter-option").Filter(new() { HasText = "Privat" }).ClickAsync();

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(App.Page.GetByTestId("tag-filter-option").Filter(new() { HasText = "Indkøb" }))
            .ToHaveAttributeAsync("aria-pressed", "true");
    }
}
