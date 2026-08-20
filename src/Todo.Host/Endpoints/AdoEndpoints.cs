using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.Host.Ado;
using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Host.Endpoints;

/// <summary>
/// The four Azure DevOps routes, mirroring JiraEndpoints. Where they differ from Jira's, the reason is
/// written down at the difference rather than here - slice 12 exists to find out where the two sources
/// stop looking alike, and a summary would flatten that back out.
/// </summary>
public static class AdoEndpoints
{
    private const int TitleMaxLength = 500;

    public static IEndpointRouteBuilder MapAdo(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ado/test", async Task<Results<Ok<AdoConnectionResponse>, BadRequest<ApiError>>> (
            AdoSettingsReader reader, AdoTaskSource source) =>
        {
            var settings = await reader.ReadAsync();

            // NotConfigured and not NotReady, unlike every other route here: TestAsync calls
            // _apis/connectionData at collection level and never touches ProjectOf, so refusing for a
            // blank project would send the user to fill in a field this request does not use. That is
            // one place Jira's shape did fit - its own test route asks the same little.
            if (NotConfigured(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                var identity = await source.TestAsync();

                return TypedResults.Ok(new AdoConnectionResponse { DisplayName = identity.DisplayName });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("testAdoConnection")
        .WithTags("Ado")
        .Produces<AdoConnectionResponse>();

        // The states come off the user's own work items rather than off _apis/wit/workitemtypes, and
        // that is measured rather than chosen: measurement 0d only asked that endpoint for name and
        // referenceName, so whether it carries a states array on this server is unknown, and binding a
        // field nobody has seen is CLAUDE.md's duedate lesson. The price is user-visible and belongs in
        // the release note rather than only here: a state none of your work items is in right now
        // cannot be offered, so Blocked cannot be marked as waiting on a day when nothing is blocked.
        app.MapGet("/api/ado/states", async Task<Results<Ok<AdoStatesResponse>, BadRequest<ApiError>>> (
            AdoSettingsReader reader, AdoTaskSource source) =>
        {
            var settings = await reader.ReadAsync();

            // Unlike Jira's status list, this one needs the project: Azure DevOps scopes a WIQL by URL
            // path, so the query cannot be asked at all without one.
            if (NotReady(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                return TypedResults.Ok(new AdoStatesResponse
                {
                    Names = [.. await source.FetchStatusNamesAsync()],
                });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("listAdoStates")
        .WithTags("Ado")
        .Produces<AdoStatesResponse>();

        app.MapPost("/api/ado/preview", async Task<Results<Ok<AdoPreviewResponse>, BadRequest<ApiError>>> (
            AdoSettingsReader reader, AdoTaskSource source, TodoDbContext db) =>
        {
            var settings = await reader.ReadAsync();

            // No work item type check here on purpose, though the import has one. The source refuses an
            // empty type filter before its first request and has its own test for it, so a guard here
            // could not be seen to fail: with it or without it, no WIQL goes out and the answer carries
            // the same code. The import has nowhere else to refuse from, which is why it does.
            if (NotReady(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                var page = await source.FetchAssignedAsync();
                var imported = await ImportedKeysAsync(db, [.. page.Items.Select(item => item.Key)]);
                var rows = new List<AdoPreviewRow>();

                foreach (var item in page.Items)
                {
                    // One rule, in Todo.Core, because this decision is taken twice - here and in the
                    // import below - and two places is two places it can be forgotten.
                    var isWaiting = AdoStateRoles.IsWaiting(item.StatusName, settings);

                    rows.Add(new AdoPreviewRow
                    {
                        Key = item.Key,
                        Title = item.Title,
                        // Computed here rather than in the frontend, so the work item route is spelled
                        // in one place - the same decision as TodoTask.externalUrl, and never stored,
                        // so it follows a changed collection URL. The throw stands in for a
                        // null-forgiving `!` because the assumption is worth writing down: BrowseUrl
                        // needs the collection URL and the project, and NotReady above has established
                        // both - so a null here would be a fault in our own ordering rather than in the
                        // user's data. Left as a silent empty string it would ship a row whose button
                        // goes nowhere, and `url` is required precisely so no @if branch has to exist
                        // for that.
                        Url = settings.BrowseUrl(item.Key)
                            ?? throw new SourceException(
                                ErrorCodes.AdoNotConfigured,
                                "A preview row has no work item URL, which cannot happen once the "
                                    + "collection and the project have both been checked."),
                        // Azure DevOps' own HTML, not CommonMark yet, and the contract's description
                        // still says otherwise - a known, owned deviation, see the plan's task 3. The
                        // converter waits for a measured sample and for slice 13's comment HTML.
                        Note = item.Note,
                        // Already derived by the source from the clock and adoDefaultDeadlineDays,
                        // because Azure DevOps has no due date field at all. The client shows it and
                        // does not send it back; the import derives it again - decision A.
                        Deadline = item.Deadline,
                        Requester = item.Requester,
                        State = item.StatusName,
                        // Null only for a source that reports no type, and Jira is that source. Here
                        // the WIQL matched on System.WorkItemType and the batch asked for it, so a null
                        // is unreachable - which is why this is an empty string rather than a branch.
                        WorkItemType = item.ItemType ?? string.Empty,
                        IsWaiting = isWaiting,
                        // Read off the row rather than fetched. Microsoft.VSTS.Common.StateChangeDate
                        // arrives with the work item, so the whole page costs no extra round trip -
                        // where Jira pays one changelog call per waiting issue.
                        //
                        // ITaskSource documents a fallback to FetchStatusChangedAtAsync when this is
                        // null, and it is deliberately not taken here: for this source the fallback
                        // reads the very same field through the very same parse, so it could only
                        // answer null a second time - one wasted round trip for every row whose
                        // timestamp was unreadable. The fallback is for a source with no such field.
                        WaitingSince = isWaiting ? AsUtc(item.StatusChangedAt) : null,
                        AlreadyImported = imported.Contains(item.Key),
                        Excluded = isWaiting && !settings.IncludeWaiting
                            ? ErrorCodes.AdoExcludedWaiting
                            : null,
                    });
                }

                return TypedResults.Ok(new AdoPreviewResponse { Rows = rows, Total = page.Total });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("previewAdo")
        .WithTags("Ado")
        .Produces<AdoPreviewResponse>();

        // Deliberately does not call Azure DevOps, the same contract Jira's import and the retro import
        // have: what the user saw in the preview is what gets written.
        app.MapPost("/api/ado/import", async Task<Results<Ok<AdoImportResponse>, BadRequest<ApiError>>> (
            AdoImportRequest request, AdoSettingsReader reader, TodoDbContext db, IClock clock) =>
        {
            var settings = await reader.ReadAsync();

            if (NotReady(settings) is { } refusal)
            {
                return refusal;
            }

            // The one guard the preview does not repeat. Nothing else on this path can refuse an empty
            // type filter, because nothing here builds a query - so read as "every type" it would let
            // the test artefacts decision B exists to keep out straight through.
            if (NoWorkItemTypes(settings) is { } typeless)
            {
                return typeless;
            }

            var rows = request.Rows ?? [];

            foreach (var row in rows)
            {
                if (ValidateRow(row) is { } invalid)
                {
                    return invalid;
                }
            }

            // Derived once from the clock rather than taken from the row, which is why AdoImportRow
            // carries no deadline: the date is relative to the import, so previewing today and
            // importing tomorrow gives tomorrow's arithmetic - decision A, and it is the intended
            // outcome rather than a rounding error.
            var deadline = AdoDeadline.For(clock.Today, settings.DefaultDeadlineDays);

            var known = await ImportedKeysAsync(db, [.. rows.Select(row => row.Key)]);
            var imported = 0;
            var skipped = 0;

            foreach (var row in rows)
            {
                // Applied again rather than trusted, which is the whole reason the row carries its
                // type: the client could have previewed under an older filter, or not be our client at
                // all.
                if (!AdoWorkItemTypes.Allows(row.WorkItemType, settings))
                {
                    skipped++;
                    continue;
                }

                // Re-derived here rather than taken from the body, which is why the row carries Azure
                // DevOps' state name and not the decision: the list and the switch live on the server,
                // so the settings as they stand now decide, even if the preview ran under older ones.
                var isWaiting = AdoStateRoles.IsWaiting(row.State, settings);

                if (isWaiting && !settings.IncludeWaiting)
                {
                    skipped++;
                    continue;
                }

                if (!known.Add(row.Key))
                {
                    skipped++;
                    continue;
                }

                db.Tasks.Add(new TaskItem
                {
                    SourceId = AdoTaskSource.Id,
                    ExternalKey = row.Key,
                    Title = row.Title.Trim(),
                    Note = row.Note,
                    Deadline = deadline,
                    Requester = row.Requester,
                    Status = isWaiting ? CoreStatus.WaitingFor : CoreStatus.Open,
                    // WaitingOn is left unset on purpose. A work item assigned to you that sits in a
                    // waiting state is waiting on somebody who is not in the AssignedTo field, so there
                    // is nobody the app could name here without inventing one.
                    WaitingSince = isWaiting ? row.WaitingSince?.UtcDateTime : null,
                    CreatedAt = clock.UtcNow,
                });

                imported++;
            }

            await db.SaveChangesAsync();

            return TypedResults.Ok(new AdoImportResponse { Imported = imported, Skipped = skipped });
        })
        .WithName("importAdo")
        .WithTags("Ado")
        .Produces<AdoImportResponse>();

        return app;
    }

    /// <summary>
    /// A 400 rather than a 500, caught at the edge of each route that reached outside the process - the
    /// same shape and the same reason as JiraEndpoints.Refused: a global handler would also turn a
    /// SourceException thrown by something that never called out into a bad request.
    /// </summary>
    private static BadRequest<ApiError> Refused(SourceException exception)
        => ApiErrors.BadRequest(exception.Code, exception.Message);

    private static BadRequest<ApiError>? NotConfigured(AdoSettings settings) => settings.IsConfigured
        ? null
        : ApiErrors.BadRequest(
            ErrorCodes.AdoNotConfigured,
            "Azure DevOps needs a collection URL and a token before it can be asked anything.");

    /// <summary>
    /// The project is checked here, before the source is touched, and it is its own refusal rather than
    /// part of IsConfigured so the user is told which field is blank. Azure DevOps needs it for a
    /// different reason than Jira did, though the refusal looks the same: Jira's project key narrowed a
    /// JQL that would otherwise have reached a customer's project, while here the project is a path
    /// segment and there is no query to widen - without it the request has nowhere to go.
    /// </summary>
    private static BadRequest<ApiError>? NotReady(AdoSettings settings)
        => NotConfigured(settings)
            ?? (string.IsNullOrWhiteSpace(settings.Project)
                ? ApiErrors.BadRequest(
                    ErrorCodes.AdoProjectRequired,
                    "An Azure DevOps project is needed, because it is what scopes the query.")
                : null);

    private static BadRequest<ApiError>? NoWorkItemTypes(AdoSettings settings)
        => AdoWorkItemTypes.Effective(settings).Count == 0
            ? ApiErrors.BadRequest(
                ErrorCodes.AdoWorkItemTypesRequired,
                "At least one work item type is needed, or the import would take everything.")
            : null;

    private static BadRequest<ApiError>? ValidateRow(AdoImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.AdoRowKeyRequired, "Every row needs a key.");
        }

        if (string.IsNullOrWhiteSpace(row.Title))
        {
            return ApiErrors.BadRequest(ErrorCodes.AdoRowTitleRequired, "Every row needs a title.");
        }

        if (row.Title.Length > TitleMaxLength)
        {
            return ApiErrors.BadRequest(
                ErrorCodes.AdoRowTitleTooLong,
                $"A row title may be at most {TitleMaxLength} characters.");
        }

        // The state is what waiting-ness is derived from, and the type is what the filter is applied
        // to - so both are facts the row has to carry, and both are refused when absent. An absent
        // string is null and can be refused; slice 11 measured that an absent bool arrives as false,
        // which is a legal value nothing could refuse, and that is why neither of these is a decision.
        if (string.IsNullOrWhiteSpace(row.State))
        {
            return ApiErrors.BadRequest(
                ErrorCodes.AdoRowStateRequired, "Every row needs its Azure DevOps state name.");
        }

        return string.IsNullOrWhiteSpace(row.WorkItemType)
            ? ApiErrors.BadRequest(
                ErrorCodes.AdoRowWorkItemTypeRequired, "Every row needs its work item type.")
            : null;
    }

    /// <summary>
    /// Scoped to this source. A Jira issue, a retro card and an Azure DevOps work item can all carry
    /// the same key - a work item id is a bare number, so the collision is likelier here than it was
    /// for Jira - and one counting as another would hide real work behind something unrelated.
    /// </summary>
    private static async Task<HashSet<string>> ImportedKeysAsync(TodoDbContext db, List<string> keys)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == AdoTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .Select(t => t.ExternalKey!)
            .ToListAsync();

        return new HashSet<string>(found, StringComparer.Ordinal);
    }

    // The source hands over UTC, because SQLite cannot sort a DateTimeOffset and one must never reach
    // the entity. The contract's field is a DateTimeOffset, so the zero offset is put back on at the
    // edge rather than left for the reader to assume.
    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } moment
        ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
        : null;
}
