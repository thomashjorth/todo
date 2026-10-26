using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todo.Helper;

/// <summary>
/// The helper, built the way <c>TodoHost.Build</c> builds the host: one place, so a test can start the
/// real thing in its own process with its own settings file and port.
/// </summary>
public static class HelperApp
{
    /// <summary>Fixed, because the app's settings page embeds the helper's page from this address.</summary>
    public const int DefaultPort = 47600;

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Loopback only: the local API changes where the user's tokens go, and nothing outside this
        // machine has any business reaching it.
        var port = builder.Configuration.GetValue("Helper:Port", DefaultPort);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var settingsPath = builder.Configuration["Helper:SettingsPath"] ?? HelperStore.DefaultPath;
        builder.Services.AddSingleton(new HelperStore(settingsPath));

        // Sixty seconds, as the host's longest client - the model's first answer after a cold start.
        builder.Services.AddHttpClient<OutboundCaller>(c => c.Timeout = TimeSpan.FromSeconds(60));

        builder.Services.AddSingleton<TunnelClient>();
        builder.Services.AddHostedService(services => services.GetRequiredService<TunnelClient>());

        var app = builder.Build();
        app.MapLocalApi();

        return app;
    }
}
