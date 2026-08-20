using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// Keyboard focus has to be visible. Three inputs used to set `outline: none` and put only a
/// border colour change in its place — and after the contrast pass raised the resting border to
/// the same colour, that change became invisible too.
///
/// Asserting the whole painted ring, colour included, is deliberate. Chromium's own UA ring is
/// `auto 1px -webkit-focus-ring-color`, so a test that only asked for "some outline, not 0px"
/// would stay green if every `focus-visible:outline-*` class were deleted — the UA ring would
/// answer for them. Real Tab-driven focus is covered by <see cref="KeyboardJourneyTests"/>;
/// these two are about what the app paints once focus has arrived.
/// </summary>
public class FocusTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    /// <summary>
    /// Tailwind 4's `blue-600` as Chromium serialises it. The palette is authored in oklch and
    /// `getComputedStyle` hands a colour back in the space it was written in, so this is the
    /// observed string, not an rgb() guess.
    /// </summary>
    private const string Blue600 = "oklch(0.546 0.245 262.881)";

    [Fact]
    public async Task Focusing_the_new_task_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await App.Tasks.NewTaskInput.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.Equal(Ring("new-task-input"), outline);
    }

    [Fact]
    public async Task Focusing_a_settings_field_leaves_a_visible_outline()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var settings = await App.GoToSettings();
        await settings.OpenAsync(SettingsScreen.LanguageSection);

        await settings.Language.FocusAsync();

        var outline = await App.FocusOutlineAsync();

        Assert.Equal(Ring("language-select"), outline);
    }

    /// <summary>
    /// What `focus-visible:outline-2 focus-visible:outline-blue-600` paints in the light theme,
    /// in the shape <see cref="TodoApp.FocusOutlineAsync"/> reports.
    /// </summary>
    private static string Ring(string testId) => $"{testId}|solid|2px|{Blue600}";
}
