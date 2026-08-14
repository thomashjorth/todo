using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Errors;
using Todo.Core.Settings;

namespace Todo.Host.Endpoints;

public static class SettingsEndpoints
{
    private static readonly string[] SupportedLanguages = ["da", "en"];

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (TodoDbContext db) =>
            new SettingsResponse { Language = await ReadAsync(db, SettingKeys.Language) })
        .WithName("getSettings")
        .WithTags("Settings")
        .Produces<SettingsResponse>();

        app.MapPut("/api/settings", async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
            SettingsRequest request, TodoDbContext db) =>
        {
            // No language means "follow the system", which is a value in its own right and not English.
            if (request.Language is { } language && !SupportedLanguages.Contains(language))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsUnknownLanguage, $"'{language}' is not a supported language.");
            }

            await StoreAsync(db, SettingKeys.Language, request.Language);
            await db.SaveChangesAsync();

            return TypedResults.Ok(new SettingsResponse
            {
                Language = await ReadAsync(db, SettingKeys.Language),
            });
        })
        .WithName("updateSettings")
        .WithTags("Settings");

        return app;
    }

    private static async Task<string?> ReadAsync(TodoDbContext db, string key)
        => await db.Settings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync();

    private static async Task StoreAsync(TodoDbContext db, string key, string? value)
    {
        var stored = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);

        if (value is null)
        {
            if (stored is not null)
            {
                db.Settings.Remove(stored);
            }

            return;
        }

        if (stored is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            stored.Value = value;
        }
    }
}
