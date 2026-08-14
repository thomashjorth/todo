using System.Drawing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;

namespace Todo.Host;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // A bare "--headless" is not valid command-line configuration syntax, so it is
        // stripped before the arguments reach the host builder.
        var headless = args.Contains("--headless", StringComparer.OrdinalIgnoreCase);
        var hostArgs = args.Where(a =>
            !string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase)).ToArray();

        var app = TodoHost.Build(hostArgs);

        if (headless)
        {
            app.Run();
            return;
        }

        app.Start();

        var url = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        new PhotinoWindow()
            .SetTitle("Mandalorian ToDo")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "icon.ico"))
            .SetUseOsDefaultSize(false)
            // The app lives in a quarter-width column on a 1080p screen, not a wide window.
            .SetSize(new Size(480, 1000))
            .Center()
            .Load(new Uri(url))
            .WaitForClose();

        app.StopAsync().GetAwaiter().GetResult();
    }
}
