using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Host.Endpoints;

namespace Todo.Host;

public static class TodoHost
{
    public static WebApplication Build(string[] args)
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

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            TodoDatabase.PrepareAsync(db, databasePath).GetAwaiter().GetResult();
        }

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

        app.MapTasks();
        app.MapRetro();
        app.MapSettings();

        app.MapFallbackToFile("index.html");

        return app;
    }
}
