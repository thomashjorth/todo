using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.Host.Links;
using Todo.TestSupport;
using Todo.TestSupport.Links;

namespace Todo.Api.Tests;

public class SystemEndpointsTests
{
    [Theory]
    [InlineData("http://example.com/docs")]
    [InlineData("https://example.com/docs?q=1#top")]
    public async Task A_web_link_is_handed_to_the_launcher(string url)
    {
        var launcher = new RecordingLinkLauncher();
        await using var host = await StartAsync(launcher);

        var response = await OpenAsync(host, url);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(url, Assert.Single(launcher.Opened).AbsoluteUri);
    }

    // UseShellExecute opens whatever the scheme is registered for, and a note is text somebody
    // else wrote, so anything but the web has to be turned away before it reaches the launcher.
    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("/api/tasks")]
    [InlineData("docs/index.html")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Anything_that_is_not_a_web_link_is_refused(string url)
    {
        var launcher = new RecordingLinkLauncher();
        await using var host = await StartAsync(launcher);

        var response = await OpenAsync(host, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.SystemUnsupportedScheme, error?.Code);
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public async Task The_shipped_app_launches_links_through_the_shell()
    {
        await using var host = await RunningHost.StartAsync();

        using var scope = host.Services.CreateScope();

        Assert.IsType<ShellLinkLauncher>(scope.ServiceProvider.GetRequiredService<ILinkLauncher>());
    }

    private static Task<RunningHost> StartAsync(RecordingLinkLauncher launcher)
        => RunningHost.StartWithAsync(
            services => services.AddSingleton<ILinkLauncher>(launcher));

    private static Task<HttpResponseMessage> OpenAsync(RunningHost host, string url)
        => host.Client.PostAsJsonAsync("/api/system/open-link", new OpenLinkRequest { Url = url });
}
