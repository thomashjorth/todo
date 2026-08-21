using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using Todo.Core.Ado;
using Todo.Core.Autostart;
using Todo.Core.Jira;
using Todo.Host.Ado;
using Todo.Host.Autostart;
using Todo.Host.Endpoints;
using Todo.Host.Jira;
using Todo.Host.Links;

namespace Todo.Host;

public static class TodoHost
{
    private const string ContractResourceName = "Todo.Host.openapi.yaml";

    /// <summary>
    /// The route the documentation page reads its document from. Outside <c>/api/</c> on purpose:
    /// it is documentation <em>of</em> the API, not part of it, and it is deliberately absent from
    /// contracts/openapi.yaml for the same reason.
    /// </summary>
    public const string ContractRoute = "/openapi/contract.yaml";

    /// <summary>
    /// Read once from the assembly, never from disk - the whole point of embedding it is that a
    /// published exe has no contracts/ folder beside it.
    /// </summary>
    private static readonly Lazy<string> Contract = new(ReadEmbeddedContract);

    /// <param name="configureServices">
    /// Runs after the app's own registrations, so the last word is the caller's. A test replaces
    /// the parts that reach outside the process with something that only records what it was asked.
    /// </param>
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = DefaultContentRoot(args),
        });

        if (builder.Configuration["urls"] is null)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }

        builder.Services.AddOpenApi();

        var databasePath = builder.Configuration["Data:Path"] ?? TodoDatabase.DefaultPath;
        builder.Services.AddDbContext<TodoDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddSingleton<IClock, SystemClock>();

        // Scoped, because it reads through the request's TodoDbContext. The settings routes take it
        // as a parameter, which is how a minimal API asks DI for something.
        builder.Services.AddScoped<JiraSettingsReader>();
        builder.Services.AddScoped<AdoSettingsReader>();

        // A typed client, so the source gets a pooled HttpClient rather than one per call. The
        // timeout is the point of configuring it at all: the app is a single window, and a Jira that
        // has stopped answering must not hold the UI open indefinitely — JiraTaskSource turns the
        // resulting cancellation into a JiraUnreachable the user can read.
        //
        // No BaseAddress. The Jira it talks to is a runtime setting the user can change, which is
        // also why the scoped JiraSettingsReader is a constructor dependency of the source.
        builder.Services.AddHttpClient<JiraTaskSource>(c => c.Timeout = TimeSpan.FromSeconds(30));

        // Its own typed client rather than a shared one, for the same reason the two sources are two
        // types: the timeout is a property of one external system's habits, and an Azure DevOps that
        // has stopped answering must not be told apart from a Jira that has.
        builder.Services.AddHttpClient<AdoTaskSource>(c => c.Timeout = TimeSpan.FromSeconds(30));

        builder.Services.AddSingleton<ILinkLauncher, ShellLinkLauncher>();

        // Registered behind the OS check the analyser asks for rather than unconditionally: the
        // registry APIs are annotated Windows-only, and the target framework is net10.0 rather than
        // net10.0-windows. The app is Windows-only either way - Photino and %APPDATA% both say so -
        // so the other branch is a courtesy to the compiler and to anyone who opens this on a Mac,
        // not a platform this ships to. It answers "off" and refuses to lie about turning on.
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton<IAutostart, RegistryAutostart>();
        }
        else
        {
            builder.Services.AddSingleton<IAutostart, UnsupportedAutostart>();
        }

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            TodoDatabase.PrepareAsync(db, databasePath).GetAwaiter().GetResult();
        }

        // The app serves two OpenAPI documents, each with a role of its own. /openapi/v1.json is
        // derived from the code and is therefore the truth about what is actually implemented -
        // ContractDriftTests reads exactly this one and holds it up against the contract. It must
        // not be removed.
        app.MapOpenApi();

        // The contract itself, which the documentation page reads. The shape is the same in both
        // documents (15 operations, 22 schemas), but the derivation has no prose: 0 of 15 operations
        // carry a summary, and the title comes out as "Todo.Host | v1" - built from the entry
        // assembly's name, so it reads differently under a test run. The contract has 29 description
        // fields, and the prose is precisely what one opens a documentation page to read.
        //
        // ExcludeFromDescription keeps this route out of /openapi/v1.json. Without it the derivation
        // describes itself, and ContractDriftTests fails with "GET /openapi/contract.yaml" in excess
        // - measured, not guessed. Living outside /api/ is not enough; ASP.NET Core puts every
        // minimal API in the document whatever the prefix.
        app.MapGet(ContractRoute, () => Results.Text(Contract.Value, "application/yaml", Encoding.UTF8))
            .ExcludeFromDescription();

        // The documentation page lives at /scalar/ - outside /api/, so it is not mistaken for the
        // app's own API. Scalar 2.16 ships its JavaScript bundle as an embedded resource in its own
        // assembly and serves it from /scalar/scalar.js, so the page works without a network. The
        // app may be offline; a bundle fetched from a CDN would give an empty page.
        //
        // There are two other places Scalar reaches outside, and each was found a different way.
        //
        // DisableDefaultFonts: Inter and JetBrains Mono are fetched from fonts.scalar.com through
        // @font-face in the served bundle. Without a network they fail silently and the page falls
        // back to the system font.
        //
        // DisableAgent: the "Ask AI" button looks itself up in Scalar's registry, so the page fetches
        // api.scalar.com/vector/registry/curated and /vector/registry/search?query= after render.
        // Neither call is visible in the HTML or in the bundle's text - they come from JavaScript
        // after mount, and ApiDocsJourneyTests found them by refusing everything that was not the
        // app's own origin. The button could not work here anyway: it talks to Scalar's service over
        // the network, and the app may be offline.
        //
        // OpenApiRoutePattern points the page at the contract rather than at /openapi/v1.json, which
        // is Scalar's default. The pattern has no {documentName} placeholder, so it stands as it is.
        app.MapScalarApiReference(options => options
            .DisableDefaultFonts()
            .DisableAgent()
            .WithOpenApiRoutePattern(ContractRoute));

        // All three read the frontend out of the assembly rather than off disk, and all three have
        // to be told: each one carries its own file provider, and one left on the default would
        // read the content root instead. That is not a hypothetical - a lookup that quietly falls
        // back to disk passes in development, where src\Todo.Host\wwwroot exists, and fails in the
        // published exe, where nothing does. Same shape as finding 5, one layer in.
        //
        // Two of the three are guarded by EmbeddedFrontendTests; the third is not. Putting
        // UseDefaultFiles back on the default fells nothing, because MapFallbackToFile already
        // answers "/". It keeps the provider anyway - the day the fallback moves or goes, this is
        // what serves the root - but nothing would catch it going wrong, which is worth knowing
        // rather than reading the symmetry as three tested call sites.
        var frontend = new ManifestEmbeddedFileProvider(typeof(TodoHost).Assembly, "wwwroot");

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontend });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = frontend });

        app.MapGet("/api/health", () => new HealthResponse
        {
            Status = "ok",
            Version = typeof(TodoHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        })
        .WithName("getHealth")
        .WithTags("Health")
        .Produces<HealthResponse>();

        app.MapTasks();
        app.MapRetro();
        app.MapJira();
        app.MapAdo();
        app.MapSettings();
        app.MapSystem();

        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = frontend });

        return app;
    }

    /// <summary>
    /// The content root to use when nobody has named one: the folder the exe lives in.
    /// </summary>
    /// <remarks>
    /// The framework's default is the process working directory, which a published exe does not
    /// control - whoever starts it does, and autostart is exactly such a caller. Measured on the
    /// published exe: run from the repository root, <c>/</c> answered 404 and the log said
    /// <c>The WebRootPath was not found: C:\privat-git\todo\wwwroot</c>; run from its own folder the
    /// same exe answered 200.
    /// <para>
    /// That 404 can no longer happen, and saying so is the honest version: the same slice went on to
    /// embed the frontend in the assembly, so static files do not come from the content root at all
    /// any more. This stays because the content root is still where the host would look for
    /// configuration beside the exe, and the exe's folder is the only answer that holds wherever the
    /// process was started from - which autostart decides, not the app.
    /// </para>
    /// <para>
    /// Returning <see langword="null"/> means "leave the default alone", and that is the point of
    /// asking first: <see cref="WebApplicationOptions.ContentRootPath"/> is applied on top of
    /// configuration, so setting it unconditionally would beat an explicit <c>--contentRoot</c>.
    /// Every test host passes one, pointing at src\Todo.Host. The three sources probed here are the
    /// three the host itself would read a content root from, in its own order of precedence.
    /// </para>
    /// </remarks>
    private static string? DefaultContentRoot(string[] args)
    {
        var named = new ConfigurationBuilder()
            .AddEnvironmentVariables("DOTNET_")
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddCommandLine(args)
            .Build()["contentRoot"];

        return named is null ? AppContext.BaseDirectory : null;
    }

    private static string ReadEmbeddedContract()
    {
        var assembly = typeof(TodoHost).Assembly;

        using var stream = assembly.GetManifestResourceStream(ContractResourceName)
            ?? throw new InvalidOperationException(
                $"'{ContractResourceName}' is not embedded in {assembly.GetName().Name}. "
                + "Todo.Host.csproj must include contracts/openapi.yaml as an EmbeddedResource "
                + $"with that LogicalName. Present: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
