using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.TestSupport;
using Todo.Tunnel;

namespace Todo.Api.Tests;

/// <summary>
/// The host's half of the tunnel, measured against a helper played by the test over a real WebSocket:
/// who may connect, what the hello does, and what a call through it looks like on the helper's side.
/// The host runs in Helper mode here, the app's own, rather than the Direct mode every other test uses.
/// </summary>
public class HelperTunnelTests : IAsyncLifetime
{
    private const string Key = "the-helpers-key";

    private RunningHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await RunningHost.StartAsync(
        "--Sources:Mode", "Helper",
        "--Helper:KeyHash", PasswordHash.Create(Key, iterations: 1));

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-key")]
    public async Task A_helper_without_the_key_is_turned_away(string? key)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;

        if (key is not null)
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {key}");
        }

        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(TunnelUri(), default));

        Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);
    }

    [Fact]
    public async Task A_helper_on_another_protocol_version_is_refused_and_told_why()
    {
        using var socket = await OpenAsync();

        await TunnelProtocol.SendAsync(socket, Hello() with { Version = TunnelProtocol.Version + 1 }, default);

        var refused = Assert.IsType<TunnelRefused>(await TunnelProtocol.ReceiveAsync(socket, default));
        Assert.Contains("Update the helper", refused.Reason);
        Assert.False((await SettingsAsync()).HelperOnline);
    }

    /// <summary>
    /// The hello is the whole of what the host learns: whether the helper is there, which tokens it
    /// holds - never the tokens - and the base URLs, kept as rows with the trailing slash trimmed.
    /// </summary>
    [Fact]
    public async Task The_hello_puts_the_helper_online_and_stores_what_it_reports()
    {
        Assert.False((await SettingsAsync()).HelperOnline);

        await using var helper = await FakeHelper.ConnectAsync(this, Hello());

        var settings = await SettingsAsync();

        Assert.True(settings.HelperOnline);
        Assert.True(settings.HasJiraToken);
        Assert.False(settings.HasAdoToken);
        Assert.Equal("https://jira.example", settings.JiraBaseUrl);
    }

    /// <summary>
    /// A call through the tunnel, seen from the helper's side: the URL and the method the source asked
    /// for, and no Authorization header - the host has no token to put there, and the helper sets its
    /// own. The answer travels back and becomes the source's answer.
    /// </summary>
    [Fact]
    public async Task A_jira_call_reaches_the_helper_without_a_token_and_its_answer_comes_back()
    {
        await using var helper = await FakeHelper.ConnectAsync(this, Hello());

        var call = _host.Client.PostAsync("/api/jira/test", null);
        var request = await helper.NextRequestAsync();

        Assert.Equal("GET", request.Method);
        Assert.Equal("https://jira.example/rest/api/2/myself", request.Url);
        Assert.DoesNotContain(request.Headers.Keys, name => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase));

        await helper.AnswerAsync(request, 200, """{ "displayName": "Thomas Hjorth Hansen" }""");

        var response = await call;
        response.EnsureSuccessStatusCode();
        Assert.Contains("Thomas Hjorth Hansen", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_a_helper_jira_says_the_helper_is_offline()
    {
        var response = await _host.Client.PostAsync("/api/jira/test", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.HelperOffline, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// A helper that goes away mid-call fails the call at once rather than after the client's 30 second
    /// timeout - which is the limit this test runs well inside of.
    /// </summary>
    [Fact]
    public async Task A_helper_that_goes_away_mid_call_fails_the_call_at_once()
    {
        var helper = await FakeHelper.ConnectAsync(this, Hello());

        var call = _host.Client.PostAsync("/api/jira/test", null);
        await helper.NextRequestAsync();
        await helper.DisposeAsync();

        var response = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.HelperOffline, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task Without_a_helper_the_model_is_not_ready()
    {
        var status = await _host.Client.GetFromJsonAsync<AiStatusResponse>("/api/ai/status");

        Assert.False(status!.Ready);
    }

    private static TunnelHello Hello() => new(
        TunnelProtocol.Version,
        JiraBaseUrl: "https://jira.example/",
        HasJiraToken: true,
        AdoBaseUrl: null,
        HasAdoToken: false);

    private Uri TunnelUri() => new($"{_host.BaseUrl.Replace("http://", "ws://")}{TunnelProtocol.Path}");

    private async Task<ClientWebSocket> OpenAsync()
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {Key}");
        await socket.ConnectAsync(TunnelUri(), default);

        return socket;
    }

    private async Task<SettingsResponse> SettingsAsync() =>
        (await _host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!;

    /// <summary>The helper, played by the test: it says hello, then answers what it is asked.</summary>
    private sealed class FakeHelper(ClientWebSocket socket) : IAsyncDisposable
    {
        public static async Task<FakeHelper> ConnectAsync(HelperTunnelTests test, TunnelHello hello)
        {
            var socket = await test.OpenAsync();
            await TunnelProtocol.SendAsync(socket, hello, default);
            Assert.IsType<TunnelAccepted>(await TunnelProtocol.ReceiveAsync(socket, default));

            return new FakeHelper(socket);
        }

        public async Task<TunnelRequest> NextRequestAsync() =>
            Assert.IsType<TunnelRequest>(
                await TunnelProtocol.ReceiveAsync(socket, default).WaitAsync(TimeSpan.FromSeconds(10)));

        public Task AnswerAsync(TunnelRequest request, int status, string json) => TunnelProtocol.SendAsync(
            socket,
            new TunnelResponse(
                request.Id,
                status,
                new() { ["Content-Type"] = ["application/json"] },
                Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                Error: null),
            default);

        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", default);
            }

            socket.Dispose();
        }
    }
}
