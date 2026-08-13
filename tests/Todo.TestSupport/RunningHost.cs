using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Todo.Host;

namespace Todo.TestSupport;

/// <summary>
/// Starts the real host in-process on a free loopback port. Tests talk to it over real HTTP,
/// so they exercise the same startup path as the shipped app.
/// </summary>
public sealed class RunningHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RunningHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
        Client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    public static async Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", RepoPaths.HostContentRoot,
            .. extraArgs
        ];

        var app = TodoHost.Build(args);
        await app.StartAsync();

        var baseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new RunningHost(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
