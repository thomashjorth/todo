using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// WCAG AA over every screen, in both colour schemes. The measurement runs in the browser
/// because only it knows which background a given piece of text ended up on.
/// </summary>
public class ContrastTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const int DaysWaited = 12;
    private const string DueTodayTitle = "Send referatet";
    private const string CompletedTitle = "Ryd skrivebordet";
    private const string SomedayTitle = "Læs om typografi";

    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Every_screen_meets_WCAG_AA(ColorScheme scheme)
    {
        // One task per state, so no section of the list goes unmeasured.
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled("Betal regningen").Overdue().Build(),
            new TaskItemBuilder(Clock).Titled(DueTodayTitle).DueToday().Build(),
            new TaskItemBuilder(Clock).Titled("Svar revisoren")
                .WaitingFor("Mette", Clock.UtcNow.AddDays(-DaysWaited)).Build(),
            new TaskItemBuilder(Clock).Titled(SomedayTitle).Someday().Build(),
            new TaskItemBuilder(Clock).Titled(CompletedTitle).Done().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1400 }, scheme);

        var failures = new List<string>();
        var tasks = App.Tasks;

        // The two switches reveal the completed and someday sections. Each measurement below waits
        // for the content it is there to measure: a switch only sets a signal, and a measurement
        // that raced the render would skip whole sections — which would later let this test go
        // green over text it never looked at.
        await tasks.ShowCompleted.CheckAsync();
        await tasks.ShowSomeday.CheckAsync();

        // Waiting on the text, not just the element, is what makes these waits real: a freshly
        // rendered component has its elements in place before the localized strings are
        // interpolated into them, and an element with no text yet is invisible to the measurement.
        await Assertions.Expect(tasks.CompletedRows).ToContainTextAsync(CompletedTitle);
        await Assertions.Expect(tasks.SomedayRows).ToContainTextAsync(SomedayTitle);
        failures.AddRange(await App.ContrastFailuresAsync());

        // The detail panel is the largest single block of colour, and it only exists expanded.
        // RowShowing, not RowTitled: the deadline line joins this row's accessible name.
        await tasks.RowShowing(DueTodayTitle).ClickAsync();
        await Assertions.Expect(tasks.Detail).ToContainTextAsync("Underopgaver");
        failures.AddRange(await App.ContrastFailuresAsync());

        var import = await App.GoToImport();
        await Assertions.Expect(import.AnalyseButton).ToHaveTextAsync("Analysér");
        failures.AddRange(await App.ContrastFailuresAsync());

        var settings = await App.GoToSettings();
        await Assertions.Expect(settings.Heading).ToHaveTextAsync("Indstillinger");
        failures.AddRange(await App.ContrastFailuresAsync());

        // The message carries every line, because this list is the work list: Assert.Empty prints
        // only the first five, which is not enough to fix anything by.
        var distinct = failures.Distinct().Order().ToList();

        Assert.True(distinct.Count == 0,
            $"{distinct.Count} contrast failures in {scheme}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, distinct));
    }
}
