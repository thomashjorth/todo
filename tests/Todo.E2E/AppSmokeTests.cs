using Microsoft.Playwright;

namespace Todo.E2E;

public class AppSmokeTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task App_loads_and_shows_api_health()
    {
        await OpenAppAsync();

        await Assertions.Expect(App.Heading).ToBeVisibleAsync();
        await Assertions.Expect(App.Health).ToContainTextAsync("API: ok");
    }

    [Fact]
    public async Task App_fits_the_480px_column_without_horizontal_scroll()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        await Assertions.Expect(App.Health).ToBeVisibleAsync();

        var scrollWidth = await App.ScrollWidthAsync();
        Assert.True(scrollWidth <= ColumnWidth,
            $"The page overflows the {ColumnWidth}px column: scrollWidth was {scrollWidth}.");
    }
}
