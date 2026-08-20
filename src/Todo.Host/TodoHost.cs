using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using Todo.Core.Ado;
using Todo.Core.Jira;
using Todo.Host.Ado;
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

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            TodoDatabase.PrepareAsync(db, databasePath).GetAwaiter().GetResult();
        }

        // Appen servérer to OpenAPI-dokumenter med hver sin rolle. /openapi/v1.json er afledt af
        // koden og er dermed sandheden om hvad der faktisk er implementeret — ContractDriftTests
        // læser netop den og holder den op mod kontrakten. Den må ikke fjernes.
        app.MapOpenApi();

        // Kontrakten selv, som dokumentationssiden læser. Formen er den samme i de to dokumenter
        // (15 operationer, 22 skemaer), men afledningen har ingen prosa: 0 af 15 operationer har
        // en summary, og titlen bliver "Todo.Host | v1" — dannet af entry-assemblyens navn, så den
        // hedder noget andet under en testkørsel. Kontrakten har 29 description-felter, og prosaen
        // er lige præcis det man åbner en dokumentationsside for at læse.
        // ExcludeFromDescription holder ruten ude af /openapi/v1.json. Uden den beskriver
        // afledningen sig selv, og ContractDriftTests fejler med "GET /openapi/contract.yaml" i
        // overskud — målt, ikke gættet. At ruten ligger uden for /api/ er ikke nok; ASP.NET Core
        // tager hver minimal API med i dokumentet uanset præfiks.
        app.MapGet(ContractRoute, () => Results.Text(Contract.Value, "application/yaml", Encoding.UTF8))
            .ExcludeFromDescription();

        // Dokumentationssiden ligger på /scalar/ — uden for /api/, så den ikke forveksles med
        // appens eget API. Scalar 2.16 lægger sin JavaScript-bundle som embedded resource i sin
        // egen assembly og servérer den fra /scalar/scalar.js, så siden virker uden netværk.
        // Appen kan være offline; en CDN-hentet bundle ville give en tom side.
        //
        // Der er to steder Scalar ellers rækker udenfor, og de blev fundet på hver sin måde.
        //
        // DisableDefaultFonts: Inter og JetBrains Mono hentes fra fonts.scalar.com gennem
        // @font-face i den serverede bundle. Uden netværk fejler de stille, og siden falder tilbage
        // på systemfonten.
        //
        // DisableAgent: "Ask AI"-knappen slår op i Scalars registry, så siden henter
        // api.scalar.com/vector/registry/curated og /vector/registry/search?query= efter render.
        // De to kald kan man ikke se i HTML'en eller i bundlens tekst — de kommer fra JavaScript
        // efter mount, og ApiDocsJourneyTests fandt dem ved at afvise alt der ikke var appens egen
        // origin. Knappen ville ikke kunne virke her alligevel: den taler med Scalars tjeneste over
        // netværket, og appen kan være offline.
        //
        // OpenApiRoutePattern peger siden på kontrakten frem for på /openapi/v1.json, som er
        // Scalars standard. Mønstret har intet {documentName}-pladsholder, så det står som det er.
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
    /// same exe answered 200. wwwroot is published beside the exe, so the exe's folder is the one
    /// answer that holds wherever the process is started from.
    /// <para>
    /// Returning <see langword="null"/> means "leave the default alone", and that is the point of
    /// asking first: <see cref="WebApplicationOptions.ContentRootPath"/> is applied on top of
    /// configuration, so setting it unconditionally would beat an explicit <c>--contentRoot</c>.
    /// Every test host passes one, pointing at src\Todo.Host where wwwroot actually lives - the
    /// test binary's own folder has none. The three sources probed here are the three the host
    /// itself would read a content root from, in its own order of precedence.
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
