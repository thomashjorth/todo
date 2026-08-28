using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// The weekly review, all the way through: the marker says a review is owed, the walk groups the
/// tasks by the question each group asks, a decision made inside the review moves the row out of
/// its group, and finishing clears the marker.
/// </summary>
public class ReviewJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    private const string OverdueTitle = "Svar på tilbuddet";
    private const string ParkedTitle = "Lær at spille harmonika";

    /// <summary>
    /// A Wednesday, so the most recent Friday 15:00 is five days back and a fresh database owes a
    /// review from the first render. Fixed rather than real: on the real clock a run on a Friday
    /// morning would find nothing owed and the marker would be gone for the wrong reason.
    /// </summary>
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 26), new TimeOnly(9, 0));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    /// <summary>
    /// The payoff of reusing <c>li[appTaskRow]</c> with its whole panel: TaskStore reloads after a
    /// save, so the row leaves its group the moment the decision is made. A review holding its own
    /// copy of the list would go stale instead and the row would sit there — see the design's
    /// section 3, which is why <c>GET /api/review</c> carries no tasks.
    /// </summary>
    [Fact]
    public async Task Deciding_from_the_review_moves_the_row_out_of_its_group()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(ParkedTitle).Someday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var review = await App.GoToReview();

        // Someday is the only group holding anything, so it opened itself. Asserted rather than
        // assumed: if it had not, the click below would be opening it instead of the row.
        await Assertions.Expect(review.GroupToggle(ReviewScreen.SomedayGroup))
            .ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(review.RowsIn(ReviewScreen.SomedayGroup)).ToHaveCountAsync(1);

        // Open the row's panel and move the task out of Someday, which is exactly the decision this
        // group exists to prompt.
        await review.RowsIn(ReviewScreen.SomedayGroup).GetByRole(AriaRole.Button).First.ClickAsync();
        // The panel's only select, the way TaskListScreen.StatusSelect finds it — there is no test
        // id on it.
        await App.Page.GetByTestId("task-detail").Locator("select").SelectOptionAsync("open");

        // The row is gone from Someday and has appeared under a deadline group, without a reload
        // and without leaving the screen.
        await Assertions.Expect(review.RowsIn(ReviewScreen.SomedayGroup)).ToHaveCountAsync(0);
        await Assertions.Expect(review.GroupToggle(ReviewScreen.SomedayGroup)).ToBeVisibleAsync();

        await review.OpenGroupAsync(ReviewScreen.NoDeadlineGroup);
        await Assertions.Expect(review.RowsIn(ReviewScreen.NoDeadlineGroup)).ToHaveCountAsync(1);
    }

    /// <summary>
    /// The marker's whole loop. It is on the nav link rather than on the task list, so the first
    /// assertion is made from the task list — a screen the review is not on.
    /// </summary>
    [Fact]
    public async Task The_marker_says_a_review_is_owed_until_one_is_taken()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var marker = App.Page.GetByTestId("review-due");
        await Assertions.Expect(marker).ToBeVisibleAsync();

        // The word rather than the dot: a colour alone is not a distinction, so the link's
        // accessible name has to change. Substring matching is what makes the plain name match too,
        // which is why the assertion after the click is Exact.
        await Assertions.Expect(App.Page.GetByRole(AriaRole.Link, new() { Name = "Gennemgang forfalden" }))
            .ToBeVisibleAsync();

        var review = await App.GoToReview();
        await Assertions.Expect(review.Never).ToBeVisibleAsync();

        await review.Complete.ClickAsync();

        // A round trip stands between the click and the read: the reply is what clears the marker,
        // and reading straight after the click would measure the state before it arrived.
        await Assertions.Expect(review.LastCompleted).ToBeVisibleAsync();
        await Assertions.Expect(marker).ToHaveCountAsync(0);
        await Assertions.Expect(
                App.Page.GetByRole(AriaRole.Link, new() { Name = "Gennemgang", Exact = true }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// The empty review, which is a state of its own rather than a failure: five headings, five
    /// zeroes and a sentence. Nothing is open, because nothing has anything in it.
    /// </summary>
    [Fact]
    public async Task An_empty_review_says_so_rather_than_showing_nothing()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var review = await App.GoToReview();

        await Assertions.Expect(review.Empty).ToBeVisibleAsync();
        await Assertions.Expect(review.GroupToggle(ReviewScreen.OverdueGroup))
            .ToHaveAttributeAsync("aria-expanded", "false");
    }

    /// <summary>
    /// A new letter gets its own keystroke test even though the machinery is shared and covered by
    /// Alt_O and its siblings: the one risk the machinery cannot vouch for is whether the browser
    /// eats the combination. Alt+D, Alt+E and Alt+F are all taken by Chrome.
    /// </summary>
    [Fact]
    public async Task Alt_G_follows_the_review_link()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(OverdueTitle).Overdue().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        await Assertions.Expect(App.Tasks.NewTaskInput).ToBeVisibleAsync();

        await App.Page.Keyboard.PressAsync("Alt+g");

        var review = new ReviewScreen(App);
        await Assertions.Expect(review.GroupToggle(ReviewScreen.OverdueGroup)).ToBeVisibleAsync();
        await Assertions.Expect(App.Tasks.NewTaskInput).ToHaveCountAsync(0);
    }
}
