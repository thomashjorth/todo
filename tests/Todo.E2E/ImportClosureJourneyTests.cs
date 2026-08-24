using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Settings;
using Todo.TestSupport.Ado;
using Todo.TestSupport.Time;

using IClock = Todo.Core.Time.IClock;

namespace Todo.E2E;

/// <summary>
/// A work item is imported, finishes in Azure DevOps, and the next import offers to close the task
/// here — the whole feature, end to end, against a real <see cref="FakeAdo"/> on loopback rather than
/// an intercepted preview.
///
/// The real chain is what makes this worth writing. Nothing here stages a <c>suggestsClosing</c>
/// field: the work items are imported through the shipping code path, the done list is a setting, and
/// the second preview asks the database for the local status. A route handler could put the label on
/// screen, but it could not measure that the app <em>decided</em> to.
/// </summary>
public class ImportClosureJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    /// <summary>
    /// The two work items standing in <c>Active</c>, which is the state this journey calls finished.
    /// Two rather than one on purpose: 17162 carries a timestamp Azure DevOps cannot parse, so the
    /// closure has to fall back to the clock for it while 16901 keeps the source's date — and a fixture
    /// with only the readable one would leave that fallback unrun.
    /// </summary>
    private const string StoryTitle = "Som bruger vil jeg kunne filtrere";

    private const string UnreadableTitle = "Med et ulaeseligt tidsstempel";

    /// <summary>Still in <c>Blocked</c>, so it stays an ordinary imported-before row throughout.</summary>
    private const string BlockedTitle = "Kunden kan ikke logge ind";

    private const string ClosingLabel = "Løst i Azure DevOps — luk opgaven her.";
    private const string SeenBeforeLabel = "importeret tidligere";

    /// <summary>How many of FakeAdo's nine work items pass the default type filter.</summary>
    private const int FilteredRows = 5;

    private static readonly FixedClock Clock = new(FakeAdo.Today);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task A_work_item_finished_in_Azure_DevOps_offers_to_close_the_task_it_was_imported_as()
    {
        await using var fake = await FakeAdo.StartAsync();

        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoBaseUrl, Value = fake.BaseUrl },
            new Setting { Key = SettingKeys.AdoProject, Value = FakeAdo.Project },
            new Setting { Key = SettingKeys.AdoToken, Value = FakeAdo.Token });

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1800 });

        // Nothing clicks an item's link, but a stray click must not reach the machine — and the abort
        // is a property of this test rather than of the file.
        await App.Page.RouteAsync("**/api/system/open-link", route => route.AbortAsync());

        var ado = await App.GoToAdo();
        await ado.PreviewAsync();
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);

        // Imported while Active still means work. Everything the type filter lets through comes in,
        // which is what makes the second preview a screen of imported-before rows.
        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync($"{FilteredRows} importeret, 0 sprunget over");

        // The user now calls Active finished. A setting rather than a stub: the preview reads it, and
        // so does the import when it takes the decision again.
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoDoneStates, Value = "[\"Active\"]" });

        await ado.PreviewAsync();
        await Assertions.Expect(ado.Rows).ToHaveCountAsync(FilteredRows);

        // The offer, on exactly the rows that earned it. The negative half is the teeth: without it a
        // template that put the label on every imported row would pass.
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(ado.Row(StoryTitle)))
            .ToHaveTextAsync(ClosingLabel);
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(ado.Row(UnreadableTitle)))
            .ToHaveTextAsync(ClosingLabel);
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(ado.Row(BlockedTitle)))
            .ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(ado.Row(BlockedTitle)))
            .ToHaveTextAsync(SeenBeforeLabel);

        // Nothing left to import and two to close, so the button says the second thing rather than
        // "Importér 0 sager" — three labels exist precisely so each is true of its own case.
        await Assertions.Expect(ado.ImportButton).ToHaveTextAsync("Luk 2 opgaver");

        await ado.ImportAsync();
        await Assertions.Expect(ado.Receipt)
            .ToHaveTextAsync("0 importeret, 0 sprunget over. 2 opgaver lukket.");

        // The offer is gone on the next look, which is the half a set of imported keys could not do:
        // the preview reads the local status, so an accepted closure stops being suggested.
        await ado.PreviewAsync();
        await Assertions.Expect(AdoImportScreen.SuggestsClosingIn(ado.Row(StoryTitle)))
            .ToHaveCountAsync(0);
        await Assertions.Expect(AdoImportScreen.AlreadyImportedIn(ado.Row(StoryTitle)))
            .ToHaveTextAsync(SeenBeforeLabel);

        // And the tasks really are done, which is the only assertion the user would recognise.
        var tasks = await App.GoToTasks();
        await tasks.ShowCompleted.CheckAsync();
        // Counted and filtered rather than ToContainTextAsync, which is measured rather than
        // fussy: that assertion reads one element, so against the two rows here it answered
        // "null" and timed out. A filter per title says which row is which and retries honestly.
        await Assertions.Expect(tasks.CompletedRows).ToHaveCountAsync(2);
        await Assertions.Expect(Completed(tasks, StoryTitle)).ToHaveCountAsync(1);
        await Assertions.Expect(Completed(tasks, UnreadableTitle)).ToHaveCountAsync(1);

        // The one that never finished is still open, so "everything got closed" cannot pass for this.
        await Assertions.Expect(tasks.RowFor(BlockedTitle)).ToHaveCountAsync(1);
        await Assertions.Expect(Completed(tasks, BlockedTitle)).ToHaveCountAsync(0);
    }

    private static ILocator Completed(TaskListScreen tasks, string title)
        => tasks.CompletedRows.Filter(new() { HasText = title });
}
