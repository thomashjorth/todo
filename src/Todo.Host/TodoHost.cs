using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;

namespace Todo.Host;

public static class TodoHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.MapOpenApi();
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

        app.MapFallbackToFile("index.html");

        return app;
    }
}
