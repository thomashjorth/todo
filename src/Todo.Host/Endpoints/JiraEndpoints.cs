using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Errors;
using Todo.Core.Jira;
using Todo.Core.Sources;
using Todo.Host.Jira;
using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Host.Endpoints;

public static class JiraEndpoints
{
    private const int TitleMaxLength = 500;

    public static IEndpointRouteBuilder MapJira(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/jira/test", async Task<Results<Ok<JiraConnectionResponse>, BadRequest<ApiError>>> (
            JiraSettingsReader reader, JiraTaskSource source) =>
        {
            var settings = await reader.ReadAsync();

            if (NotConfigured(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                var identity = await source.TestAsync();

                return TypedResults.Ok(new JiraConnectionResponse { DisplayName = identity.DisplayName });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("testJiraConnection")
        .WithTags("Jira")
        .Produces<JiraConnectionResponse>();

        app.MapGet("/api/jira/statuses", async Task<Results<Ok<JiraStatusesResponse>, BadRequest<ApiError>>> (
            JiraSettingsReader reader, JiraTaskSource source) =>
        {
            var settings = await reader.ReadAsync();

            if (NotConfigured(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                return TypedResults.Ok(new JiraStatusesResponse
                {
                    Names = [.. await source.FetchStatusNamesAsync()],
                });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("listJiraStatuses")
        .WithTags("Jira")
        .Produces<JiraStatusesResponse>();

        app.MapPost("/api/jira/preview", async Task<Results<Ok<JiraPreviewResponse>, BadRequest<ApiError>>> (
            JiraSettingsReader reader, JiraTaskSource source, TodoDbContext db) =>
        {
            var settings = await reader.ReadAsync();

            if (NotReady(settings) is { } refusal)
            {
                return refusal;
            }

            try
            {
                var page = await source.FetchAssignedAsync();
                var imported = await ImportedKeysAsync(db, [.. page.Items.Select(item => item.Key)]);
                var rows = new List<JiraPreviewRow>();

                foreach (var item in page.Items)
                {
                    // One rule, in Todo.Core, because this decision is taken twice — here and in the
                    // import below — and two places is two places the duty branch can be forgotten.
                    var role = JiraStatusRoles.For(item.StatusName, settings);
                    var isWaiting = role == JiraStatusRole.Waiting;

                    rows.Add(new JiraPreviewRow
                    {
                        Key = item.Key,
                        Title = item.Title,
                        // Computed here rather than in the frontend, so /browse/ is spelled in one
                        // place — the same decision as TodoTask.externalUrl, and never stored, so it
                        // follows a changed base URL. The throw stands in for a null-forgiving `!`
                        // because the assumption is worth writing down: NotReady above has already
                        // established IsConfigured, which requires a callable base URL, so a null
                        // here would be a fault in our own ordering rather than in the user's data.
                        Url = settings.BrowseUrl(item.Key)
                            ?? throw new SourceException(
                                ErrorCodes.JiraNotConfigured,
                                "A preview row has no browse URL, which cannot happen once IsConfigured has passed."),
                        Note = item.Note,
                        Deadline = item.Deadline,
                        Requester = item.Requester,
                        Status = item.StatusName,
                        IsWaiting = isWaiting,
                        // A status in both lists while the rotation is on: the issue waits for the
                        // pool, the user is the pool, so it waits for them. Labelled rather than
                        // hidden, and never waiting.
                        IsDuty = role == JiraStatusRole.Duty,
                        // Only for the rows that have a wait to date. This is one HTTP call to Jira
                        // per issue, so asking for every row would multiply the preview's cost by
                        // the size of the page for answers nothing would show.
                        WaitingSince = isWaiting
                            ? AsUtc(await source.FetchStatusChangedAtAsync(item.Key))
                            : null,
                        AlreadyImported = imported.Contains(item.Key),
                        Excluded = isWaiting && !settings.IncludeWaiting
                            ? ErrorCodes.JiraExcludedWaiting
                            : null,
                    });
                }

                return TypedResults.Ok(new JiraPreviewResponse { Rows = rows, Total = page.Total });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("previewJira")
        .WithTags("Jira")
        .Produces<JiraPreviewResponse>();

        // Deliberately does not call Jira. It writes the rows the client sends, the same contract
        // slice 2's retro import has: what the user saw in the preview is what gets written, and a
        // confirmed import does not wait for the instance a second time.
        app.MapPost("/api/jira/import", async Task<Results<Ok<JiraImportResponse>, BadRequest<ApiError>>> (
            JiraImportRequest request, JiraSettingsReader reader, TodoDbContext db, IClock clock) =>
        {
            var settings = await reader.ReadAsync();

            if (NotReady(settings) is { } refusal)
            {
                return refusal;
            }

            var rows = request.Rows ?? [];

            foreach (var row in rows)
            {
                if (ValidateRow(row) is { } invalid)
                {
                    return invalid;
                }
            }

            var known = await ImportedKeysAsync(db, [.. rows.Select(row => row.Key)]);
            var imported = 0;
            var skipped = 0;

            foreach (var row in rows)
            {
                // Re-derived here rather than taken from the body, which is why the row carries
                // Jira's status name and not the decision: both lists and the duty switch live on
                // the server, so the settings as they stand now decide, even if the preview ran
                // under an older one — including a rotation that has ended since.
                var role = JiraStatusRoles.For(row.Status, settings);
                var isWaiting = role == JiraStatusRole.Waiting;

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
                    SourceId = JiraTaskSource.Id,
                    ExternalKey = row.Key,
                    Title = row.Title.Trim(),
                    Note = row.Note,
                    Deadline = row.Deadline,
                    Requester = row.Requester,
                    // Only Waiting parks the task. A duty row arrives Open on purpose: imported as
                    // WaitingFor it would land in "Venter på", away from the deadline sections —
                    // hiding exactly the work the user holds the duty for.
                    Status = isWaiting ? CoreStatus.WaitingFor : CoreStatus.Open,
                    // WaitingOn is left unset on purpose. An issue assigned to you that sits in a
                    // waiting status is waiting on somebody who is not in the assignee field, so
                    // there is nobody the app could name here without inventing one.
                    WaitingSince = isWaiting ? row.WaitingSince?.UtcDateTime : null,
                    CreatedAt = clock.UtcNow,
                });

                imported++;
            }

            await db.SaveChangesAsync();

            return TypedResults.Ok(new JiraImportResponse { Imported = imported, Skipped = skipped });
        })
        .WithName("importJira")
        .WithTags("Jira")
        .Produces<JiraImportResponse>();

        return app;
    }

    /// <summary>
    /// A 400 rather than a 500, and caught at the edge of each route that reached outside the
    /// process. Deliberately not an IExceptionHandler: a global one would also turn a
    /// SourceException thrown by something that never called out into a bad request, and the reason
    /// this is a 400 at all is that the request named an unreachable Jira.
    /// </summary>
    private static BadRequest<ApiError> Refused(SourceException exception)
        => ApiErrors.BadRequest(exception.Code, exception.Message);

    private static BadRequest<ApiError>? NotConfigured(JiraSettings settings) => settings.IsConfigured
        ? null
        : ApiErrors.BadRequest(
            ErrorCodes.JiraNotConfigured,
            "Jira needs a base URL and a token before it can be asked anything.");

    /// <summary>
    /// The project key is checked here, before the source is touched, and that is the whole point:
    /// the token can see several projects including a customer's, so a missing key must refuse
    /// rather than let a JQL without a project clause fetch everything assigned to the user.
    /// </summary>
    private static BadRequest<ApiError>? NotReady(JiraSettings settings)
        => NotConfigured(settings)
            ?? (string.IsNullOrWhiteSpace(settings.ProjectKey)
                ? ApiErrors.BadRequest(
                    ErrorCodes.JiraProjectKeyRequired,
                    "A Jira project key is needed, so the import cannot widen to every project.")
                : null);

    private static BadRequest<ApiError>? ValidateRow(JiraImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.JiraRowKeyRequired, "Every row needs a key.");
        }

        if (string.IsNullOrWhiteSpace(row.Title))
        {
            return ApiErrors.BadRequest(ErrorCodes.JiraRowTitleRequired, "Every row needs a title.");
        }

        if (row.Title.Length > TitleMaxLength)
        {
            return ApiErrors.BadRequest(
                ErrorCodes.JiraRowTitleTooLong,
                $"A row title may be at most {TitleMaxLength} characters.");
        }

        // The status is what waiting-ness is derived from, and an absent string is null - which is
        // exactly why the contract carries the status name rather than an isWaiting boolean. An
        // absent bool would arrive as false, a legal value nothing here could refuse.
        return string.IsNullOrWhiteSpace(row.Status)
            ? ApiErrors.BadRequest(
                ErrorCodes.JiraRowStatusRequired, "Every row needs its Jira status name.")
            : null;
    }

    /// <summary>
    /// Scoped to this source. A retro card and a Jira issue can carry the same key, and one of them
    /// counting as the other would hide a real issue behind a card that has nothing to do with it.
    /// </summary>
    private static async Task<HashSet<string>> ImportedKeysAsync(TodoDbContext db, List<string> keys)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == JiraTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .Select(t => t.ExternalKey!)
            .ToListAsync();

        return new HashSet<string>(found, StringComparer.Ordinal);
    }

    // The source hands over UTC, because SQLite cannot sort a DateTimeOffset and one must never
    // reach the entity. The contract's field is a DateTimeOffset, so the zero offset is put back on
    // at the edge rather than left for the reader to assume.
    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } moment
        ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
        : null;
}
