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
/// these four are about what the app paints once focus has arrived — the first two that the ring
/// is there, the last two that nothing clips it away again.
/// </summary>
public class FocusTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    // The width the two-column layout starts at, and the only width where anything in the app
    // clips: the breakpoint is 1280, and a viewport sitting exactly on it would make every number
    // here depend on a scrollbar. Same reason and same number as the side-by-side journeys.
    private const int WideWidth = 1400;

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
    /// A visible ring is not the same as a ring you can see all of. Side by side turned three
    /// elements into scroll containers — the wrapper around <c>router-outlet</c> and the two
    /// columns — and <c>overflow-y: auto</c> forces <c>overflow-x</c> to <c>auto</c> with it, so
    /// each one clips at its padding edge. Every field in the app is <c>w-full</c> and therefore
    /// stood flush against that edge, and the ring sits 4px <em>outside</em> the border box, so
    /// its left segment was cut. Measured off the screenshot that reported it: the search box ran
    /// x=32..489 inside a column whose scrollport started at x=32.
    /// <para>
    /// The assertion is about room rather than about the ring itself, and that is what lets it
    /// cover the detail panel too: those fields carry no <c>focus-visible:outline-*</c> classes
    /// and rely on Chromium's UA ring, whose painted width is nowhere in computed style. 4px is
    /// the app's own ring — <c>outline-2</c> plus <c>outline-offset-2</c> — and the widest one it
    /// paints, so it is the number every field has to have room for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task No_field_on_the_task_list_stands_closer_to_a_clipping_edge_than_its_ring()
    {
        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        // The panel is the half of this that only exists side by side, and it needs a task to
        // show: auto-selection picks the first selectable one, so creating it is enough.
        await tasks.NewTaskInput.FillAsync("Køb kaffe");
        await tasks.NewTaskInput.PressAsync("Enter");
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        var cramped = await App.Page.EvaluateAsync<string[]>(RingRoom);

        Assert.Empty(cramped);
    }

    /// <summary>
    /// The wrapper around <c>router-outlet</c> is the clipping container for the four screens that
    /// are not the task list, so its own padding is the only thing standing between a settings
    /// field and the same cut. A group has to be open for there to be a field at all — the
    /// accordion renders the closed ones not at all.
    /// </summary>
    [Fact]
    public async Task No_field_on_the_settings_screen_stands_closer_to_a_clipping_edge_than_its_ring()
    {
        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var settings = await App.GoToSettings();
        await settings.OpenAsync(SettingsScreen.LanguageSection);
        await Assertions.Expect(settings.Language).ToBeVisibleAsync();

        var cramped = await App.Page.EvaluateAsync<string[]>(RingRoom);

        Assert.Empty(cramped);
    }

    /// <summary>
    /// What `focus-visible:outline-2 focus-visible:outline-blue-600` paints in the light theme,
    /// in the shape <see cref="TodoApp.FocusOutlineAsync"/> reports.
    /// </summary>
    private static string Ring(string testId) => $"{testId}|solid|2px|{Blue600}";

    /// <summary>
    /// Every focusable element on screen that sits closer to a clipping ancestor's scrollport than
    /// the ring it would paint, named along with the container that would cut it. Nothing is
    /// focused while this runs: the question is geometric, and a focused element would only tell
    /// us about one of them.
    /// </summary>
    private const string RingRoom = """
        () => {
          const needed = 4;

          const focusable = 'input, select, textarea, button, a[href], [tabindex]:not([tabindex="-1"])';

          const cramped = [];

          for (const el of document.querySelectorAll(focusable)) {
            const b = el.getBoundingClientRect();
            if (b.width === 0 && b.height === 0) continue;

            // Markdown from a note is not the app's layout: a wide table in a note scrolls on
            // purpose, and a link inside it is that table's business rather than this rule's.
            if (el.closest('[data-testid="note-rendered"]')) continue;

            const name = el.dataset.testid ?? el.id ?? el.tagName.toLowerCase();

            for (let p = el.parentElement; p; p = p.parentElement) {
              const s = getComputedStyle(p);
              if (s.overflowX === 'visible' && s.overflowY === 'visible') continue;

              // The scrollport, not the bounding box: clipping happens at the padding edge, and
              // clientLeft/clientWidth leave out the border and the scrollbar just as it does.
              const r = p.getBoundingClientRect();
              const left = r.left + p.clientLeft;
              const room = { left: b.left - left, right: left + p.clientWidth - b.right };
              const holder = p.dataset.testid ?? p.tagName.toLowerCase();

              for (const side of ['left', 'right']) {
                if (room[side] < needed - 0.5) {
                  cramped.push(
                    `${name} has ${Math.round(room[side])}px on the ${side} inside ${holder}, needs ${needed}`);
                }
              }
            }
          }

          return cramped;
        }
        """;
}
