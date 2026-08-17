using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// Keyboard focus has to be visible. Three inputs used to set `outline: none` and put only a
/// border colour change in its place — and after the contrast pass raised the resting border to
/// the same colour, that change became invisible too.
/// </summary>
public class FocusTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task Tabbing_to_the_new_task_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Tasks.NewTaskInput.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.DoesNotContain("none", outline);
        Assert.DoesNotContain("0px", outline);
    }

    [Fact]
    public async Task Tabbing_to_a_settings_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var settings = await App.GoToSettings();

        await settings.Language.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.DoesNotContain("none", outline);
        Assert.DoesNotContain("0px", outline);
    }
}
