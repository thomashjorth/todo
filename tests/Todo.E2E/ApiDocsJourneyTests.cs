using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Todo.E2E;

/// <summary>
/// Two claims about the API documentation, and nothing else in the suite covers either: that the
/// button on the health line asks the shell for the right URL, and that the page behind that URL
/// renders without a single request leaving this process.
/// </summary>
public class ApiDocsJourneyTests : BrowserTest
{
    private const int ColumnWidth = 480;

    /// <summary>
    /// The documentation page. The trailing slash is load-bearing: <c>/scalar</c> answers with a
    /// 302 to this one, so a URL without it is a different URL.
    /// </summary>
    private const string DocsPath = "/scalar/";

    /// <summary>
    /// Scalar's bundle is 3.5 MB and parses the contract before it draws anything, so the first
    /// render here is slower than anything else in this suite. Waiting on the locators rather than
    /// on a fixed delay is still what decides when the test moves on.
    /// </summary>
    private const float RenderTimeout = 30_000;

    private readonly BrowserFixture _fixture;

    /// <summary>
    /// The fixture is held rather than only handed to the base class, because the offline test
    /// needs a page of its own: its routes have to be in place before the very first request, and
    /// the page the harness opens has already loaded the app by the time a test can reach it.
    /// </summary>
    public ApiDocsJourneyTests(BrowserFixture fixture) : base(fixture) => _fixture = fixture;

    [Fact]
    public async Task The_health_line_asks_the_shell_to_open_the_documentation_page()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var opened = new TaskCompletionSource<string?>();

        // Aborting is not only how the URL is read: letting the call through would ask the
        // operating system to open a real browser window on the machine running the tests.
        await App.Page.RouteAsync("**/api/system/open-link", async route =>
        {
            opened.TrySetResult(route.Request.PostDataJSON()?.GetProperty("url").GetString());
            await route.AbortAsync();
        });

        // The button lives in the ok branch; the failure branch has none by design, so the state
        // has to be reached before the button can be looked for. This is the gate, not the
        // assertion: the button sits inside this paragraph, so anything phrased against the
        // paragraph would pass on the status text alone, with no button on the page at all.
        await Assertions.Expect(App.Health).ToContainTextAsync("API: ok");

        var button = App.Page.GetByTestId("api-docs");

        await Assertions.Expect(button).ToBeVisibleAsync();

        // An <a href> would let a middle-click take this window somewhere else, and the Photino
        // window has no address bar and no back button to come back with. Nothing but this
        // assertion stops the button being "simplified" into a link.
        Assert.Equal("BUTTON", await button.EvaluateAsync<string>("el => el.tagName"));

        await App.Page.EvaluateAsync("window.stampedBeforeTheClick = true");

        await button.ClickAsync();

        // The whole URL, not a substring: /scalar without the trailing slash answers with a 302,
        // and a substring assertion on "/scalar" would be happy with it. The origin is asserted
        // too, because the port is assigned at startup and only the app itself knows it.
        Assert.Equal(
            DocsUrl(),
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(
            await App.Page.EvaluateAsync<bool>("window.stampedBeforeTheClick === true"),
            "The click took the window with it, and this window has no way back.");
    }

    [Fact]
    public async Task The_documentation_page_renders_and_asks_no_foreign_host()
    {
        var page = await _fixture.Browser.NewPageAsync();
        var foreign = new ConcurrentBag<string>();

        try
        {
            // Everything is routed, and only this host's own URLs are let through. The abort is
            // what makes the page prove it works offline; the bag is what makes it prove more than
            // that. Scalar releases fast, and a version bump that reintroduced a CDN would still
            // render fine on a machine with a network — the bag is what would catch it.
            await page.RouteAsync("**/*", async route =>
            {
                if (route.Request.Url.StartsWith(Host.BaseUrl, StringComparison.Ordinal))
                {
                    await route.ContinueAsync();
                    return;
                }

                foreign.Add(route.Request.Url);
                await route.AbortAsync();
            });

            await page.GotoAsync(DocsUrl());

            // Text Scalar draws out of the document. That is the point of asserting on it rather
            // than on a status code: this text exists only if the bundle loaded, ran, fetched the
            // contract and parsed it.
            //
            // "Todo API" is the contract's own title. The derived document at /openapi/v1.json
            // would have titled the page "Todo.Host | v1", so this string also says which of the
            // two documents the page is reading.
            await ExpectDrawn(page.GetByText("Todo API", new() { Exact = true }).First);
            await ExpectDrawn(page.GetByText("OpenAPI 3.0.4", new() { Exact = true }).First);

            // An operation, and the summary beside it: prose lives only in the contract — the
            // derivation has a summary on 0 of its 15 operations.
            await ExpectDrawn(page.GetByText("/api/health", new() { Exact = true }).First);
            await ExpectDrawn(page.GetByText("Reports that the API is running.").First);

            // Read last, because the calls that matter come after mount rather than out of the
            // HTML: this is where "Ask AI" was caught fetching api.scalar.com/vector/registry
            // twice, with all four assertions above already green. Waiting for the render is
            // therefore also what gives a late request time to arrive.
            Assert.True(foreign.IsEmpty,
                "The documentation page asked for hosts outside this process:"
                + $"{Environment.NewLine}{string.Join(Environment.NewLine, foreign.Distinct().Order())}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static Task ExpectDrawn(ILocator locator)
        => Assertions.Expect(locator).ToBeVisibleAsync(new() { Timeout = RenderTimeout });

    private string DocsUrl() => $"{Host.BaseUrl.TrimEnd('/')}{DocsPath}";
}
