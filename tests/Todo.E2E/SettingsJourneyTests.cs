using Microsoft.Playwright;
using Todo.TestSupport;
using Todo.TestSupport.Builders;

namespace Todo.E2E;

public class SettingsJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;
    private const string TaskTitle = "Ring til tandlægen";

    [Fact]
    public async Task A_language_is_chosen_holds_across_screens_survives_a_restart_and_is_handed_back()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "TodoApp.Tests", $"settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "todo.db");
        var today = DateOnly.FromDateTime(DateTime.Now);

        try
        {
            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                await host.AddAndSaveChangesAsync(
                    new TaskItemBuilder().Titled(TaskTitle).DueToday().Build());

                var app = await TodoApp.OpenAsync(
                    fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });

                // The journey begins in the system language, so it only reads as intended when the
                // browser reports Danish. Said here, rather than left for step 2 to fail cryptically.
                var systemLanguage = await app.Page.EvaluateAsync<string>("navigator.language");
                Assert.StartsWith("da", systemLanguage, StringComparison.OrdinalIgnoreCase);

                await Assertions.Expect(app.Tasks.RowsIn("I dag"))
                    .ToContainTextAsync($"Deadline: {Deadlines.InDanish(today)}");

                var settings = await app.GoToSettings();

                // A reload would land in English too, so the document is stamped first: the stamp
                // is only still there if the language was switched without leaving the page.
                await app.Page.EvaluateAsync("window.stampedBeforeTheSwitch = true");

                await settings.ChooseLanguageAsync("en");

                await Assertions.Expect(settings.Heading).ToHaveTextAsync("Settings");
                Assert.True(
                    await app.Page.EvaluateAsync<bool>("window.stampedBeforeTheSwitch === true"),
                    "The page reloaded, so it says nothing about switching language in place.");

                var tasks = await settings.GoToTasks();

                await Assertions.Expect(tasks.RowsIn("Today"))
                    .ToContainTextAsync($"Deadline: {Deadlines.InEnglish(today)}");
                await AssertFitsTheColumnAsync(app);

                settings = await tasks.GoToSettings();

                await Assertions.Expect(settings.Heading).ToHaveTextAsync("Settings");

                // The page arrives folded, so the select has to be brought back on screen: the
                // fold is view state and is deliberately not stored, which is what makes coming
                // back to a folded page the expected thing rather than a bug.
                await settings.OpenAsync(SettingsScreen.LanguageSection);
                await Assertions.Expect(settings.Language).ToHaveValueAsync("en");

                await app.Page.CloseAsync();
            }

            await using (var host = await RunningHost.StartAtAsync(databasePath))
            {
                var app = await TodoApp.OpenAsync(
                    fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });

                await Assertions.Expect(app.Tasks.RowsIn("Today"))
                    .ToContainTextAsync($"Deadline: {Deadlines.InEnglish(today)}");

                var settings = await app.GoToSettings();

                await settings.ChooseLanguageAsync("system");

                await Assertions.Expect(settings.Heading).ToHaveTextAsync("Indstillinger");

                var tasks = await settings.GoToTasks();

                await Assertions.Expect(tasks.RowsIn("I dag"))
                    .ToContainTextAsync($"Deadline: {Deadlines.InDanish(today)}");
                await AssertFitsTheColumnAsync(app);
            }
        }
        finally
        {
            RunningHost.ClearConnectionPoolFor(databasePath);
            TryDelete(directory);
        }
    }

    private static async Task AssertFitsTheColumnAsync(TodoApp app)
    {
        var scrollWidth = await app.ScrollWidthAsync();

        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
