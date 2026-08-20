using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

// Playwright has a clock of its own, which is not the one the app reads the date from.
using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// Delegating a task, all the way through: a name goes on the list in the settings, and the task
/// list offers it when a task moves to WaitingFor. Delegating is a shortcut to a state that already
/// exists — WaitingFor plus a name — so what this journey has to prove is that the shortcut
/// <em>saves</em>, not that a new state was invented.
/// </summary>
public class DelegateJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string TaskTitle = "Send referatet";
    private const string Colleague = "Flemming Overgaard";

    // The row this journey reads counts days, so the clock is fixed: on the real one a run that
    // crossed midnight would turn "0 dage" into 1.
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 17));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_name_from_the_delegate_list_is_offered_and_saved_when_a_task_starts_waiting()
    {
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder(Clock).Titled(TaskTitle).DueToday().Build());

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        // Saved through the page rather than seeded. Nothing in this suite stubs /api/settings, and
        // a seeded row would not say that the group stores anything.
        var settings = await App.GoToSettings();
        await settings.AddDelegateAsync(Colleague);

        var tasks = await settings.GoToTasks();

        // The suggestions reached the screen. Their popup is the browser's own chrome and cannot be
        // driven from Playwright, but the options are DOM — so this is the honest assertion about
        // them, and it is what would fail if the list attribute or the shared datalist went away.
        // Never visible: a <datalist> is not rendered, so count and value are the only questions.
        await Assertions.Expect(tasks.DelegateOptions).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.DelegateOptions).ToHaveAttributeAsync("value", Colleague);

        await tasks.RowShowing(TaskTitle).ClickAsync();
        await Assertions.Expect(tasks.DetailFor(TaskTitle)).ToBeVisibleAsync();

        await tasks.StatusSelect.SelectOptionAsync(new SelectOptionValue { Label = "Venter på" });

        // The field does not exist yet at this point: @if hangs on the reloaded task's status, so a
        // server round trip sits between the choice and the field. And the reload moves the row out
        // of "I dag" and into the waiting section — two different @for blocks — so the <li> that was
        // clicked is destroyed and a fresh one renders the field. Both waits below are therefore on
        // the section the row ends up in rather than on the one it left.
        await Assertions.Expect(tasks.WaitingRows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.WaitingSection.GetByTestId("waiting-on-input"))
            .ToBeVisibleAsync();

        // The picker asks and the field answers, and the focus ring is the whole affordance.
        // ToBeFocusedAsync retries, which this needs: the effect that moves the focus runs after the
        // field renders, so a one-shot read of document.activeElement would race it.
        await Assertions.Expect(tasks.WaitingOnInput).ToBeFocusedAsync();

        // Where a user picking from the popup ends up: the name in the field. The popup is
        // unreachable from here, so the value is set the way a chooser leaves it, and everything
        // below is about what got stored rather than about the click that stored it.
        await tasks.WaitingOnInput.FillAsync(Colleague);
        await tasks.WaitingOnInput.BlurAsync();

        await Assertions.Expect(tasks.RowFor(TaskTitle))
            .ToContainTextAsync($"Venter på: {Colleague}");

        // The last leg, and the only one that can tell a name that was chosen from a name that was
        // saved: a reload reads the task back out of the database. Measured — with waitingOn left
        // out of the save, every assertion above still passes and this one falls.
        tasks = await App.ReloadAsync();

        await Assertions.Expect(tasks.WaitingRows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.RowFor(TaskTitle))
            .ToContainTextAsync($"Venter på: {Colleague}");

        // The clock the wait counts from is set by the move itself, and the move went through the
        // picker: a day count here says the shortcut left the task in a real waiting state rather
        // than only writing a name.
        await Assertions.Expect(tasks.WaitingDaysFor(TaskTitle)).ToHaveTextAsync("0 dage");
    }
}
