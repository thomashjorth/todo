using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Persistence;
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

    /// <summary>
    /// Writes an arranged entity graph to the database in one call, bypassing the API.
    /// </summary>
    public async Task AddAndSaveChangesAsync(params object[] entities)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        db.AddRange(entities);

        await db.SaveChangesAsync();
    }

    /// <summary>The connection string the host builds for a database path.</summary>
    public static string ConnectionStringFor(string databasePath) => $"Data Source={databasePath}";

    public static Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "TodoApp.Tests", $"{Guid.NewGuid():N}.db");

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

        VerifyPoolKey(app, databasePath);

        var baseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new RunningHost(app, baseUrl, databasePath, ownsDatabase);
    }

    /// <summary>
    /// A pool is keyed by its connection string, so clearing the right one depends on
    /// <see cref="ConnectionStringFor"/> still spelling the database the way the host does.
    /// Drift would otherwise leave every test database behind without a word.
    /// </summary>
    private static void VerifyPoolKey(WebApplication app, string databasePath)
    {
        using var scope = app.Services.CreateScope();
        var actual = scope.ServiceProvider
            .GetRequiredService<TodoDbContext>()
            .Database.GetConnectionString();

        if (actual != ConnectionStringFor(databasePath))
        {
            throw new InvalidOperationException(
                $"The host connects with '{actual}', but this helper builds "
                + $"'{ConnectionStringFor(databasePath)}', which names a different pool.");
        }
    }

    /// <summary>
    /// Closes the pooled connections to one database, so its files can be deleted. Scoped to
    /// that database on purpose: ClearAllPools would also close connections that tests running
    /// in parallel are holding open, and closing the last connection to a SQLite database
    /// folds its write-ahead log into the database file behind their backs.
    /// </summary>
    public static void ClearConnectionPoolFor(string databasePath)
    {
        using var connection = new SqliteConnection(ConnectionStringFor(databasePath));

        SqliteConnection.ClearPool(connection);
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

        ClearConnectionPoolFor(_databasePath);

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
