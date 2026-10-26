using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;

namespace Todo.Helper.Tests;

/// <summary>
/// The real helper in this process, on a free port and with a settings file of its own - never the
/// user's in %APPDATA%\TodoHelper, which holds their real tokens.
/// </summary>
public sealed class RunningHelper : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _directory;

    private RunningHelper(WebApplication app, string directory, int port)
    {
        _app = app;
        _directory = directory;
        BaseUrl = $"http://127.0.0.1:{port}";
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Starts the helper with these settings already stored, as a user would have left them.</summary>
    public static async Task<RunningHelper> StartAsync(HelperSettings? settings = null)
    {
        var directory = Directory.CreateTempSubdirectory("TodoHelper.Tests.").FullName;
        var path = Path.Combine(directory, "helper.json");

        if (settings is not null)
        {
            new HelperStore(path).Update(_ => settings);
        }

        var port = FreePort();
        var app = HelperApp.Build(["--Helper:Port", port.ToString(), "--Helper:SettingsPath", path]);
        await app.StartAsync();

        return new RunningHelper(app, directory, port);
    }

    /// <summary>A port nothing listens on, for a target that must refuse - the user's real Ollama is on 11434.</summary>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
