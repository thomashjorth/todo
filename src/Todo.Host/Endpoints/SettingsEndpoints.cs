using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Errors;
using Todo.Core.Jira;
using Todo.Core.Settings;

namespace Todo.Host.Endpoints;

public static class SettingsEndpoints
{
    private static readonly string[] SupportedLanguages = ["da", "en"];

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (TodoDbContext db, JiraSettingsReader jira) =>
            await ReadAllAsync(db, jira))
        .WithName("getSettings")
        .WithTags("Settings")
        .Produces<SettingsResponse>();

        app.MapPut("/api/settings", async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
            SettingsRequest request, TodoDbContext db, JiraSettingsReader jira) =>
        {
            // No language means "follow the system", which is a value in its own right and not English.
            if (request.Language is { } language && !SupportedLanguages.Contains(language))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsUnknownLanguage, $"'{language}' is not a supported language.");
            }

            await StoreAsync(db, SettingKeys.Language, request.Language);

            // A full replacement, so every field here is written from the request - and the token
            // is deliberately not one of them. It lives behind /api/settings/jira-token precisely
            // because an absent field means clear, and a language change must not wipe it.
            await StoreAsync(db, SettingKeys.JiraBaseUrl, BaseUrl(request.JiraBaseUrl));
            await StoreAsync(db, SettingKeys.JiraProjectKey, Blank(request.JiraProjectKey));
            await StoreAsync(db, SettingKeys.JiraWaitingStatuses, WaitingStatuses(request.JiraWaitingStatuses));

            // Stored only when on, so the row set stays empty when nothing has been chosen -
            // storing "false" would leave a row behind for a setting that was never touched.
            await StoreAsync(db, SettingKeys.JiraIncludeWaiting, request.JiraIncludeWaiting ? "true" : null);

            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira));
        })
        .WithName("updateSettings")
        .WithTags("Settings");

        // The token has a route of its own because PUT /api/settings is a full replacement: were
        // the token a field on it, saving any other setting would clear the token.
        app.MapPut("/api/settings/jira-token",
            async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
                JiraTokenRequest request, TodoDbContext db, JiraSettingsReader jira) =>
        {
            // NSwag puts [Required] on Token, but that is DataAnnotations, which System.Text.Json
            // does not enforce while deserialising - and it was generated with AllowEmptyStrings.
            // The contract enforces nothing; this is the only actual validation.
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsEmptyToken, "A token cannot be blank.");
            }

            await StoreAsync(db, SettingKeys.JiraToken, request.Token.Trim());
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira));
        })
        .WithName("setJiraToken")
        .WithTags("Settings");

        app.MapDelete("/api/settings/jira-token", async (TodoDbContext db, JiraSettingsReader jira) =>
        {
            await StoreAsync(db, SettingKeys.JiraToken, null);
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira));
        })
        .WithName("clearJiraToken")
        .WithTags("Settings");

        return app;
    }

    /// <summary>
    /// Built in one place, so all four routes answer with the same shape - and so there is exactly
    /// one line to read when asking whether the token can get out.
    /// </summary>
    private static async Task<SettingsResponse> ReadAllAsync(TodoDbContext db, JiraSettingsReader reader)
    {
        var jira = await reader.ReadAsync();

        return new SettingsResponse
        {
            Language = await ReadAsync(db, SettingKeys.Language),
            JiraBaseUrl = jira.BaseUrl,
            JiraProjectKey = jira.ProjectKey,
            JiraWaitingStatuses = [.. jira.WaitingStatuses],
            JiraIncludeWaiting = jira.IncludeWaiting,
            // The token itself is deliberately absent. Only whether there is one.
            HasJiraToken = jira.Token is not null,
        };
    }

    /// <summary>A trailing slash here would make JiraSettings.BrowseUrl a double slash there.</summary>
    private static string? BaseUrl(string? value) => Blank(value)?.TrimEnd('/') is { Length: > 0 } url
        ? url
        : null;

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The list is one row of JSON, and an empty list is no row at all.</summary>
    private static string? WaitingStatuses(ICollection<string>? names)
    {
        string[] kept = [.. (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n))];

        return kept.Length == 0 ? null : JsonSerializer.Serialize(kept);
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
