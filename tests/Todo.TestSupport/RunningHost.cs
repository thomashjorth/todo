using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
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
    private readonly string _databasePath;

    private RunningHost(WebApplication app, string baseUrl, string databasePath)
    {
        _app = app;
        BaseUrl = baseUrl;
        _databasePath = databasePath;
        Client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    public static async Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "EdoraTodo.Tests", $"{Guid.NewGuid():N}.db");

        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", RepoPaths.HostContentRoot,
            "--Data:Path", databasePath,
            .. extraArgs
        ];

        var app = TodoHost.Build(args);
        await app.StartAsync();

        var baseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new RunningHost(app, baseUrl, databasePath);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();

        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
