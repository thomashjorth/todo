using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.Host.Links;
using Todo.TestSupport.Links;

namespace Todo.Api.Tests;

public class SystemEndpointsTests : ApiTest
{
    private readonly RecordingLinkLauncher _launcher = new();

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<ILinkLauncher>(_launcher);

    [Theory]
    [InlineData("http://example.com/docs")]
    [InlineData("https://example.com/docs?q=1#top")]
    // Marked turns a bare address in a note into a mailto: link, so refusing it would make an
    // ordinary note report an error on a perfectly reasonable click.
    [InlineData("mailto:someone@example.com")]
    public async Task A_web_link_is_handed_to_the_launcher(string url)
    {
        var response = await OpenAsync(url);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(url, Assert.Single(_launcher.Opened).AbsoluteUri);
    }

    // UseShellExecute opens whatever the scheme is registered for, and a note is text somebody
    // else wrote, so anything off the list has to be turned away before it reaches the launcher.
    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("/api/tasks")]
    [InlineData("docs/index.html")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Anything_that_is_not_a_web_link_is_refused(string url)
    {
        var response = await OpenAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.SystemUnsupportedScheme, error?.Code);
        Assert.Empty(_launcher.Opened);
    }

    private Task<HttpResponseMessage> OpenAsync(string url)
        => Client.PostAsJsonAsync("/api/system/open-link", new OpenLinkRequest { Url = url });
}
