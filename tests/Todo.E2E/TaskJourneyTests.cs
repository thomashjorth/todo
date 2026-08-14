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
        await using var host = await RunningHost.StartAsync();

        var app = await TodoApp.OpenAsync(
            fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });
        var tasks = app.Tasks;

        await Assertions.Expect(app.Health).ToContainTextAsync("API: ok");

        await tasks.NewTaskInput.FillAsync(TaskTitle);
        await tasks.NewTaskInput.PressAsync("Enter");

        await Assertions.Expect(tasks.RowsIn("Uden deadline")).ToHaveTextAsync(TaskTitle);

        await tasks.RowTitled(TaskTitle).ClickAsync();
        await Assertions.Expect(tasks.Detail).ToBeVisibleAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        await tasks.DeadlineInput.FillAsync(today.ToString("yyyy-MM-dd"));
        await tasks.DeadlineInput.PressAsync("Enter");

        await Assertions.Expect(tasks.Section("Uden deadline")).ToHaveCountAsync(0);
        await Assertions.Expect(tasks.RowsIn("I dag"))
            .ToContainTextAsync($"Deadline: {Deadlines.InDanish(today)}");

        await tasks.NewSubTaskInput.FillAsync("Male bønner");
        await tasks.NewSubTaskInput.PressAsync("Enter");
        await Assertions.Expect(tasks.SubTaskRows).ToHaveCountAsync(1);

        await tasks.NewSubTaskInput.FillAsync("Kog vand");
        await tasks.NewSubTaskInput.PressAsync("Enter");
        await Assertions.Expect(tasks.SubTaskRows).ToHaveCountAsync(2);

        await Assertions.Expect(tasks.SubTaskProgress).ToHaveTextAsync("0/2");

        await tasks.SubTaskRows.First.Locator("input[type=checkbox]").CheckAsync();

        await Assertions.Expect(tasks.SubTaskProgress).ToHaveTextAsync("1/2");
        await Assertions.Expect(tasks.CompleteToggle).Not.ToBeCheckedAsync();

        await tasks.CompleteToggle.ClickAsync();

        await Assertions.Expect(tasks.Rows).ToHaveCountAsync(0);

        await tasks.ShowCompleted.CheckAsync();

        await Assertions.Expect(tasks.CompletedRows).ToHaveTextAsync(TaskTitle);
        await Assertions.Expect(tasks.CompletedRows.GetByText(TaskTitle, new() { Exact = true }))
            .ToHaveCSSAsync("text-decoration-line", "line-through");

        var scrollWidth = await app.ScrollWidthAsync();
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }
}
