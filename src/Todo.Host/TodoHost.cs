using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;
using Todo.Host.Endpoints;
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
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Configuration["urls"] is null)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }

        builder.Services.AddOpenApi();

        var databasePath = builder.Configuration["Data:Path"] ?? TodoDatabase.DefaultPath;
        builder.Services.AddDbContext<TodoDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddSingleton<IClock, SystemClock>();
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
        // DisableDefaultFonts er det eneste sted Scalar ellers rækker udenfor: Inter og
        // JetBrains Mono hentes fra fonts.scalar.com. Uden netværk ville de fejle stille og
        // siden falde tilbage på systemfonten — men så er der ingen fremmede kald tilbage.
        //
        // OpenApiRoutePattern peger siden på kontrakten frem for på /openapi/v1.json, som er
        // Scalars standard. Mønstret har intet {documentName}-pladsholder, så det står som det er.
        app.MapScalarApiReference(options => options
            .DisableDefaultFonts()
            .WithOpenApiRoutePattern(ContractRoute));

        app.UseDefaultFiles();
        app.UseStaticFiles();

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
        app.MapSettings();
        app.MapSystem();

        app.MapFallbackToFile("index.html");

        return app;
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
