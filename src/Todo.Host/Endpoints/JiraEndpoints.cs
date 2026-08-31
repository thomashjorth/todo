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

                    // Three facts, combined here rather than shipped separately: the lists live on
                    // the server, so the decision belongs to the server, and the import takes it
                    // again. The local status is what stops the offer once it has been accepted.
                    var localStatus = imported.TryGetValue(item.Key, out var found)
                        ? (CoreStatus?)found
                        : null;
                    var suggestsClosing = role == JiraStatusRole.Done
                        && localStatus is { } status
                        && status != CoreStatus.Done;

                    // The mirror image of suggestsClosing, on the same two facts with each of them
                    // negated: the source is not finished, and the local task is. No reconciliation is
                    // needed for this half, because the rows it speaks about are the ones the fetch
                    // has already handed over — they stand here as alreadyImported with nothing to do
                    // until this rule speaks. Stated that way round on purpose: the converse would not
                    // hold, since the JQL asks for resolution = Unresolved, which is the resolution
                    // field and not the status - so an issue moved back to "I gang" without its
                    // resolution being cleared is out of reach here. That hole is named in the
                    // design's section 8 and left open there.
                    //
                    // IncludeWaiting is deliberately not asked, though every other waiting branch on
                    // this route asks it: that toggle governs what comes in as new work, and a task the
                    // user already owns is not held back by a filter for new ones.
                    var suggestsReopening = role != JiraStatusRole.Done && localStatus is CoreStatus.Done;

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
                        AlreadyImported = localStatus is not null,
                        SuggestsClosing = suggestsClosing,
                        // Same bargain as WaitingSince above and the same price: one changelog call,
                        // and only for the rows that will show the answer. Its own field rather than
                        // a loosened WaitingSince, which is null for every row that is not waiting -
                        // and a finished row never is.
                        DoneAt = suggestsClosing
                            ? AsUtc(await source.FetchStatusChangedAtAsync(item.Key))
                            : null,
                        SuggestsReopening = suggestsReopening,
                        // Only where there is a suggestion to explain, the same rule DoneAt follows.
                        ReopenTo = suggestsReopening ? ReopenTargetFor(role) : null,
                        // A row with something to do is never excluded, and that is what the word means
                        // rather than a special case for the reopening: isBlocked reads this field to
                        // disable the checkbox, and the label chain shows it first, so a row carrying
                        // it says "nothing will happen here". Before the reopening existed no row
                        // could be both, which is why the waiting branch below never needed a guard
                        // while the done branch did. The twin in AdoEndpoints carries the rest of the
                        // reasoning, including why this is asked here rather than in the label chain.
                        //
                        // Only the waiting branch can actually reach it - measured at this endpoint
                        // with a status in both lists and the toggle off: suggestsClosing true,
                        // isWaiting false, excluded null. The durable reason does not rest on the
                        // branch order in JiraStatusRoles.For: role is one enum value, suggestsClosing
                        // needs Done and isWaiting is Waiting, so the two cannot both hold.
                        Excluded = suggestsClosing || suggestsReopening
                            ? null
                            // Done first, so a finished issue that was never imported is kept out
                            // rather than brought in as a fresh open task. Shown rather than hidden,
                            // the same choice the waiting rows make.
                            //
                            // The localStatus test stays, and it is not the guard above written twice:
                            // the two part company when the task is done here and done in the source,
                            // where neither suggestion fires and this is what keeps a finished row
                            // from being labelled as one import will skip. Pinned by
                            // A_task_finished_in_both_places_suggests_nothing.
                            : role == JiraStatusRole.Done && localStatus is null
                                ? ErrorCodes.JiraExcludedDone
                                : isWaiting && !settings.IncludeWaiting
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
            var closures = request.Closures ?? [];

            foreach (var row in rows)
            {
                if (ValidateRow(row) is { } invalid)
                {
                    return invalid;
                }
            }

            // Validated before anything is written, so a bad closure cannot leave half an import
            // behind. The codes are the row codes rather than new ones: the same two facts.
            foreach (var closure in closures)
            {
                if (ValidateClosure(closure) is { } invalid)
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

                // A finished issue is never imported as a new task. The preview already marks it
                // excluded, so reaching this needs a client that previewed under an older done list -
                // which is exactly the case the rule is re-applied for.
                if (role == JiraStatusRole.Done)
                {
                    skipped++;
                    continue;
                }

                if (isWaiting && !settings.IncludeWaiting)
                {
                    skipped++;
                    continue;
                }

                if (!known.TryAdd(row.Key, CoreStatus.Open))
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
                    // Only Waiting parks the task, and the duty row's half of that reasoning has moved
                    // to StatusFor, where the reopening suggestion reads it too.
                    Status = StatusFor(role),
                    // WaitingOn is left unset on purpose. An issue assigned to you that sits in a
                    // waiting status is waiting on somebody who is not in the assignee field, so
                    // there is nobody the app could name here without inventing one.
                    WaitingSince = isWaiting ? row.WaitingSince?.UtcDateTime : null,
                    CreatedAt = clock.UtcNow,
                });

                imported++;
            }

            var closed = await CloseAsync(db, settings, clock, closures, () => skipped++);

            await db.SaveChangesAsync();

            return TypedResults.Ok(new JiraImportResponse
            {
                Imported = imported,
                Skipped = skipped,
                Closed = closed,
            });
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

    /// <summary>
    /// The status a fresh import gives a row in this role, and the one place that mapping is written.
    /// Only <see cref="JiraStatusRole.Waiting"/> parks the task; every other role arrives open.
    ///
    /// <b>A duty row arrives open on purpose</b>, and that is the branch worth knowing: written as
    /// WaitingFor it would land in "Venter på", away from the deadline sections — hiding exactly the
    /// work the user holds the duty for. It reads the same way for a reopening: an issue you hold the
    /// duty for waits for <em>you</em>.
    ///
    /// Extracted when the reopening suggestion needed the same answer: the suggestion says which status
    /// it would restore, and if it worked that out for itself the two could disagree without the
    /// compiler or a single test noticing. Note what this is <em>not</em>: the import's own waiting
    /// branches still ask the role directly, because they decide whether to write a row at all rather
    /// than what to put in it.
    /// </summary>
    private static CoreStatus StatusFor(JiraStatusRole role)
        => role == JiraStatusRole.Waiting ? CoreStatus.WaitingFor : CoreStatus.Open;

    /// <summary>
    /// What a reopening would restore, read off <see cref="StatusFor"/> rather than off the role a
    /// second time - so the suggestion can only ever name a status the import would actually write,
    /// whatever that mapping becomes.
    /// </summary>
    private static ReopenTarget ReopenTargetFor(JiraStatusRole role)
        => StatusFor(role) == CoreStatus.WaitingFor ? ReopenTarget.WaitingFor : ReopenTarget.Open;

    /// <summary>
    /// The two facts a closure has to carry, refused with the row codes rather than codes of their
    /// own: the same key and the same status name are being demanded, so a second pair would be two
    /// more translations for a sentence that already exists.
    /// </summary>
    private static BadRequest<ApiError>? ValidateClosure(JiraClosureRow closure)
    {
        if (string.IsNullOrWhiteSpace(closure.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.JiraRowKeyRequired, "Every row needs a key.");
        }

        return string.IsNullOrWhiteSpace(closure.Status)
            ? ApiErrors.BadRequest(
                ErrorCodes.JiraRowStatusRequired, "Every row needs its Jira status name.")
            : null;
    }

    /// <summary>
    /// Closes the local tasks whose issue is finished, and answers how many. The twin of
    /// AdoEndpoints.CloseAsync, which carries the full reasoning; the short version is that every
    /// decision is taken again here, and that the completion time cannot come from
    /// <c>PUT /api/tasks/{id}</c> because that route overwrites it with the clock's now.
    /// </summary>
    private static async Task<int> CloseAsync(
        TodoDbContext db,
        JiraSettings settings,
        IClock clock,
        ICollection<JiraClosureRow> closures,
        Action skip)
    {
        if (closures.Count == 0)
        {
            return 0;
        }

        var keys = closures.Select(closure => closure.Key).ToList();
        var tasks = await db.Tasks
            .Where(t => t.SourceId == JiraTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .ToDictionaryAsync(t => t.ExternalKey!, t => t, StringComparer.Ordinal);

        var closed = 0;

        foreach (var closure in closures)
        {
            if (JiraStatusRoles.For(closure.Status, settings) != JiraStatusRole.Done
                || !tasks.TryGetValue(closure.Key, out var task)
                || task.Status == CoreStatus.Done)
            {
                skip();
                continue;
            }

            task.Status = CoreStatus.Done;
            task.CompletedAt = closure.DoneAt?.UtcDateTime ?? clock.UtcNow;
            task.WaitingSince = null;
            closed++;
        }

        return closed;
    }

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
    /// <summary>
    /// The keys already imported, each with the local task's status. The status is what stops a
    /// closure suggestion from coming back once it has been accepted, and a set of keys cannot say
    /// it - see the twin in AdoEndpoints.
    /// </summary>
    private static async Task<Dictionary<string, CoreStatus>> ImportedKeysAsync(
        TodoDbContext db, List<string> keys)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == JiraTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .Select(t => new { Key = t.ExternalKey!, t.Status })
            .ToListAsync();

        return found.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
    }

    // The source hands over UTC, because SQLite cannot sort a DateTimeOffset and one must never
    // reach the entity. The contract's field is a DateTimeOffset, so the zero offset is put back on
    // at the edge rather than left for the reader to assume.
    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } moment
        ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
        : null;
}
