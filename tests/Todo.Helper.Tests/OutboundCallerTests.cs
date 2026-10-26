using System.Net;
using System.Text;
using Todo.Tunnel;

namespace Todo.Helper.Tests;

/// <summary>
/// The call the helper makes for the host: its own Authorization header per target, the host's thrown
/// away first, and no call at all outside the allowlist.
/// </summary>
public class OutboundCallerTests : IDisposable
{
    private const string JiraToken = "a-jira-pat";
    private const string AdoToken = "an-ado-pat";

    private readonly string _directory = Directory.CreateTempSubdirectory("TodoHelper.Tests.").FullName;
    private readonly Recorder _recorder = new();
    private readonly OutboundCaller _caller;

    public OutboundCallerTests()
    {
        var store = new HelperStore(Path.Combine(_directory, "helper.json"));
        store.Update(_ => new HelperSettings
        {
            Jira = new SourceSettings { BaseUrl = "https://jira.example", Token = JiraToken },
            Ado = new SourceSettings { BaseUrl = "https://ado.example/Coll", Token = AdoToken },
            OllamaUrl = "http://127.0.0.1:11434",
        });

        _caller = new OutboundCaller(new HttpClient(_recorder), store);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Jira_gets_the_token_as_bearer()
    {
        await CallAsync("https://jira.example/rest/api/2/myself");

        Assert.Equal("Bearer", _recorder.Last!.Headers.Authorization!.Scheme);
        Assert.Equal(JiraToken, _recorder.Last.Headers.Authorization.Parameter);
    }

    // Basic with an empty user name, as measured against the user's own instance before 2.0.0.
    [Fact]
    public async Task Azure_devops_gets_the_token_as_basic_with_an_empty_user()
    {
        await CallAsync("https://ado.example/Coll/_apis/connectionData");

        Assert.Equal("Basic", _recorder.Last!.Headers.Authorization!.Scheme);
        Assert.Equal(
            $":{AdoToken}",
            Encoding.ASCII.GetString(Convert.FromBase64String(_recorder.Last.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task Ollama_gets_no_token()
    {
        await CallAsync("http://127.0.0.1:11434/api/tags");

        Assert.Null(_recorder.Last!.Headers.Authorization);
    }

    // The host has no token to send, so anything in this header came from something that is not the
    // host as we built it - and it must never decide what the helper sends.
    [Fact]
    public async Task An_authorization_header_from_the_host_is_thrown_away()
    {
        await CallAsync("http://127.0.0.1:11434/api/tags", new() { ["Authorization"] = ["Bearer from-the-host"] });

        Assert.Null(_recorder.Last!.Headers.Authorization);
    }

    [Fact]
    public async Task A_url_outside_the_allowlist_is_refused_without_a_call()
    {
        var answer = await CallAsync("https://intranet.example/secrets");

        Assert.Equal(0, answer.Status);
        Assert.Contains("not one of the helper's own targets", answer.Error);
        Assert.Null(_recorder.Last);
    }

    [Fact]
    public async Task The_answer_carries_the_status_the_headers_and_the_body()
    {
        _recorder.Answer = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{ "ok": true }""", Encoding.UTF8, "application/json"),
        };

        var answer = await CallAsync("https://jira.example/rest/api/2/issue");

        Assert.Equal(201, answer.Status);
        Assert.Equal("""{ "ok": true }""", Encoding.UTF8.GetString(Convert.FromBase64String(answer.Body!)));
        Assert.Contains("application/json", answer.Headers["Content-Type"][0]);
    }

    private Task<TunnelResponse> CallAsync(string url, Dictionary<string, string[]>? headers = null) =>
        _caller.CallAsync(new TunnelRequest("1", "GET", url, headers ?? [], Body: null), default);

    private sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public HttpResponseMessage Answer { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(Answer);
        }
    }
}
