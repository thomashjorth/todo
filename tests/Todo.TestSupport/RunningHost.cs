using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Todo.Contracts;
using Todo.Tunnel;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Sources;
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

    private RunningHost(
        WebApplication app,
        string baseUrl,
        string databasePath,
        bool ownsDatabase,
        FakeSourceConnections connections)
    {
        Connections = connections;
        _app = app;
        BaseUrl = baseUrl;
        _databasePath = databasePath;
        _ownsDatabase = ownsDatabase;

        // A cookie container of its own, so the login in StartAsync rides along on every call a test
        // makes through Client - which is how the whole suite runs the login path without a test
        // saying so.
        Client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(baseUrl),
        };
    }

    /// <summary>A client with no cookie, for the claims about what answers without one.</summary>
    public HttpClient AnonymousClient() => new() { BaseAddress = new Uri(BaseUrl) };

    private async Task LogInAsync()
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest { Password = TestPassword });

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"The test host refused its own test password: {(int)response.StatusCode} "
                + $"{await response.Content.ReadAsStringAsync()}. A test that needs another hash "
                + "has to build the host itself rather than pass one through extraArgs.");
        }
    }

    private static string KeysPathFor(string databasePath) => $"{databasePath}.keys";

    /// <summary>The password every test host is started with.</summary>
    public const string TestPassword = "test-password";

    /// <summary>
    /// One iteration rather than the app's 600,000: the hash format carries its own count, and a
    /// suite that logs in once per host would otherwise spend most of a minute on key stretching.
    /// </summary>
    public static readonly string TestPasswordHash = PasswordHash.Create(TestPassword, iterations: 1);

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

    /// <summary>
    /// The tests' stand-in for the helper: online, and holding whatever token a test hands it.
    /// </summary>
    public FakeSourceConnections Connections { get; }

    /// <summary>
    /// Points the host at a Jira and gives it the token to call it with. One place for every test that
    /// needs Jira set up, so where the base URL and the token live is this method's business and not
    /// a dozen tests': the base URL is a row, as the helper's hello writes it, and the token is held in
    /// memory and never stored, as the helper holds it.
    /// </summary>
    public async Task ConnectJiraAsync(string baseUrl, string token)
    {
        await StoreAsync(SettingKeys.JiraBaseUrl, baseUrl);
        Connections.Jira = new SourceConnection(token, AuthorizedElsewhere: false);
    }

    /// <summary>The same for an Azure DevOps collection.</summary>
    public async Task ConnectAdoAsync(string baseUrl, string token)
    {
        await StoreAsync(SettingKeys.AdoBaseUrl, baseUrl);
        Connections.Ado = new SourceConnection(token, AuthorizedElsewhere: false);
    }

    private async Task StoreAsync(string key, string value)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        db.Settings.RemoveRange(db.Settings.Where(s => s.Key == key));
        db.Settings.Add(new Setting { Key = key, Value = value });

        await db.SaveChangesAsync();
    }

    /// <summary>The connection string the host builds for a database path.</summary>
    public static string ConnectionStringFor(string databasePath) => $"Data Source={databasePath}";

    public static Task<RunningHost> StartAsync(params string[] extraArgs)
        => StartWithAsync(configureServices: null, extraArgs);

    /// <summary>
    /// Starts the host with one of its registrations replaced, so a test can stand in for the
    /// parts that reach outside the process.
    /// </summary>
    public static Task<RunningHost> StartWithAsync(
        Action<IServiceCollection>? configureServices, params string[] extraArgs)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "TodoApp.Tests", $"{Guid.NewGuid():N}.db");

        return StartAsync(databasePath, ownsDatabase: true, configureServices, extraArgs);
    }

    /// <summary>
    /// Starts against a database the test owns, so the file and anything beside it survive
    /// disposal and a second host can be started on top of it.
    /// </summary>
    public static Task<RunningHost> StartAtAsync(string databasePath, params string[] extraArgs) =>
        StartAsync(databasePath, ownsDatabase: false, configureServices: null, extraArgs);

    private static async Task<RunningHost> StartAsync(
        string databasePath,
        bool ownsDatabase,
        Action<IServiceCollection>? configureServices,
        string[] extraArgs)
    {
        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", RepoPaths.HostContentRoot,
            "--Data:Path", databasePath,

            // Always given, never left to the environment: once the user has run SetPassword.cmd,
            // their own Auth__PasswordHash is in every process this suite starts, and a host that
            // read it would refuse the test password - on that machine only.
            "--Auth:PasswordHash", TestPasswordHash,

            // Beside the database rather than the default folder beside it, which for every test
            // would be the one shared TodoApp.Tests folder. Deleted with the database.
            "--Data:KeysPath", KeysPathFor(databasePath),

            // The tests' own mode: calls leave the host directly, with the token FakeSourceConnections
            // hands over. The app's default is through the helper, which a test opts into by naming it
            // again in extraArgs.
            "--Sources:Mode", "Direct",

            // Before extraArgs, so a test's own --Ai:BaseUrl wins: the command-line provider keeps
            // the last value for a repeated key.
            "--Ai:BaseUrl", ClosedLoopbackUrl(),

            // And for the same reason the Connections page: the default is the user's real helper on
            // 47600, which a test that opens the settings group would otherwise frame - with the user's
            // real connections in it.
            "--Helper:PageUrl", $"{ClosedLoopbackUrl()}/connections",
            .. extraArgs
        ];

        // Registered before the test's own configureServices, so a test can still replace it.
        var connections = new FakeSourceConnections();

        var app = TodoHost.Build(args, services =>
        {
            // TryAdd, so a host started in Helper mode keeps the helper's own connections.
            services.TryAddSingleton<ISourceConnections>(connections);
            configureServices?.Invoke(services);
        });
        await app.StartAsync();

        VerifyPoolKey(app, databasePath);

        var baseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        var host = new RunningHost(app, baseUrl, databasePath, ownsDatabase, connections);
        await host.LogInAsync();

        return host;
    }

    /// <summary>
    /// An address nothing listens on, so no test host can reach the user's real Ollama, which
    /// answers on the default address on the machine the suite runs on. Asked for rather than
    /// hard-coded, because a fixed port can be taken on somebody's machine. Measured 2026-10-22: a
    /// refused connect on loopback takes under 1 ms on Windows, so this costs the suite nothing.
    /// </summary>
    private static string ClosedLoopbackUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return $"http://127.0.0.1:{port}";
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

        // A folder, so File.Delete below would leave it: the host writes a key file into it when it
        // starts.
        try
        {
            Directory.Delete(KeysPathFor(_databasePath), recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

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
