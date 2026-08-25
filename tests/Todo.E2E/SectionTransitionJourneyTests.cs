using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// The one thing about the section transitions that only a real browser can see: that the
/// transition actually <em>ran</em> rather than being skipped.
///
/// Everything else about the feature is measured from Vitest - the gate, the four guards, the two
/// names. What no unit test can reach is the browser's own verdict, and it has one: two elements
/// claiming the same `view-transition-name` make the whole transition skip, and the only place that
/// shows is `ready` rejecting. The repo has met that class of bug before, when
/// `data-testid="task-detail"` existed twice and Playwright silently picked the first.
///
/// 480 px on purpose. `WideScreen` switches the transition off from `xl` (1280 px), which is
/// measured and deliberate - see section 8 of docs/plans/2026-08-25-section-transitions-design.md -
/// so a journey at WideWidth would assert that nothing happened and pass on nothing.
/// </summary>
public class SectionTransitionJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string MovingTitle = "Book flybilletterne";
    private const string AnchorTitle = "Skriv årsrapporten";

    /// <summary>
    /// Wraps `document.startViewTransition` and records how each call's `ready` settled.
    ///
    /// Installed after the app has loaded rather than as an init script, and that is safe rather
    /// than lucky: the store reads the property off `document` at every call, and nothing calls it
    /// before a save. `ready` and not `finished`, because measured in Chromium 148 `finished`
    /// resolves even for a transition that was skipped - it is the wrong question to ask here.
    /// </summary>
    private const string RecordTransitions = """
        () => {
          window.__transitions = [];
          const real = document.startViewTransition.bind(document);
          document.startViewTransition = (update) => {
            const entry = { ready: 'pending' };
            window.__transitions.push(entry);
            const transition = real(update);
            transition.ready.then(
              () => {
                entry.ready = 'resolved';
              },
              (error) => {
                entry.ready = 'rejected: ' + error.name + ': ' + error.message;
              },
            );
            return transition;
          };
        }
        """;

    // The buckets are sums on today's date, so a run crossing midnight would move the boundary and
    // send the task to a different section than the journey names.
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    // Far enough out to land in Senere, so the move is between two sections a reader can name.
    private static readonly DateOnly Deadline = new(2026, 8, 31);
    private static readonly DateOnly AnchorDeadline = new(2026, 9, 7);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task Giving_a_task_a_deadline_runs_a_view_transition_the_browser_does_not_skip()
    {
        // Two tasks, and the second one is the assertion's teeth rather than scenery: a duplicate
        // `view-transition-name` is what makes the browser skip a transition, and with a single row
        // on screen that failure cannot be expressed at all - a mutation naming every row the same
        // thing would still leave one name in the document, and this test would pass on nothing.
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(MovingTitle).Build(),
            new TaskItemBuilder(Clock).Titled(AnchorTitle).DueOn(AnchorDeadline).Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.RowsIn("Uden deadline")).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.RowsIn("Senere")).ToHaveCountAsync(1);

        await App.Page.EvaluateAsync(RecordTransitions);

        await tasks.RowShowing(MovingTitle).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(MovingTitle)).ToBeVisibleAsync();

        await tasks.DeadlineInput.FillAsync(Deadline.ToString("yyyy-MM-dd"));
        await tasks.DeadlineInput.PressAsync("Enter");

        // The move itself first. Without it the transition assertion below could be green while the
        // feature it exists for did nothing.
        await Assertions.Expect(tasks.RowsIn("Senere")).ToHaveCountAsync(2);
        await Assertions.Expect(tasks.Section("Uden deadline")).ToHaveCountAsync(0);

        await App.Page.WaitForFunctionAsync(
            "() => window.__transitions.length > 0 "
            + "&& window.__transitions.every((entry) => entry.ready !== 'pending')");

        var states = await App.Page.EvaluateAsync<string[]>(
            "() => window.__transitions.map((entry) => entry.ready)");

        var state = Assert.Single(states);
        Assert.True(
            state == "resolved",
            $"The browser skipped the view transition: ready settled as '{state}'. The way that "
            + "happens here is two elements claiming the same view-transition-name, which nothing "
            + "else in the suite can see - the list still updates, so every other assertion stays "
            + "green.");
    }
}
