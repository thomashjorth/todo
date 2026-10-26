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
using Todo.Core.Review;
using Todo.Core.Settings;
using Todo.Core.Sources;

namespace Todo.Host.Endpoints;

public static class SettingsEndpoints
{
    private static readonly string[] SupportedLanguages = ["da", "en"];

    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", async (
            TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado, ReviewSettingsReader review,
            ISourceConnections connections) =>
            await ReadAllAsync(db, jira, ado, review, connections))
        .WithName("getSettings")
        .WithTags("Settings")
        .Produces<SettingsResponse>();

        app.MapPut("/api/settings", async Task<Results<Ok<SettingsResponse>, BadRequest<ApiError>>> (
            SettingsRequest request, TodoDbContext db, JiraSettingsReader jira, AdoSettingsReader ado, ReviewSettingsReader review,
            ISourceConnections connections) =>
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

            // 0 is not out of range here: it is Sunday. Refused rather than clamped, so a typo is
            // visible instead of quietly moving the habit to the start of the week.
            if (request.ReviewWeekday is < 0 or > 6)
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.ReviewWeekdayInvalid,
                    $"{request.ReviewWeekday} is not a day of the week.");
            }

            if (!TimeOnly.TryParseExact(
                    request.ReviewTimeOfDay, ReviewDefaults.TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var reviewTime))
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.ReviewTimeOfDayInvalid,
                    $"'{request.ReviewTimeOfDay}' is not a time of day.");
            }

            await StoreAsync(db, SettingKeys.Language, request.Language);

            // An absent list clears the row, like every other field on this full replacement.
            await StoreAsync(db, SettingKeys.Delegates, SettingList.Write(request.Delegates ?? []));

            // A full replacement, so every field here is written from the request - and the base URL
            // and the token are deliberately not among them. The helper on the PC owns both: it
            // writes the base URL when it says hello and keeps the token to itself, so a save here
            // must not clear what the helper reported.
            await StoreAsync(db, SettingKeys.JiraProjectKey, Blank(request.JiraProjectKey));
            await StoreAsync(
                db, SettingKeys.JiraWaitingStatuses, OrdinalNameList(request.JiraWaitingStatuses));
            await StoreAsync(db, SettingKeys.JiraDutyStatuses, OrdinalNameList(request.JiraDutyStatuses));
            await StoreAsync(
                db, SettingKeys.JiraDoneStatuses, OrdinalNameList(request.JiraDoneStatuses));

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

            // The ADO half. Its collection URL and token are absent for the same reason as Jira's.
            await StoreAsync(db, SettingKeys.AdoProject, Blank(request.AdoProject));
            await StoreAsync(db, SettingKeys.AdoWaitingStates, OrdinalNameList(request.AdoWaitingStates));
            await StoreAsync(db, SettingKeys.AdoDoneStates, OrdinalNameList(request.AdoDoneStates));
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

            // The same rule as the line above, and the trap is sharper: 0 here is Sunday rather than
            // "off", so the contract's default: 5 is what keeps an absent field from moving the
            // review. Asking for the default removes the row, which is what stops a save that never
            // mentioned the review from leaving one behind - and a leftover row is exactly what the
            // two whole-table tests in SettingsEndpointsTests go red on.
            await StoreAsync(
                db,
                SettingKeys.ReviewWeekday,
                request.ReviewWeekday == (int)ReviewDefaults.Weekday
                    ? null
                    : request.ReviewWeekday.ToString(CultureInfo.InvariantCulture));

            // Compared as a parsed TimeOnly rather than as text, so "09:30" and a default written
            // some other way could never both be stored. The completion timestamp is deliberately
            // not here: it has its own route, see SettingKeys.ReviewLastCompletedAt.
            await StoreAsync(
                db,
                SettingKeys.ReviewTimeOfDay,
                reviewTime == ReviewDefaults.TimeOfDay
                    ? null
                    : reviewTime.ToString(ReviewDefaults.TimeFormat, CultureInfo.InvariantCulture));

            await db.SaveChangesAsync();

            return TypedResults.Ok(await ReadAllAsync(db, jira, ado, review, connections));
        })
        .WithName("updateSettings")
        .WithTags("Settings");

        return app;
    }

    /// <summary>
    /// Built in one place, so both routes answer with the same shape. No token can get out: the host
    /// has none to give.
    /// </summary>
    private static async Task<SettingsResponse> ReadAllAsync(
        TodoDbContext db,
        JiraSettingsReader reader,
        AdoSettingsReader adoReader,
        ReviewSettingsReader reviewReader,
        ISourceConnections connections)
    {
        var jira = await reader.ReadAsync();
        var ado = await adoReader.ReadAsync();
        var review = await reviewReader.ReadAsync();

        return new SettingsResponse
        {
            Language = await ReadAsync(db, SettingKeys.Language),
            Delegates = [.. SettingList.Read(await ReadAsync(db, SettingKeys.Delegates))],
            JiraBaseUrl = jira.BaseUrl,
            JiraProjectKey = jira.ProjectKey,
            JiraWaitingStatuses = [.. jira.WaitingStatuses],
            JiraIncludeWaiting = jira.IncludeWaiting,
            JiraDutyStatuses = [.. jira.DutyStatuses],
            JiraDoneStatuses = [.. jira.DoneStatuses],
            JiraOnDuty = jira.OnDuty,
            // Whether calls will carry a token - this process's own, or the helper's, which is the
            // only one there is outside the tests.
            HasJiraToken = jira.Token is not null || jira.AuthorizedElsewhere,
            AdoBaseUrl = ado.BaseUrl,
            AdoProject = ado.Project,
            AdoWaitingStates = [.. ado.WaitingStates],
            AdoDoneStates = [.. ado.DoneStates],
            AdoIncludeWaiting = ado.IncludeWaiting,
            // The effective list and the effective number, not the stored ones: the defaults live in
            // the reader, and the client has no second place to learn them from.
            AdoWorkItemTypes = [.. ado.WorkItemTypes],
            AdoDefaultDeadlineDays = ado.DefaultDeadlineDays,
            HasAdoToken = ado.Token is not null || ado.AuthorizedElsewhere,
            HelperOnline = connections.Online,
            // The effective values, not the stored ones: the defaults live in the reader, and the
            // client has no second place to learn them from. lastCompletedAt is deliberately not
            // here - it is not a choice, and this route would clear it.
            ReviewWeekday = (int)review.Weekday,
            ReviewTimeOfDay = review.TimeOfDay.ToString(ReviewDefaults.TimeFormat, CultureInfo.InvariantCulture),
        };
    }

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
