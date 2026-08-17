using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// A start date is a tickler: the task is a real commitment, but not yet. This journey walks one
/// task out of its deadline section into Udskudt, keeps it there while a neighbouring field is
/// edited, and brings it back by clearing the date.
/// </summary>
public class DeferUntilJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string DeferredTitle = "Bestil vinterdæk";
    private const string AnchorTitle = "Skriv årsrapporten";

    // Deferredness is a sum on today's date, so on the real clock a run that crossed midnight
    // would move the boundary and turn "tomorrow" into today. Both dates are fixed from here.
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    // Both deadlines fall after this week, so the deferred task has somewhere to land that is not
    // Overskredet: a deadline in the past beats a start date in the future, and the journey would
    // then show the opposite of what it claims.
    private static readonly DateOnly Deadline = new(2026, 8, 31);
    private static readonly DateOnly MovedDeadline = new(2026, 9, 7);
    private static readonly DateOnly Tomorrow = Clock.Today.AddDays(1);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_task_starting_tomorrow_sits_in_Udskudt_survives_an_edit_and_returns_when_cleared()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock)
                .Titled(DeferredTitle)
                .DueOn(Deadline)
                .DeferredUntil(Tomorrow)
                .Build(),
            new TaskItemBuilder(Clock).Titled(AnchorTitle).DueOn(Deadline).Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });
        var tasks = App.Tasks;

        await Assertions.Expect(tasks.RowsIn("Udskudt")).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.Section("Udskudt")).ToContainTextAsync(DeferredTitle);

        // The second task holds the deadline section open the whole way. Without it, "not in
        // Senere" would also be true for the uninteresting reason that no such section existed.
        await Assertions.Expect(tasks.RowsIn("Senere")).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.Section("Senere")).ToContainTextAsync(AnchorTitle);
        await Assertions.Expect(tasks.Section("Senere")).Not.ToContainTextAsync(DeferredTitle);

        // The positive assertion proves the date format first. Without it the negative one below
        // could be green only because it looked for a string the app never writes.
        await Assertions.Expect(tasks.RowFor(DeferredTitle))
            .ToContainTextAsync($"Deadline: {Deadlines.InDanish(Deadline)}");
        await Assertions.Expect(tasks.RowFor(DeferredTitle))
            .Not.ToContainTextAsync(Deadlines.InDanish(Tomorrow));

        await tasks.RowShowing(DeferredTitle).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(DeferredTitle)).ToBeVisibleAsync();
        await Assertions.Expect(tasks.DeferUntilInput).ToHaveValueAsync(Iso(Tomorrow));

        // The backend reads an absent field as "clear", so a stored start date once disappeared
        // when something else was edited. The deadline is the something else, and the row's own
        // deadline line is what says the edit reached the server rather than stalling in the panel.
        await tasks.DeadlineInput.FillAsync(Iso(MovedDeadline));
        await tasks.DeadlineInput.PressAsync("Enter");

        await Assertions.Expect(tasks.RowFor(DeferredTitle))
            .ToContainTextAsync($"Deadline: {Deadlines.InDanish(MovedDeadline)}");
        await Assertions.Expect(tasks.RowsIn("Udskudt")).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DeferUntilInput).ToHaveValueAsync(Iso(Tomorrow));

        await tasks.DeferUntilInput.FillAsync(string.Empty);
        await tasks.DeferUntilInput.BlurAsync();

        await Assertions.Expect(tasks.Section("Udskudt")).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.RowsIn("Senere")).ToHaveCountAsync(2);
        await Assertions.Expect(tasks.Section("Senere")).ToContainTextAsync(DeferredTitle);

        // The row comes back still expanded, so the field is read off the reloaded task: this is
        // where a clear that never left the browser would show itself.
        await Assertions.Expect(tasks.DeferUntilInput).ToHaveValueAsync(string.Empty);

        var pageWidth = await App.ClientWidthAsync();
        var scrolledWidth = await App.ScrollWidthAsync();

        Assert.True(scrolledWidth <= pageWidth,
            $"The Udskudt section pushes the page sideways: scrollWidth was {scrolledWidth} "
            + $"against a clientWidth of {pageWidth}.");
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
