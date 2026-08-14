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
    private readonly bool _ownsDatabase;

    private RunningHost(WebApplication app, string baseUrl, string databasePath, bool ownsDatabase)
    {
        _app = app;
        BaseUrl = baseUrl;
        _databasePath = databasePath;
        _ownsDatabase = ownsDatabase;
        Client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    /// <summary>Lets a test read the database directly, not only through the API.</summary>
    public IServiceProvider Services => _app.Services;

    public static Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "EdoraTodo.Tests", $"{Guid.NewGuid():N}.db");

        return StartAsync(databasePath, ownsDatabase: true, extraArgs);
    }

    /// <summary>
    /// Starts against a database the test owns, so the file and anything beside it survive
    /// disposal and a second host can be started on top of it.
    /// </summary>
    public static Task<RunningHost> StartAtAsync(string databasePath, params string[] extraArgs) =>
        StartAsync(databasePath, ownsDatabase: false, extraArgs);

    private static async Task<RunningHost> StartAsync(
        string databasePath, bool ownsDatabase, string[] extraArgs)
    {
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

        return new RunningHost(app, baseUrl, databasePath, ownsDatabase);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();

        if (!_ownsDatabase)
        {
            // Leave the pooled connections open, the way the shipped app leaves them when its
            // process ends: SQLite only folds the write-ahead log back into the database file
            // when the last connection is closed, and a caller-owned database is usually being
            // handed to a second host that should meet it in exactly that state.
            return;
        }

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
