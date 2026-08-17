using Microsoft.Playwright;

namespace Todo.E2E;

public class RetroImportJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string Me = "Thomas Hjorth Hansen";
    private const string MyAction = "Skriv referatet fra retroen";
    private const string TheirAction = "Book et lokale til næste gang";

    private static readonly string Board = """
        "Content","Author","Created","Zone","Action Due Date","Action Owner"
        "Thomas Hjorth Hansen - Skriv referatet fra retroen","Mette Kirkegaard","7/17/26, 1:32 PM","Actions","24.7.2026","Thomas Hjorth Hansen"
        "Book et lokale til næste gang","Rasmus Bjerre","7/17/26, 1:33 PM","Actions","","Mette Kirkegaard"
        "9/10","Sofie Dalgaard","7/17/26, 1:34 PM","Mood","",""
        """;

    [Fact]
    public async Task A_board_is_pasted_claimed_by_an_alias_imported_once_and_recognised_next_time()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var import = await App.GoToImport();

        await import.PasteAsync(Board);
        await import.AnalyseAsync();

        await Assertions.Expect(import.Rows).ToHaveCountAsync(2);
        await Assertions.Expect(import.Skipped).ToHaveTextAsync("Sprang 1 afstemningskort over.");
        await Assertions.Expect(import.NoneMine).ToBeVisibleAsync();
        await Assertions.Expect(import.Error).ToHaveCountAsync(0);

        var mine = import.Row(MyAction);
        var theirs = import.Row(TheirAction);

        await Assertions.Expect(RetroImportScreen.PickOf(mine)).Not.ToBeCheckedAsync();
        await Assertions.Expect(RetroImportScreen.PickOf(theirs)).Not.ToBeCheckedAsync();
        await Assertions.Expect(mine).ToContainTextAsync($"{Me} - {MyAction}");

        var settings = await App.GoToSettings();

        await settings.AddAliasAsync(Me);

        // The screen is built anew on the way back, so the export has to be analysed again.
        import = await settings.GoToImport();

        await import.PasteAsync(Board);
        await import.AnalyseAsync();

        await Assertions.Expect(import.NoneMine).ToHaveCountAsync(0);
        await Assertions.Expect(RetroImportScreen.PickOf(mine)).ToBeCheckedAsync();
        await Assertions.Expect(RetroImportScreen.PickOf(theirs)).Not.ToBeCheckedAsync();
        await Assertions.Expect(mine.GetByText(MyAction, new() { Exact = true })).ToBeVisibleAsync();

        await import.ImportAsync();

        await Assertions.Expect(import.Receipt).ToHaveTextAsync("1 importeret, 0 sprunget over");

        await import.AnalyseAsync();

        await Assertions.Expect(import.AlreadyImported).ToHaveCountAsync(1);
        await Assertions.Expect(RetroImportScreen.PickOf(mine)).ToBeDisabledAsync();
        await Assertions.Expect(RetroImportScreen.PickOf(mine)).Not.ToBeCheckedAsync();

        var tasks = await import.GoToTasks();

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(1);
        await Assertions.Expect(tasks.Rows).ToContainTextAsync(MyAction);
        await Assertions.Expect(tasks.Rows)
            .ToContainTextAsync($"Deadline: {Deadlines.InDanish(new DateOnly(2026, 7, 24))}");
        await Assertions.Expect(tasks.Rows).ToContainTextAsync($"Opgavestiller: {Me}");

        var scrollWidth = await App.ScrollWidthAsync();
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }
}
