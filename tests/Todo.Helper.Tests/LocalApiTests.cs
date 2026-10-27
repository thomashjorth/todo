using System.Net;
using System.Net.Http.Json;

namespace Todo.Helper.Tests;

/// <summary>
/// The helper's own API, which can change where the user's token is sent - so it answers only a caller
/// that sends its header from this machine's own name, and it never hands a token back.
/// </summary>
public class LocalApiTests : IAsyncLifetime
{
    private RunningHelper _helper = null!;

    public async ValueTask InitializeAsync() => _helper = await RunningHelper.StartAsync();

    public async ValueTask DisposeAsync() => await _helper.DisposeAsync();

    [Fact]
    public async Task A_call_without_the_header_is_refused()
    {
        var response = await _helper.Client.GetAsync("/api/connections");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // DNS rebinding: a foreign site's name pointed at 127.0.0.1 still sends that name as the Host.
    [Fact]
    public async Task A_call_naming_another_host_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/connections");
        request.Headers.Add(LocalApi.RequiredHeader, "1");
        request.Headers.Host = "evil.example";

        var response = await _helper.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_stored_token_shows_as_held_and_never_comes_back()
    {
        var stored = await SendAsync(HttpMethod.Put, "/api/connections/jira",
            new { baseUrl = "https://jira.example/", token = "a-jira-pat" });

        stored.EnsureSuccessStatusCode();
        var body = await stored.Content.ReadAsStringAsync();

        Assert.DoesNotContain("a-jira-pat", body);

        var status = await stored.Content.ReadFromJsonAsync<ConnectionsStatus>();
        Assert.True(status!.Jira.HasToken);
        Assert.Equal("https://jira.example", status.Jira.BaseUrl);
    }

    // The UI's token field is write-only, so a save that sends none keeps the one there is.
    [Fact]
    public async Task A_save_without_a_token_keeps_the_one_held_and_clearing_removes_it()
    {
        await SendAsync(HttpMethod.Put, "/api/connections/ado", new { baseUrl = "https://ado.example/Coll", token = "an-ado-pat" });

        var kept = await (await SendAsync(HttpMethod.Put, "/api/connections/ado", new { baseUrl = "https://ado.example/Other" }))
            .Content.ReadFromJsonAsync<ConnectionsStatus>();

        Assert.True(kept!.Ado.HasToken);
        Assert.Equal("https://ado.example/Other", kept.Ado.BaseUrl);

        var cleared = await (await SendAsync(HttpMethod.Put, "/api/connections/ado", new { baseUrl = "https://ado.example/Other", clearToken = true }))
            .Content.ReadFromJsonAsync<ConnectionsStatus>();

        Assert.False(cleared!.Ado.HasToken);
    }

    // The Connections page reads this by hand, so the names are pinned on the raw body rather than read
    // back through the record that wrote them.
    [Fact]
    public async Task The_status_wire_format_uses_camel_case_names()
    {
        await SendAsync(HttpMethod.Put, "/api/connections/jira", new { baseUrl = "https://jira.example", token = "a-jira-pat" });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/connections");
        request.Headers.Add(LocalApi.RequiredHeader, "1");
        var body = await (await _helper.Client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("\"serverUrl\":null", body);
        Assert.Contains("\"hasKey\":false", body);
        Assert.Contains("\"tunnel\":\"NotConfigured\"", body);
        Assert.Contains("\"refusal\":null", body);
        Assert.Contains("\"jira\":{\"baseUrl\":\"https://jira.example\",\"hasToken\":true}", body);
        Assert.Contains("\"ado\":{\"baseUrl\":null,\"hasToken\":false}", body);
        Assert.Contains("\"ollamaUrl\":", body);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(LocalApi.RequiredHeader, "1");

        return _helper.Client.SendAsync(request);
    }
}
