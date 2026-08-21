using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// The two-column layout, which no other journey can see: every one of them opens the app at 480px,
/// so everything behind the <c>xl</c> breakpoint — the right-hand column, the auto-selection, the
/// selection that stays, the independent scrolling — was unmeasured before this class existed.
/// </summary>
public class SideBySideJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    // Wide enough for the two columns and then some: the breakpoint is 1280, and a viewport sitting
    // exactly on it would make every assertion here depend on whether a scrollbar counts.
    private const int WideWidth = 1400;

    private const string FirstTitle = "Ring til rørlæggeren";
    private const string SecondTitle = "Betal regningen";

    /// <summary>A finished task, used by one journey purely to have a reload to wait for.</summary>
    private const string DoneTitle = "Ryd skrivebordet";

    // Deferredness and the buckets are sums on today's date, so a run crossing midnight would move
    // the boundary underneath the fixture.
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    // Distinct deadlines are how the panel says which task it is showing: it has no title field, so
    // the date is the identity. Both are overdue, which puts both tasks in the same section - the
    // one place the in-progress rule can reorder them.
    private static readonly DateOnly FirstDeadline = Clock.Today.AddDays(-1);
    private static readonly DateOnly SecondDeadline = Clock.Today.AddDays(-3);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    /// <summary>
    /// One panel, and outside every row. Two copies would both carry <c>data-testid="task-detail"</c>
    /// and a locator would silently take the first, which is why the breakpoint is a signal driving
    /// an <c>@if</c> rather than a <c>hidden xl:block</c>.
    /// </summary>
    [Fact]
    public async Task The_panel_stands_in_its_own_column_and_not_inside_a_row()
    {
        await SeedTwoAsync();
        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.DetailColumn).ToBeVisibleAsync();
        await Assertions.Expect(tasks.Detail).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DetailColumn.GetByTestId("task-detail")).ToHaveCountAsync(1);

        // The row-scoped locator every other journey uses, asserted empty on purpose: it is what
        // says the panel really left the row, and it pins the note on DetailFor to something
        // measured rather than remembered.
        await Assertions.Expect(tasks.DetailFor(FirstTitle)).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.DetailFor(SecondTitle)).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Auto-selection, and the fixture is the assertion's teeth. The server orders by deadline, so
    /// its first task is <see cref="SecondTitle"/>; the client then lifts the in-progress task to
    /// the top of the section, which makes <see cref="FirstTitle"/> the first task *on screen*. An
    /// implementation that reached for the server's first item would answer the other one.
    /// </summary>
    [Fact]
    public async Task The_first_task_on_screen_is_already_showing_before_anything_is_clicked()
    {
        await SeedTwoAsync();
        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        // Said out loud, so a later reader can see the ordering the teeth depend on: the lifted
        // task is on top, and it is not the one the server sent first.
        await Assertions.Expect(tasks.Rows.Nth(0)).ToContainTextAsync(FirstTitle);
        await Assertions.Expect(tasks.Rows.Nth(1)).ToContainTextAsync(SecondTitle);

        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(Iso(FirstDeadline));
    }

    /// <summary>
    /// Side by side the selection stays. In one column a second click is the only way to fold the
    /// panel away, and keeping that rule here would leave half the window empty for nothing.
    /// <para>
    /// The round trip in the middle is the whole reason this test is honest, and it was measured
    /// rather than reasoned. "The panel still shows the same task" cannot be proven by polling: the
    /// first poll to succeed ends the wait, and straight after a click the DOM has not re-rendered,
    /// so the unchanged value is there to be read. The version without the round trip was
    /// <em>green</em> under the mutation that puts the deselecting toggle back — and probing the
    /// field showed why: it went 2026-08-14 to 2026-08-16 a moment later, after the assertion had
    /// already passed. Showing the completed task forces a reload, so every render the click caused
    /// has happened before the panel is read.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Clicking_the_selected_row_again_keeps_it_showing()
    {
        await SeedTwoAsync();

        // Only here, and only to be the round trip: checking the switch reloads the list, and a row
        // that appears is something to wait for. A wait on the switch itself would prove nothing,
        // because a checkbox is checked before the answer comes back.
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(DoneTitle).DueOn(SecondDeadline).Done().Build());

        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        // The other task, so the click is a real move away from what was auto-selected.
        await tasks.RowShowing(SecondTitle).ClickAsync();
        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(Iso(SecondDeadline));

        await tasks.RowShowing(SecondTitle).ClickAsync();

        await tasks.ShowCompleted.CheckAsync();
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(DoneTitle);

        await Assertions.Expect(tasks.Detail).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(Iso(SecondDeadline));
    }

    /// <summary>
    /// The selection is derived, not stored, so a task that leaves the list takes the panel with it
    /// and the first one still in view answers. Searching is the cheapest way to make a task leave.
    /// </summary>
    [Fact]
    public async Task Searching_the_selected_task_away_moves_the_panel_to_the_first_one_left()
    {
        await SeedTwoAsync();
        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        await tasks.RowShowing(SecondTitle).ClickAsync();
        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(Iso(SecondDeadline));

        // Matches the other task only, so the selected one is the one that leaves.
        await tasks.Search.FillAsync("rørlæggeren");

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DeadlineInput).ToHaveValueAsync(Iso(FirstDeadline));
    }

    /// <summary>
    /// Auto-selection does not remove the empty state, and this is the state that proves it: a
    /// completed task's row is a plain list item with no panel behind it, so with only completed
    /// tasks in view there is nothing selectable. The row count is the teeth — without it the
    /// prompt would pass on an empty list too, which is a different state reached another way.
    /// </summary>
    [Fact]
    public async Task Only_completed_tasks_in_view_leaves_the_prompt_standing()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(SecondTitle).DueOn(SecondDeadline).Done().Build());

        await OpenAppAsync(new() { Width = WideWidth, Height = 1000 });
        var tasks = App.Tasks;

        await tasks.ShowCompleted.CheckAsync();

        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DetailEmpty).ToBeVisibleAsync();
        await Assertions.Expect(tasks.Detail).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The point of two columns rather than two halves: the list scrolls inside its own column, so
    /// the panel stays on screen. Measured as "the column moved and the page did not" — without
    /// <c>min-h-0</c> the columns cannot shrink below their content, the inner scroll never
    /// activates, and the numbers come out the other way round.
    /// </summary>
    [Fact]
    public async Task The_columns_scroll_on_their_own()
    {
        // Enough rows that the left column is taller than the window, which is what gives it
        // anything to scroll. A short viewport does the other half of that.
        var many = Enumerable.Range(1, 40)
            .Select(i => new TaskItemBuilder(Clock).Titled($"Opgave {i}").DueOn(FirstDeadline).Build())
            .ToArray();
        await Host.AddAndSaveChangesAsync(many);

        await OpenAppAsync(new() { Width = WideWidth, Height = 600 });
        var tasks = App.Tasks;
        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(many.Length);
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        await tasks.TaskColumn.EvaluateAsync("el => el.scrollTop = 400");

        var columnScroll = await tasks.TaskColumn.EvaluateAsync<int>("el => el.scrollTop");
        var pageScroll = await App.Page.EvaluateAsync<int>(
            "() => document.scrollingElement.scrollTop");

        Assert.True(columnScroll > 0, $"The list column did not scroll inside itself: {columnScroll}");
        Assert.Equal(0, pageScroll);

        // The user-facing half of the same fact, and not implied by the numbers above: a column that
        // scrolled while dragging the panel out of the window would satisfy both of them.
        await Assertions.Expect(tasks.Detail).ToBeInViewportAsync();
    }

    private Task SeedTwoAsync() => Host.AddAndSaveChangesAsync(
        new TaskItemBuilder(Clock).Titled(FirstTitle).DueOn(FirstDeadline).InProgress().Build(),
        new TaskItemBuilder(Clock).Titled(SecondTitle).DueOn(SecondDeadline).Build());

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
