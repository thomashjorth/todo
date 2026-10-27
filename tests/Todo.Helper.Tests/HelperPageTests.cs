using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Todo.Tunnel;

namespace Todo.Helper.Tests;

/// <summary>
/// The Connections page comes out of the helper's own exe - never from the NAS - and says who may frame
/// it. And pairing makes a key that stays here, handing out only its hash.
/// </summary>
public partial class HelperPageTests : IAsyncLifetime
{
    private RunningHelper _helper = null!;

    public async ValueTask InitializeAsync() => _helper = await RunningHelper.StartAsync();

    public async ValueTask DisposeAsync() => await _helper.DisposeAsync();

    [Fact]
    public async Task The_page_and_its_scripts_are_served_from_the_helper()
    {
        var page = await _helper.Client.GetAsync(HelperPage.Path);

        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<app-root>", html);

        // The hashed bundle the page names, so the test cannot pass on an index.html without its code.
        var script = MainScript().Match(html);
        Assert.True(script.Success, "index.html names no main-*.js.");
        (await _helper.Client.GetAsync($"/{script.Value}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_root_leads_to_the_page()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(_helper.BaseUrl),
        };

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HelperPage.Path, response.Headers.Location?.OriginalString);
    }

    // Only the one page: the app's other routes belong to the app, and served from here they would be
    // an app without its server.
    [Fact]
    public async Task Another_route_of_the_app_is_not_found()
    {
        var response = await _helper.Client.GetAsync("/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_may_frame_the_page_until_the_server_is_known_and_then_only_the_server()
    {
        var before = await _helper.Client.GetAsync(HelperPage.Path);
        Assert.Equal("frame-ancestors *", string.Join(";", before.Headers.GetValues("Content-Security-Policy")));

        await SendAsync(HttpMethod.Put, "/api/server", new { url = "http://nas.local:8080/" });

        var after = await _helper.Client.GetAsync(HelperPage.Path);
        Assert.Equal("frame-ancestors http://nas.local:8080",
            string.Join(";", after.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Pairing_keeps_the_key_and_hands_out_only_a_hash_of_it()
    {
        var first = await (await SendAsync(HttpMethod.Post, "/api/server/pair", new { }))
            .Content.ReadFromJsonAsync<PairResponse>();

        var key = _helper.Services.GetRequiredService<HelperStore>().Current.HelperKey;
        Assert.False(string.IsNullOrEmpty(key));
        Assert.DoesNotContain(key, first!.KeyHash);
        Assert.True(PasswordHash.Verify(key, first.KeyHash));

        // Again, and the old hash no longer opens the tunnel.
        await SendAsync(HttpMethod.Post, "/api/server/pair", new { });
        var newKey = _helper.Services.GetRequiredService<HelperStore>().Current.HelperKey!;
        Assert.False(PasswordHash.Verify(newKey, first.KeyHash));
    }

    [Fact]
    public async Task Pairing_without_the_header_is_refused()
    {
        var response = await _helper.Client.PostAsync("/api/server/pair", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(_helper.Services.GetRequiredService<HelperStore>().Current.HelperKey);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(LocalApi.RequiredHeader, "1");

        return _helper.Client.SendAsync(request);
    }

    [GeneratedRegex(@"main-[A-Z0-9]+\.js")]
    private static partial Regex MainScript();
}
