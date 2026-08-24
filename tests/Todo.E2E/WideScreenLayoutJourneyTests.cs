using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// The four screens that are <em>not</em> the task list, seen at the width where the task list took
/// the layout over. <c>main</c> carries <c>xl:h-screen</c> so the two columns have a frame to scroll
/// inside — and that frame is the whole app's, so every other screen is clamped by it too. Only the
/// task list handles it, because its columns carry <c>xl:overflow-y-auto</c> of their own; see
/// <see cref="SideBySideJourneyTests.The_columns_scroll_on_their_own"/> for that half.
/// </summary>
public class WideScreenLayoutJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    // The same width as the side-by-side journeys, and for the same reason: the breakpoint is 1280,
    // and a viewport sitting exactly on it would make every number here depend on a scrollbar.
    private const int WideWidth = 1400;

    // Short enough that the list is taller than the window, which is the state the bug needs.
    private const int ShortHeight = 600;

    private const int Actions = 40;

    /// <summary>
    /// The retro board is the cheapest long list in the app: it is parsed locally, so no foreign
    /// system has to answer for the screen to be taller than the window.
    /// </summary>
    private static readonly string Board = BoardWith(Actions);

    /// <summary>
    /// Measured before it was written: at 1400x600 the wrapper around <c>router-outlet</c> is 424px
    /// tall while the import screen is 1310px, and with <c>overflow: visible</c> nothing clips - so
    /// the rows ran straight through the health line, which sits at a fixed offset inside a 600px
    /// <c>main</c>. Scrolling the page did not help: <c>main</c> moves as a whole, so the overlap
    /// travels with it.
    /// <para>
    /// The assertion has to ask what a row actually <em>paints</em>, not what its rectangle says:
    /// <c>getBoundingClientRect</c> does not clip, so a row inside a scroll container still reports
    /// the full box and a plain intersection test is green under both the bug and the fix. Measured -
    /// the first version of this test passed on the broken layout. The visible rectangle is the box
    /// cut down by every ancestor that clips, and it comes out at the wrapper's bottom edge once the
    /// wrapper scrolls.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_long_import_list_scrolls_inside_the_window_rather_than_through_the_footer()
    {
        await OpenAppAsync(new() { Width = WideWidth, Height = ShortHeight });

        var import = await App.GoToImport();

        await import.PasteAsync(Board);
        await import.AnalyseAsync();
        await Assertions.Expect(import.Rows).ToHaveCountAsync(Actions);

        // Without this the overlap question means nothing: a footer pushed out of the window
        // overlaps nothing either, and that is not a fix.
        await Assertions.Expect(App.Health).ToBeInViewportAsync();

        var overlapping = await App.Page.EvaluateAsync<string[]>(RowsOverPainting("health"));

        Assert.Empty(overlapping);
    }

    /// <summary>
    /// Returns the rows whose painted rectangle covers the element with the given test id, named by
    /// their own text so a failure says which rows landed on top of it.
    /// </summary>
    private static string RowsOverPainting(string testId) => $$"""
        () => {
          const target = document.querySelector('[data-testid="{{testId}}"]');
          const t = target.getBoundingClientRect();

          // The rectangle an element really paints in: its own box, cut down by every ancestor that
          // clips. getBoundingClientRect on its own reports the box a scroll container is hiding.
          const painted = el => {
            const b = el.getBoundingClientRect();
            const box = { top: b.top, bottom: b.bottom, left: b.left, right: b.right };
            for (let p = el.parentElement; p; p = p.parentElement) {
              const s = getComputedStyle(p);
              if (s.overflowX === 'visible' && s.overflowY === 'visible') continue;
              const r = p.getBoundingClientRect();
              box.top = Math.max(box.top, r.top);
              box.bottom = Math.min(box.bottom, r.bottom);
              box.left = Math.max(box.left, r.left);
              box.right = Math.min(box.right, r.right);
            }
            return box;
          };

          return [...document.querySelectorAll('[data-testid="retro-row"]')]
            .filter(row => {
              const b = painted(row);
              if (b.bottom <= b.top || b.right <= b.left) return false;
              return b.top < t.bottom && b.bottom > t.top && b.left < t.right && b.right > t.left;
            })
            .map(row => row.textContent.replace(/\s+/g, ' ').trim().slice(0, 60));
        }
        """;

    private static string BoardWith(int actions)
    {
        var rows = Enumerable.Range(1, actions).Select(i =>
            $"\"Handling nummer {i}\",\"Mette Kirkegaard\",\"7/17/26, 1:32 PM\",\"Actions\",\"\",\"\"");

        return string.Join(
            Environment.NewLine,
            ["\"Content\",\"Author\",\"Created\",\"Zone\",\"Action Due Date\",\"Action Owner\"", .. rows]);
    }
}
