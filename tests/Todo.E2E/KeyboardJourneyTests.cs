using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// Every action has to be reachable without a mouse. This walks one task from created to
/// expanded to deleted using only the keyboard.
/// </summary>
public class KeyboardJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string Title = "Send referatet";

    /// <summary>
    /// The three nav links in app.html are the first focusable elements on the page, so the
    /// field is the fourth stop — not the first. Asserted below rather than assumed: a fourth
    /// link, or a skip link, has to fail here with the name of what appeared, not somewhere
    /// later where the failure would read as a broken field.
    /// </summary>
    private static readonly string[] TrailToTheField =
        ["nav-tasks", "nav-import", "nav-settings", "new-task-input"];

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_task_can_be_created_expanded_and_deleted_without_a_mouse()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1200 });
        var tasks = App.Tasks;

        // Create: focus the field by tabbing, not by clicking.
        var trail = new List<string>();
        for (var i = 0; i < TrailToTheField.Length; i++)
        {
            await App.Page.Keyboard.PressAsync("Tab");
            trail.Add(await FocusedTestIdAsync());
        }

        Assert.Equal(TrailToTheField, trail);

        await App.Page.Keyboard.TypeAsync(Title);
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeVisibleAsync();

        // Expand: the row title is a button, so Enter on it must open the detail panel.
        await tasks.RowTitled(Title).FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        // The note's edit button is the keyboard path to editing — the click handlers on the
        // rendered note are a mouse shortcut, not the only way in.
        await tasks.Detail.GetByTestId("note-edit").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.NoteEditor).ToBeVisibleAsync();

        // The editor takes focus on its own, so Escape reaches it without a click. Checked
        // rather than assumed: Escape pressed at the page would close nothing and leave the
        // editor open, and the delete below would still pass.
        Assert.Equal("note-editor", await FocusedTestIdAsync());

        await App.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tasks.NoteEditor).ToBeHiddenAsync();

        // Delete: reachable and activatable by keyboard.
        await tasks.Detail.GetByTestId("delete-task").FocusAsync();
        await App.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(tasks.RowTitled(Title)).ToBeHiddenAsync();
    }

    private Task<string> FocusedTestIdAsync() => App.Page.EvaluateAsync<string>(
        "() => document.activeElement?.dataset.testid ?? 'none'");
}
