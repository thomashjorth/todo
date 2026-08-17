using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

public class WaitingJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;
    private const int DaysWaited = 12;
    private const string DueTodayTitle = "Send referatet";
    private const string LongWaitTitle = "Svar fra revisoren";
    private const string ParkedTitle = "Læs bogen om typografi";
    private const string FirstColleague = "Mette";
    private const string SecondColleague = "Rasmus";

    // Every day this journey counts is counted from here: on the real clock a run that crossed
    // midnight would turn "12 dage" into 13, and "0 dage" into 1.
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    [Fact]
    public async Task A_task_waits_on_someone_is_released_waits_again_from_zero_and_a_third_is_parked()
    {
        await using var host = await RunningHost.StartWithAsync(
            services => services.AddSingleton<IClock>(Clock));

        await host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(DueTodayTitle).DueToday().Build(),
            new TaskItemBuilder(Clock)
                .Titled(LongWaitTitle)
                .WaitingFor(SecondColleague, Clock.UtcNow.AddDays(-DaysWaited))
                .Build(),
            new TaskItemBuilder(Clock).Titled(ParkedTitle).Build());

        var app = await TodoApp.OpenAsync(
            fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });
        var tasks = app.Tasks;

        await Assertions.Expect(tasks.RowsIn("I dag")).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.RowFor(DueTodayTitle))
            .ToContainTextAsync($"Deadline: {Deadlines.InDanish(Clock.Today)}");

        await tasks.RowShowing(DueTodayTitle).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(DueTodayTitle)).ToBeVisibleAsync();

        await tasks.StatusSelect.SelectOptionAsync(new SelectOptionValue { Label = "Venter på" });
        await Assertions.Expect(tasks.WaitingOnInput).ToBeVisibleAsync();

        await tasks.WaitingOnInput.FillAsync(FirstColleague);
        await tasks.WaitingOnInput.BlurAsync();

        await Assertions.Expect(tasks.Section("I dag")).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.WaitingRows).ToHaveCountAsync(2);
        await Assertions.Expect(tasks.RowFor(DueTodayTitle))
            .ToContainTextAsync($"Venter på: {FirstColleague}");
        await Assertions.Expect(tasks.WaitingDaysFor(DueTodayTitle)).ToHaveTextAsync("0 dage");

        // Arranged as an older wait, so the counter can be read without the test waiting.
        await Assertions.Expect(tasks.RowFor(LongWaitTitle))
            .ToContainTextAsync($"Venter på: {SecondColleague}");
        await Assertions.Expect(tasks.WaitingDaysFor(LongWaitTitle))
            .ToHaveTextAsync($"{DaysWaited} dage");

        await tasks.StatusSelect.SelectOptionAsync(new SelectOptionValue { Label = "Åben" });

        await Assertions.Expect(tasks.RowsIn("I dag")).ToContainTextAsync(DueTodayTitle);
        await Assertions.Expect(tasks.WaitingRows).ToHaveCountAsync(1);

        // The waiting line is only drawn while the task waits, so "the name is gone" says nothing
        // until the task waits again: this is where a wait that was never cleared shows itself.
        await tasks.StatusSelect.SelectOptionAsync(new SelectOptionValue { Label = "Venter på" });

        await Assertions.Expect(tasks.WaitingOnInput).ToHaveValueAsync("");
        await Assertions.Expect(tasks.RowFor(DueTodayTitle))
            .Not.ToContainTextAsync($"Venter på: {FirstColleague}");
        await Assertions.Expect(tasks.WaitingDaysFor(DueTodayTitle)).ToHaveTextAsync("0 dage");

        await tasks.RowShowing(ParkedTitle).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(ParkedTitle)).ToBeVisibleAsync();

        await tasks.StatusSelect.SelectOptionAsync(new SelectOptionValue { Label = "Måske" });

        await Assertions.Expect(tasks.RowFor(ParkedTitle)).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.SomedaySection).ToHaveCountAsync(0);

        await tasks.ShowSomeday.CheckAsync();

        // The row comes back still expanded, so its text is the whole detail rather than a title.
        await Assertions.Expect(tasks.SomedayRows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.SomedayRows).ToContainTextAsync(ParkedTitle);

        var pageWidth = await app.ClientWidthAsync();
        var scrolledWidth = await app.ScrollWidthAsync();

        Assert.True(scrolledWidth <= pageWidth,
            $"The waiting and parked sections push the page sideways: scrollWidth was {scrolledWidth} "
            + $"against a clientWidth of {pageWidth}.");
    }
}
