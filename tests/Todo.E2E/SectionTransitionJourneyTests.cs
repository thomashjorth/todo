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
    private const int WideWidth = 1400;
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

    /// <summary>
    /// Side by side, which is where the transition was switched off until 2026-08-25 and where it now
    /// has to work - the user runs the app full screen on 4K.
    ///
    /// Two things at once, and they are two because either alone would be green for the wrong reason.
    /// The transition has to <em>run</em> (`ready` resolved), which a broken nesting could take away;
    /// and the three parts of the clipping mechanism have to be <em>there</em>, because a missing part
    /// takes the clip away silently and gives back the bleed - the animation keeps working, and no
    /// timing-free assertion but this one would notice.
    /// </summary>
    [Fact]
    public async Task Side_by_side_runs_the_transition_and_keeps_the_clipping_mechanism()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(MovingTitle).Build(),
            new TaskItemBuilder(Clock).Titled(AnchorTitle).DueOn(AnchorDeadline).Build());

        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.DetailColumn).ToBeVisibleAsync();

        // Part one of the mechanism: the column is a group, so a row's group can hang under it.
        var columnName = await tasks.TaskColumn.EvaluateAsync<string>(
            "(el) => el.style.getPropertyValue('view-transition-name')");
        Assert.Equal("task-column", columnName);

        // Part two: every row points its group at that name.
        var groups = await tasks.Rows.EvaluateAllAsync<string[]>(
            "(rows) => rows.map((row) => row.style.getPropertyValue('view-transition-group'))");
        Assert.All(groups, group => Assert.Equal("task-column", group));

        // Part three: the rule that actually clips them. Read off the served stylesheet rather than
        // the source, because that is the copy the browser obeys - and a build that dropped it would
        // otherwise pass.
        var clipRule = await App.Page.EvaluateAsync<string>(
            """
            () => [...document.styleSheets]
              .flatMap((sheet) => [...sheet.cssRules])
              .map((rule) => rule.cssText)
              .find((text) => text.includes('view-transition-group-children(task-column)')) ?? 'missing'
            """);
        Assert.Contains("overflow: clip", clipRule);

        await App.Page.EvaluateAsync(RecordTransitions);

        await tasks.RowShowing(MovingTitle).ClickAsync();

        // Not ToBeVisibleAsync, and that is the whole point: side by side the panel is already there
        // for the auto-selected task, so "visible" is true before the click has been rendered and the
        // fill below would land on the previous task - measured, it edited the anchor's deadline and
        // Senere stayed at one row. The empty value is what only the moving task can show, since the
        // anchor has a deadline. Same trap CLAUDE.md records for an action taken right after a click.
        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(string.Empty);

        await tasks.DeadlineInput.FillAsync(Deadline.ToString("yyyy-MM-dd"));
        await tasks.DeadlineInput.PressAsync("Enter");

        await Assertions.Expect(tasks.RowsIn("Senere")).ToHaveCountAsync(2);

        await App.Page.WaitForFunctionAsync(
            "() => window.__transitions.length > 0 "
            + "&& window.__transitions.every((entry) => entry.ready !== 'pending')");

        var states = await App.Page.EvaluateAsync<string[]>(
            "() => window.__transitions.map((entry) => entry.ready)");

        var state = Assert.Single(states);
        Assert.True(
            state == "resolved",
            $"Side by side the browser skipped the view transition: ready settled as '{state}'.");
    }
}
