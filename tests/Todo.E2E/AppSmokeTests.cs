using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

public class AppSmokeTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task App_loads_and_shows_api_health()
    {
        await using var host = await RunningHost.StartAsync();

        var app = await TodoApp.OpenAsync(fixture.Browser, host);

        await Assertions.Expect(app.Heading).ToBeVisibleAsync();
        await Assertions.Expect(app.Health).ToContainTextAsync("API: ok");
    }

    [Fact]
    public async Task App_fits_the_480px_column_without_horizontal_scroll()
    {
        await using var host = await RunningHost.StartAsync();

        var app = await TodoApp.OpenAsync(
            fixture.Browser, host, new() { Width = ColumnWidth, Height = 1000 });

        await Assertions.Expect(app.Health).ToBeVisibleAsync();

        var scrollWidth = await app.ScrollWidthAsync();
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }
}
