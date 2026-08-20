using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Jira;
using Todo.Core.Settings;

namespace Todo.Host.Endpoints;

public static class SettingsEndpoints
{
    private static readonly string[] SupportedLanguages = ["da", "en"];

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado) =>
            await ReadAllAsync(db, jira, ado))
        .WithName("getSettings")
        .WithTags("Settings")
        .Produces<SettingsResponse>();

        app.MapPut("/api/settings", async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
            SettingsRequest request, TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado) =>
        {
            // No language means "follow the system", which is a value in its own right and not English.
            if (request.Language is { } language && !SupportedLanguages.Contains(language))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsUnknownLanguage, $"'{language}' is not a supported language.");
            }

            // Rejected rather than folded, the same choice RetroEndpoints makes for aliases: a list
            // where two names became one without anybody saying so is worse than an error. It has to
            // run on the raw request, because SettingList.Write dedupes as well - after it there is
            // no duplicate left to report.
            if (DuplicateDelegate(request.Delegates) is { } duplicate)
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsDuplicateDelegate, $"'{duplicate}' is listed more than once.");
            }

            // A requirement rather than an optional filter, the same rule as the Jira project key: the
            // absence of a limit is not a neutral default, and here it would import the test artefacts
            // the filter exists to keep out. Null is absence, not emptiness - a full replacement reads
            // an absent field as clear, and clearing this one restores AdoDefaults.WorkItemTypes.
            // A list that is present but has nothing usable in it is an empty list, not an absent one.
            if (request.AdoWorkItemTypes is { } types && types.All(string.IsNullOrWhiteSpace))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.AdoWorkItemTypesRequired,
                    "At least one work item type is required. Clearing the list restores the default.");
            }

            // Both ends are refused rather than clamped, so a typo is visible instead of silently
            // becoming something else. 0 is not out of range: it means no deadline.
            if (request.AdoDefaultDeadlineDays is < 0 or > AdoDefaults.DeadlineDaysMax)
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.AdoDefaultDeadlineDaysInvalid,
                    $"A deadline of {request.AdoDefaultDeadlineDays} days ahead is outside "
                        + $"0-{AdoDefaults.DeadlineDaysMax}.");
            }

            await StoreAsync(db, SettingKeys.Language, request.Language);

            // An absent list clears the row, like every other field on this full replacement.
            await StoreAsync(db, SettingKeys.Delegates, SettingList.Write(request.Delegates ?? []));

            // A full replacement, so every field here is written from the request - and the token
            // is deliberately not one of them. It lives behind /api/settings/jira-token precisely
            // because an absent field means clear, and a language change must not wipe it.
            await StoreAsync(db, SettingKeys.JiraBaseUrl, BaseUrl(request.JiraBaseUrl));
            await StoreAsync(db, SettingKeys.JiraProjectKey, Blank(request.JiraProjectKey));
            await StoreAsync(
                db, SettingKeys.JiraWaitingStatuses, OrdinalNameList(request.JiraWaitingStatuses));
            await StoreAsync(db, SettingKeys.JiraDutyStatuses, OrdinalNameList(request.JiraDutyStatuses));

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

            // The ADO half. Its token is absent from this route for the same reason Jira's is, and it
            // lives behind /api/settings/ado-token below.
            await StoreAsync(db, SettingKeys.AdoBaseUrl, BaseUrl(request.AdoBaseUrl));
            await StoreAsync(db, SettingKeys.AdoProject, Blank(request.AdoProject));
            await StoreAsync(db, SettingKeys.AdoWaitingStates, OrdinalNameList(request.AdoWaitingStates));
            await StoreAsync(db, SettingKeys.AdoWorkItemTypes, OrdinalNameList(request.AdoWorkItemTypes));

            // Same asymmetry as the two Jira switches above, for the same reason, and read the same way
            // in AdoSettingsReader.
            await StoreAsync(db, SettingKeys.AdoIncludeWaiting, request.AdoIncludeWaiting ? "true" : null);

            // The default is the absence of a row, so asking for the default removes it. That is what
            // keeps a save that never mentioned ADO from adding a row - and a row is exactly what the
            // two whole-table tests in SettingsEndpointsTests go red on. It works because the contract
            // declares default: 3 on this field: NSwag turns that into a property initializer, which
            // the deserialiser leaves alone when the field is absent, so an absent field arrives as 3
            // and a deliberate 0 arrives as 0. Without the default both would arrive as 0, and a
            // language change would have turned the deadline off.
            await StoreAsync(
                db,
                SettingKeys.AdoDefaultDeadlineDays,
                request.AdoDefaultDeadlineDays == AdoDefaults.DeadlineDays
                    ? null
                    : request.AdoDefaultDeadlineDays.ToString(CultureInfo.InvariantCulture));

            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado));
        })
        .WithName("updateSettings")
        .WithTags("Settings");

        // The token has a route of its own because PUT /api/settings is a full replacement: were
        // the token a field on it, saving any other setting would clear the token.
        app.MapPut("/api/settings/jira-token",
            async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
                JiraTokenRequest request, TodoDbContext db, JiraSettingsReader jira,
                AdoSettingsReader ado) =>
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

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado));
        })
        .WithName("setJiraToken")
        .WithTags("Settings");

        app.MapDelete("/api/settings/jira-token",
            async (TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado) =>
        {
            await StoreAsync(db, SettingKeys.JiraToken, null);
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado));
        })
        .WithName("clearJiraToken")
        .WithTags("Settings");

        // A route of its own for the same reason Jira's token has one, written out again rather than
        // shared: one handler taking the key as a parameter would have to be reached through a route
        // template, and two literal routes are what the contract declares.
        app.MapPut("/api/settings/ado-token",
            async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
                AdoTokenRequest request, TodoDbContext db, JiraSettingsReader jira,
                AdoSettingsReader ado) =>
        {
            // The contract's [Required] is DataAnnotations, which System.Text.Json does not enforce
            // while deserialising, so this is the only actual validation.
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.SettingsEmptyToken, "A token cannot be blank.");
            }

            await StoreAsync(db, SettingKeys.AdoToken, request.Token.Trim());
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado));
        })
        .WithName("setAdoToken")
        .WithTags("Settings");

        app.MapDelete("/api/settings/ado-token",
            async (TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado) =>
        {
            await StoreAsync(db, SettingKeys.AdoToken, null);
            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado));
        })
        .WithName("clearAdoToken")
        .WithTags("Settings");

        return app;
    }

    /// <summary>
    /// Built in one place, so all six routes answer with the same shape - and so there is exactly
    /// one line to read when asking whether either token can get out.
    /// </summary>
    private static async Task<SettingsResponse> ReadAllAsync(
        TodoDbContext db, JiraSettingsReader reader, AdoSettingsReader adoReader)
    {
        var jira = await reader.ReadAsync();
        var ado = await adoReader.ReadAsync();

        return new SettingsResponse
        {
            Language = await ReadAsync(db, SettingKeys.Language),
            Delegates = [.. SettingList.Read(await ReadAsync(db, SettingKeys.Delegates))],
            JiraBaseUrl = jira.BaseUrl,
            JiraProjectKey = jira.ProjectKey,
            JiraWaitingStatuses = [.. jira.WaitingStatuses],
            JiraIncludeWaiting = jira.IncludeWaiting,
            JiraDutyStatuses = [.. jira.DutyStatuses],
            JiraOnDuty = jira.OnDuty,
            // The token itself is deliberately absent. Only whether there is one.
            HasJiraToken = jira.Token is not null,
            AdoBaseUrl = ado.BaseUrl,
            AdoProject = ado.Project,
            AdoWaitingStates = [.. ado.WaitingStates],
            AdoIncludeWaiting = ado.IncludeWaiting,
            // The effective list and the effective number, not the stored ones: the defaults live in
            // the reader, and the client has no second place to learn them from.
            AdoWorkItemTypes = [.. ado.WorkItemTypes],
            AdoDefaultDeadlineDays = ado.DefaultDeadlineDays,
            HasAdoToken = ado.Token is not null,
        };
    }

    /// <summary>A trailing slash here would make JiraSettings.BrowseUrl a double slash there.</summary>
    private static string? BaseUrl(string? value) => Blank(value)?.TrimEnd('/') is { Length: > 0 } url
        ? url
        : null;

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The first name that appears twice, or null. Case-insensitive and trimmed, so it sees the same
    /// duplicates SettingList.Write would silently fold - which is the whole point of asking before
    /// the write rather than after it. Blanks are skipped rather than reported: two empty rows in the
    /// editor are not a name listed twice, and the writer drops them anyway.
    /// </summary>
    private static string? DuplicateDelegate(IEnumerable<string?>? names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names ?? [])
        {
            var value = name?.Trim();

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!seen.Add(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// A list of names from a foreign system, as one row of JSON, with an empty list stored as no row
    /// at all. Four callers now: Jira's waiting and duty statuses, and ADO's waiting states and work
    /// item types. Renamed from StatusList when the fourth arrived, because a work item type is not a
    /// status and the old name had stopped describing what it holds.
    ///
    /// Deliberately not SettingList.Write, close as the two look: that one trims and dedupes
    /// case-insensitively, and these names are compared <em>ordinally</em> on purpose, because a
    /// case-insensitive fold would merge two states the foreign system keeps apart. Jira's trimming
    /// lives in JqlFor instead, where it protects the query and has tests of its own; ADO's belongs
    /// with whatever builds the WIQL, for the same reason.
    ///
    /// Note what this does <em>not</em> do: it does not reject an empty result. The work item types are
    /// a requirement, so their emptiness is refused earlier, on the raw request - after this there is
    /// no difference left between a list that was emptied and one that was never sent.
    /// </summary>
    private static string? OrdinalNameList(ICollection<string>? names)
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
