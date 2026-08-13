using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

public class AppSmokeTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task App_loads_and_shows_api_health()
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        await using var host = await RunningHost.StartAsync();
        var page = await fixture.Browser.NewPageAsync();

        await page.GotoAsync(host.BaseUrl);

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Todo", Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("health"))
            .ToContainTextAsync("API: ok");
    }

    [Fact]
    public async Task App_fits_the_480px_column_without_horizontal_scroll()
    {
        await using var host = await RunningHost.StartAsync();
        var page = await fixture.Browser.NewPageAsync(new()
        {
            ViewportSize = new() { Width = ColumnWidth, Height = 1000 }
        });

        await page.GotoAsync(host.BaseUrl);

        await Assertions.Expect(page.GetByTestId("health")).ToBeVisibleAsync();

        var scrollWidth = await page.EvaluateAsync<int>("document.documentElement.scrollWidth");
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }
}
