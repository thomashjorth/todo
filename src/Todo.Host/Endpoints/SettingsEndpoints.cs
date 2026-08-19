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
            await StoreAsync(db, SettingKeys.JiraWaitingStatuses, StatusList(request.JiraWaitingStatuses));
            await StoreAsync(db, SettingKeys.JiraDutyStatuses, StatusList(request.JiraDutyStatuses));

            // Stored only when on. Not tidiness - two tests in SettingsEndpointsTests assert about
            // the whole Settings table and go red on a "false" row left behind by a save that never
            // touched this setting: Clearing_the_language_removes_the_row_rather_than_storing_null
            // (Assert.Empty) and Choosing_a_language_twice_overwrites_the_one_row (Assert.Single).
            // They live in a file about language, so nothing there points back here.
            // The asymmetry this creates is read in JiraSettingsReader, and turning it back off
            // has its own test, JiraSettingsEndpointsTests.Turning_waiting_back_off_turns_it_off.
            await StoreAsync(db, SettingKeys.JiraIncludeWaiting, request.JiraIncludeWaiting ? "true" : null);

            // Same shape as the line above, for the same reason - a literal "false" row would fell
            // the same two language tests. The duty switch is separate from the waiting one, and the
            // list is stored whether or not the switch is on: the list has to survive a rotation
            // ending, or the user would clear it to go off duty and re-pick it a week later.
            await StoreAsync(db, SettingKeys.JiraOnDuty, request.JiraOnDuty ? "true" : null);

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
            JiraDutyStatuses = [.. jira.DutyStatuses],
            JiraOnDuty = jira.OnDuty,
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

    /// <summary>
    /// A status list is one row of JSON, and an empty list is no row at all. Shared by the waiting
    /// list and the duty list, which are two lists with one storage shape.
    /// </summary>
    private static string? StatusList(ICollection<string>? names)
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
