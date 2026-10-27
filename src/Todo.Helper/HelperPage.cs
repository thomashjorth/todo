using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace Todo.Helper;

/// <summary>
/// The Connections page: the app's own bundle, carried in the helper's exe, with main.ts starting the
/// second root on <see cref="Path"/>. The app's settings page shows it in an iframe.
/// </summary>
public static class HelperPage
{
    public const string Path = "/connections";

    private const string ManifestName = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";

    public static void UseHelperPage(this WebApplication app)
    {
        // With only .gitkeep in the host's wwwroot the build makes no manifest and merely warns, and the
        // provider would then throw at startup with a message about a missing resource. Said by name
        // instead, with the cure.
        var assembly = typeof(HelperPage).Assembly;
        if (!assembly.GetManifestResourceNames().Contains(ManifestName))
        {
            throw new InvalidOperationException(
                "The helper was built without the app's bundle, so it has no Connections page to serve. "
                + @"Run scripts\build-web.ps1, then build the helper again.");
        }

        var bundle = new ManifestEmbeddedFileProvider(assembly, "wwwroot");

        // Who may show the page in a frame: the server the helper is paired with, once it knows one. Until
        // then anyone, because the first setup happens precisely inside the app's iframe, before the
        // helper has been told where the app lives. The page holds nothing to steal in that state - it
        // can only be typed into, and the local API refuses every call that is not from this machine.
        app.Use(async (context, next) =>
        {
            var store = context.RequestServices.GetRequiredService<HelperStore>();
            context.Response.Headers.ContentSecurityPolicy = $"frame-ancestors {FrameAncestor(store.Current.ServerUrl)}";
            await next(context);
        });

        app.UseStaticFiles(new StaticFileOptions { FileProvider = bundle });

        app.MapGet("/", () => Results.Redirect(Path)).ExcludeFromDescription();
        app.MapFallbackToFile(Path, "index.html", new StaticFileOptions { FileProvider = bundle });
    }

    /// <summary>The origin of the server URL, or <c>*</c> while there is none to name.</summary>
    public static string FrameAncestor(string? serverUrl) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out var url) ? url.GetLeftPart(UriPartial.Authority) : "*";
}
