using Microsoft.Playwright;
using Todo.Helper;
using Todo.Helper.Tests;
using Todo.TestSupport.Jira;
using Todo.Tunnel;

namespace Todo.E2E;

/// <summary>
/// The Connections group with nothing played: the real host in Helper mode, the real helper serving its
/// page from the bundle in its own exe, and a FakeJira that wants the token. The user types the server
/// and the token into the frame, and the app around it follows.
/// <para>
/// One thing this cannot see: Chrome's Local Network Access prompt. Host and helper are both on
/// loopback here, and the prompt is about a public or LAN page reaching a loopback one - the NAS
/// framing the PC. That stays a measurement only the user can make (docs/HANDOFF.md).
/// </para>
/// </summary>
public class ConnectionsJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;
    private const string Key = "the-helpers-key";

    private RunningHelper _helper = null!;
    private FakeJira _jira = null!;

    protected override string[] HostArgs =>
    [
        "--Sources:Mode", "Helper",
        "--Helper:KeyHash", PasswordHash.Create(Key, iterations: 1),
        "--Helper:PageUrl", $"{_helper.BaseUrl}{HelperPage.Path}",
    ];

    public override async ValueTask InitializeAsync()
    {
        _jira = await FakeJira.StartAsync();

        // Paired already, but not told where the server is: the key hash is the host's configuration,
        // so it has to exist before the host starts, while the address is what the user types.
        _helper = await RunningHelper.StartAsync(new HelperSettings
        {
            HelperKey = Key,
            // A closed port, so nothing here can reach the user's real Ollama on 11434.
            OllamaUrl = $"http://127.0.0.1:{RunningHelper.FreePort()}",
        });

        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _helper.DisposeAsync();
        await _jira.DisposeAsync();
    }

    /// <summary>
    /// The whole of it, and the guard the design rests on: every request the browser sends to the host
    /// is recorded - the frame's included - and the token typed into the frame is in none of them. The
    /// recorder is shown to see the frame's traffic by finding the token in a request to the helper.
    /// </summary>
    [Fact]
    public async Task The_server_and_a_token_are_typed_into_the_helper_and_the_token_never_reaches_the_host()
    {
        await OpenAppAsync(new ViewportSize { Width = ColumnWidth, Height = 900 });

        var requests = new List<(string Url, string Content)>();
        App.Page.Request += (_, request) => requests.Add((request.Url, Describe(request)));

        var settings = await App.GoToSettings();
        await settings.OpenAsync(SettingsScreen.ConnectionsSection);
        await Assertions.Expect(App.Page.GetByTestId("helper-frame")).ToBeVisibleAsync();

        var frame = App.Page.FrameLocator("[data-testid='helper-frame']");
        await Assertions.Expect(frame.GetByTestId("connections-tunnel"))
            .ToHaveTextAsync("Ikke sat op endnu: hjælperen mangler serverens adresse eller en nøgle.");

        await frame.GetByTestId("connections-server-url").FillAsync(Host.BaseUrl);
        await frame.GetByTestId("connections-server-url").PressAsync("Tab");
        await Assertions.Expect(frame.GetByTestId("connections-tunnel")).ToHaveTextAsync("Forbundet til serveren.");

        await frame.GetByTestId("connections-jira-base-url").FillAsync(_jira.BaseUrl);
        await frame.GetByTestId("connections-jira-base-url").PressAsync("Tab");
        await frame.GetByTestId("connections-jira-token").FillAsync(FakeJira.Token);
        await frame.GetByTestId("connections-jira-token").PressAsync("Tab");
        await Assertions.Expect(frame.GetByTestId("connections-jira-token-state")).ToHaveTextAsync("Hjælperen har et token.");
        await Assertions.Expect(frame.GetByTestId("connections-jira-token")).ToHaveValueAsync("");

        // The app around the frame follows without a reload: the page said "changed", and the host
        // had the helper's hello by the time the settings were read again.
        await settings.OpenAsync(SettingsScreen.JiraSection);
        await Assertions.Expect(settings.JiraHelperConnection)
            .ToHaveTextAsync($"Forbundet til {_jira.BaseUrl} gennem hjælperen.");

        // And the token works where it is meant to: through the helper, to Jira.
        await settings.TestJiraConnection.ClickAsync();
        await Assertions.Expect(settings.JiraConnection).ToHaveTextAsync("Forbundet som Thomas");

        Assert.Contains(requests, r => r.Url.StartsWith(_helper.BaseUrl) && r.Content.Contains(FakeJira.Token));

        var leaks = requests
            .Where(r => r.Url.StartsWith(Host.BaseUrl) && r.Content.Contains(FakeJira.Token))
            .Select(r => r.Url)
            .ToList();
        Assert.True(leaks.Count == 0, $"The token reached the host in: {string.Join(", ", leaks)}");
    }

    /// <summary>
    /// The frame is another origin, so the page's own contrast walk cannot reach into it - this one runs
    /// inside. Measured in the states the page has: unpaired and unconnected, a fresh key's hash with
    /// its help, a token held with its button, and the red line when the helper refuses a save.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task The_connections_page_meets_WCAG_AA(ColorScheme scheme)
    {
        await OpenAppAsync(new ViewportSize { Width = ColumnWidth, Height = 900 }, scheme);

        var settings = await App.GoToSettings();
        await settings.OpenAsync(SettingsScreen.ConnectionsSection);
        await Assertions.Expect(App.Page.GetByTestId("helper-frame")).ToBeVisibleAsync();

        var frame = App.Page.FrameLocator("[data-testid='helper-frame']");
        var inside = App.Page.Frames.Single(f => f.Url.StartsWith(_helper.BaseUrl));
        var failures = new List<string>();

        async Task Snapshot(string state) =>
            failures.AddRange((await TodoApp.ContrastFailuresAsync(inside)).Select(f => $"{state}: {f}"));

        await Assertions.Expect(frame.GetByTestId("connections-key")).ToBeVisibleAsync();
        await Snapshot("arrived");

        await frame.GetByTestId("connections-pair").ClickAsync();
        await Assertions.Expect(frame.GetByTestId("connections-key-hash")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^pbkdf2-sha256"));
        await Snapshot("paired");

        await frame.GetByTestId("connections-jira-token").FillAsync(FakeJira.Token);
        await frame.GetByTestId("connections-jira-token").PressAsync("Tab");
        await Assertions.Expect(frame.GetByTestId("connections-jira-clear")).ToBeVisibleAsync();
        await Snapshot("token held");

        await App.Page.RouteAsync($"{_helper.BaseUrl}/api/connections/ado", route => route.FulfillAsync(new() { Status = 500 }));
        await frame.GetByTestId("connections-ado-base-url").FillAsync("https://ado.test/Coll");
        await frame.GetByTestId("connections-ado-base-url").PressAsync("Tab");
        await Assertions.Expect(frame.GetByTestId("connections-error")).ToBeVisibleAsync();
        await Snapshot("refused");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>The URL, the body and every header value: anywhere a token could ride along.</summary>
    private static string Describe(IRequest request) =>
        string.Join("\n", [request.Url, request.PostData ?? "", .. request.Headers.Values]);
}
