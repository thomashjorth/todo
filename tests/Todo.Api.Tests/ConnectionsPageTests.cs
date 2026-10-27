using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.TestSupport;
using Todo.Tunnel;

namespace Todo.Api.Tests;

/// <summary>
/// The Connections page belongs to the helper on the PC. The host only says where it is - and never
/// serves it itself, because on the NAS's origin a token typed into it would go to the NAS.
/// </summary>
public class ConnectionsPageTests
{
    [Fact]
    public async Task The_settings_name_the_page_the_host_is_configured_with()
    {
        await using var host = await RunningHost.StartAsync("--Helper:PageUrl", "http://pc.example:47600/connections");

        var settings = await host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.Equal("http://pc.example:47600/connections", settings!.HelperPageUrl);
    }

    // Unconfigured, it is the helper on this machine - the PC the browser runs on, wherever the host is.
    [Fact]
    public async Task Without_configuration_the_page_is_the_helper_on_this_machine()
    {
        await using var host = await RunningHost.StartAsync("--Helper:PageUrl", "");

        var settings = await host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.Equal(HelperAddress.DefaultPageUrl, settings!.HelperPageUrl);
    }

    [Fact]
    public async Task The_host_never_serves_the_page_itself()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync("/connections");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
