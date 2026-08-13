using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

public class TaskJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;
    private const string TaskTitle = "Køb kaffe";

    [Fact]
    public async Task A_task_is_created_scheduled_split_into_subtasks_completed_and_shown_again()
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        await using var host = await RunningHost.StartAsync();
        var page = await fixture.Browser.NewPageAsync(new()
        {
            ViewportSize = new() { Width = ColumnWidth, Height = 1000 }
        });

        await page.GotoAsync(host.BaseUrl);
        await Assertions.Expect(page.GetByTestId("health")).ToContainTextAsync("API: ok");

        var newTask = page.GetByTestId("new-task-input");
        await newTask.FillAsync(TaskTitle);
        await newTask.PressAsync("Enter");

        await Assertions.Expect(Section(page, "Uden deadline").GetByTestId("task-row"))
            .ToHaveTextAsync(TaskTitle);

        await page.GetByTestId("task-row")
            .GetByRole(AriaRole.Button, new() { Name = TaskTitle, Exact = true })
            .ClickAsync();
        await Assertions.Expect(page.GetByTestId("task-detail")).ToBeVisibleAsync();

        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var deadline = page.GetByTestId("task-detail").Locator("input[type=date]");
        await deadline.FillAsync(today);
        await deadline.PressAsync("Enter");

        await Assertions.Expect(Section(page, "Uden deadline")).ToHaveCountAsync(0);
        await Assertions.Expect(Section(page, "I dag").GetByTestId("task-row"))
            .ToContainTextAsync($"Deadline: {today}");

        var newSubTask = page.GetByTestId("new-subtask-input");
        await newSubTask.FillAsync("Male bønner");
        await newSubTask.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("subtask-row")).ToHaveCountAsync(1);

        await newSubTask.FillAsync("Kog vand");
        await newSubTask.PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("subtask-row")).ToHaveCountAsync(2);

        await Assertions.Expect(page.GetByTestId("subtask-progress")).ToHaveTextAsync("0/2");

        await page.GetByTestId("subtask-row").First.Locator("input[type=checkbox]").CheckAsync();

        await Assertions.Expect(page.GetByTestId("subtask-progress")).ToHaveTextAsync("1/2");
        await Assertions.Expect(page.GetByTestId("complete-toggle")).Not.ToBeCheckedAsync();

        await page.GetByTestId("complete-toggle").ClickAsync();

        await Assertions.Expect(page.GetByTestId("task-row")).ToHaveCountAsync(0);

        await page.GetByTestId("show-completed").CheckAsync();

        var completedRow = page.GetByTestId("completed-section").GetByTestId("task-row");
        await Assertions.Expect(completedRow).ToHaveTextAsync(TaskTitle);
        await Assertions.Expect(completedRow.GetByText(TaskTitle, new() { Exact = true }))
            .ToHaveCSSAsync("text-decoration-line", "line-through");

        var scrollWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }

    private static ILocator Section(IPage page, string heading) =>
        page.GetByTestId("task-section").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })
        });
}
