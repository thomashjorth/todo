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
                var imported = await ImportedKeysAsync(db);
                var fetched = new HashSet<string>(
                    page.Items.Select(item => item.Key), StringComparer.Ordinal);
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
                        ? (CoreStatus?)found.Status
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
                        // This loop is the fetch, so false throughout - see the twin in AdoEndpoints
                        // for why the client cannot work it out and why the count line needs it.
                        Reconciled = false,
                        SuggestsClosing = suggestsClosing,
                        // Decided rather than left out, and the server cannot tell the two apart: a
                        // closure body without "reason" deserialises to Resolved, because Reason is a
                        // non-nullable enum whose zero is Resolved, nothing validates it, and
                        // ValidateClosure asks only about the key and the status. So the value here is
                        // the same one an absent field would have produced - what changes is that it
                        // was chosen. That is what lets jira-store.ts drop its
                        // `?? ClosureReason.Resolved` fallback: the fallback can only go once every
                        // closing row carries a reason, and these are the older half of them.
                        ClosureReason = suggestsClosing ? ClosureReason.Resolved : null,
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
                        // rather than a special case for the reopening: this field is the row's
                        // "nothing will happen here" mark. Before the reopening existed no row could
                        // be both, which is why the waiting branch below never needed a guard while
                        // the done branch did. The twin in AdoEndpoints carries the rest of the
                        // reasoning, including why this is asked here rather than in the label chain.
                        //
                        // What it costs today, measured against the frontend that is actually there:
                        // excludedCount in jira-import.ts counts every row carrying a code into "N
                        // left out", so a reopening row would be counted as left out while being
                        // offered, and the label chain would print the skip sentence over the
                        // suggestion. The checkbox half is a prediction about tasks 8 and 9 rather
                        // than a fact - isBlocked already answers true for any alreadyImported row.
                        //
                        // The suggestsClosing half is inert: measured, replacing this condition with
                        // suggestsReopening alone leaves zero red. It stands for task 6's two codes.
                        // It cannot fire today because role is one enum value - suggestsClosing needs
                        // Done and isWaiting is Waiting - a narrower reason than the branch order in
                        // JiraStatusRoles.For, which it does not depend on. Measured here with a
                        // status in both lists and the toggle off: closing true, waiting false,
                        // excluded null.
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

                // Its own catch, and the fetch's rows are what it protects - the twin in AdoEndpoints
                // carries the reasoning. Jira is the source that makes it urgent: only a 404 is read
                // as an absence, so an issue the token may not browse raises, and one such local task
                // would otherwise answer 400 for the whole screen until the user found and deleted it.
                List<JiraPreviewRow> reconciled;
                string? notice;

                try
                {
                    (reconciled, notice) =
                        await ReconciledRowsAsync(source, settings, imported, fetched);
                }
                catch (SourceException exception)
                {
                    (reconciled, notice) = ([], exception.Code);
                }

                rows.AddRange(reconciled);

                return TypedResults.Ok(new JiraPreviewResponse
                {
                    Rows = rows,
                    Total = page.Total,
                    ReconciliationNotice = notice,
                });
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
            var reopenings = request.Reopenings ?? [];

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

            foreach (var reopening in reopenings)
            {
                if (ValidateReopening(reopening) is { } invalid)
                {
                    return invalid;
                }
            }

            var known = await ImportedKeysAsync(db);
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

                // StatusFor rather than a literal Open, which is what this said while the value went
                // unread. The preview now reads the status the lookup carries, and a reader who
                // trusted a hardcoded Open here would get it for a row this very loop writes as
                // WaitingFor - a third spelling of the mapping the two above were just merged into.
                // The title is trimmed to match what the entity below is given, for the same reason.
                if (!known.TryAdd(row.Key, new ImportedTask(StatusFor(role), row.Title.Trim())))
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
            var reopened = await ReopenAsync(db, settings, reopenings, () => skipped++);

            await db.SaveChangesAsync();

            return TypedResults.Ok(new JiraImportResponse
            {
                Imported = imported,
                Skipped = skipped,
                Closed = closed,
                Reopened = reopened,
            });
        })
        .WithName("importJira")
        .WithTags("Jira")
        .Produces<JiraImportResponse>();

        return app;
    }

    /// <summary>
    /// The rows for the keys the fetch could not speak about, and the notice that stands in for all of
    /// them when the answer is not to be trusted. The twin in AdoEndpoints carries the full reasoning
    /// and the mutation measurements - the two were mutated together, and every one of them named a
    /// test on this source as well as on that one. What differs here is the price of a timestamp:
    /// Jira has none on the issue, so a resolved row costs one changelog call, the same bargain the
    /// fetch's own closing rows make.
    /// </summary>
    private static async Task<(List<JiraPreviewRow> Rows, string? Notice)> ReconciledRowsAsync(
        JiraTaskSource source,
        JiraSettings settings,
        Dictionary<string, ImportedTask> imported,
        HashSet<string> fetched)
    {
        // Open here and missing from today's fetch. Done is left out because a task finished here is
        // settled whatever Jira says - and this source pays one call per key, so the archive would be
        // the expensive half of a question with no answer in it.
        var keys = imported
            .Where(entry => entry.Value.Status != CoreStatus.Done && !fetched.Contains(entry.Key))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        var answer = await source.ReconcileAsync(keys);
        var found = answer.Found.ToDictionary(item => item.Key, StringComparer.Ordinal);

        // The safety net, and Jira is the source it was written for: Data Center hides an issue you may
        // not browse behind 404 Issue Does Not Exist rather than a 403, so a lost project permission
        // answers exactly like a deleted issue, for every key at once. Reading that as a mass deletion
        // would paper the screen with missingInSource against every task the user still has open. The
        // twin in AdoEndpoints carries the rest of the reasoning, including why the absence is asked on
        // Found alone and why one key asked is exempt.
        if (keys.Count > 1 && keys.TrueForAll(key => !found.ContainsKey(key)))
        {
            return ([], ErrorCodes.JiraNoneRecognized);
        }

        var rows = new List<JiraPreviewRow>();

        foreach (var key in keys)
        {
            var local = imported[key];
            var item = found.GetValueOrDefault(key);
            var role = item is null
                ? (JiraStatusRole?)null
                : JiraStatusRoles.For(item.StatusName, settings);

            // Resolved outranks unassigned, and the order of these two branches is the whole of that
            // rule - see the twin in AdoEndpoints for why only one test can see it, and why the
            // unassigned branch has to test the item and not only the set.
            ClosureReason? reason = role == JiraStatusRole.Done
                ? ClosureReason.Resolved
                : item is not null && !answer.StillMine.Contains(key)
                    ? ClosureReason.Unassigned
                    : null;

            rows.Add(new JiraPreviewRow
            {
                Key = key,
                // The source's title where there is one, and the local task's where Jira answered
                // nothing at all - there was no answer to take one from. This is the half of the key
                // lookup that grew past the status.
                Title = item?.Title ?? local.Title,
                Url = settings.BrowseUrl(key)
                    ?? throw new SourceException(
                        ErrorCodes.JiraNotConfigured,
                        "A reconciled row has no browse URL, which cannot happen once IsConfigured has passed."),
                Note = item?.Note,
                // Null on every reconciled item, and that is ExternalReconciliation's rule rather than
                // this line's: Map fills it from duedate for the fetch, and the reconciliation clears
                // it, because a row whose tick box means "close this one" is never imported as new
                // work. Read off the item so the rule lives in one place.
                Deadline = item?.Deadline,
                Requester = item?.Requester,
                // Null for a key Jira does not know, which is why the contract loosened it: no answer
                // came back to take one from, and an empty string would be a value saying one thing
                // while its type said another.
                Status = item?.StatusName,
                IsWaiting = role == JiraStatusRole.Waiting,
                IsDuty = role == JiraStatusRole.Duty,
                // Never carried on a reconciled row. It travels so a newly imported waiting task knows
                // when its wait began, and none of these is imported as new work - and on this source
                // it would cost a changelog call per row to say something nothing acts on.
                WaitingSince = null,
                // True by construction: every key here came out of the imported lookup.
                AlreadyImported = true,
                // And this is the half alreadyImported cannot say: a fetched row can be imported
                // too. This one says the fetch did not answer with it and the app went looking.
                Reconciled = true,
                SuggestsClosing = reason is not null,
                ClosureReason = reason,
                // Follows the reason and not the suggestion, and the call is paid only where the answer
                // will be used: an unassigned issue was never finished - it stopped being the user's -
                // so there is no completion time to fetch and the import falls back on the clock.
                DoneAt = reason == ClosureReason.Resolved
                    ? AsUtc(await source.FetchStatusChangedAtAsync(key))
                    : null,
                // Unreachable rather than merely false: a task that is done here is not reconciled at
                // all, and being done here is the whole of what a reopening rests on.
                SuggestsReopening = false,
                ReopenTo = null,
                // The same rule the fetch's rows follow: a row with something to do never carries a
                // code, because this field is the row's "nothing will happen here" mark.
                Excluded = reason is not null
                    ? null
                    : item is null
                        ? ErrorCodes.JiraMissingInSource
                        : ErrorCodes.JiraOutsideFilter,
            });
        }

        return (rows, null);
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
    /// whatever that mapping becomes. That is structure rather than a guard, and the twin in
    /// AdoEndpoints carries the measurement that shows where the difference bites.
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
    /// The same two facts and the same two codes a closure is refused for. A reopening carries no
    /// reason, because its target is derived from the status through <see cref="StatusFor"/> - there
    /// is nothing the client could tell the server that the server does not already work out.
    /// </summary>
    private static BadRequest<ApiError>? ValidateReopening(JiraReopeningRow reopening)
    {
        if (string.IsNullOrWhiteSpace(reopening.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.JiraRowKeyRequired, "Every row needs a key.");
        }

        return string.IsNullOrWhiteSpace(reopening.Status)
            ? ApiErrors.BadRequest(
                ErrorCodes.JiraRowStatusRequired, "Every row needs its Jira status name.")
            : null;
    }

    /// <summary>
    /// Reopens the local tasks the source is not finished with, and answers how many. The twin of
    /// AdoEndpoints.ReopenAsync, which carries the full reasoning; the short version is that every
    /// decision is taken again, that being done here is the one condition inverted from the closure's,
    /// and that the status comes from <see cref="StatusFor"/> so the suggestion and the import cannot
    /// name different things.
    /// </summary>
    private static async Task<int> ReopenAsync(
        TodoDbContext db,
        JiraSettings settings,
        ICollection<JiraReopeningRow> reopenings,
        Action skip)
    {
        if (reopenings.Count == 0)
        {
            return 0;
        }

        var keys = reopenings.Select(reopening => reopening.Key).ToList();
        var tasks = await db.Tasks
            .Where(t => t.SourceId == JiraTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .ToDictionaryAsync(t => t.ExternalKey!, t => t, StringComparer.Ordinal);

        var reopened = 0;

        foreach (var reopening in reopenings)
        {
            var role = JiraStatusRoles.For(reopening.Status, settings);

            if (role == JiraStatusRole.Done
                || !tasks.TryGetValue(reopening.Key, out var task)
                || task.Status != CoreStatus.Done)
            {
                skip();
                continue;
            }

            task.Status = StatusFor(role);
            task.CompletedAt = null;
            task.WaitingSince = role == JiraStatusRole.Waiting
                ? reopening.WaitingSince?.UtcDateTime
                : null;
            reopened++;
        }

        return reopened;
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
            // Conditional on the reason, and AdoEndpoints.CloseAsync carries the full reasoning: an
            // issue handed to somebody else is still in an open status, so an unconditional status
            // check would skip every unassigned closure and count it, leaving the feature looking
            // finished while doing nothing. The price is the same too - ownership is a fact only the
            // source can settle, and the import makes no call.
            var statusSaysDone =
                JiraStatusRoles.For(closure.Status, settings) == JiraStatusRole.Done;

            if ((closure.Reason == ClosureReason.Resolved && !statusSaysDone)
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
    /// Every key this source has imported, each with the local task's status and title.
    ///
    /// <b>Scoped to this source and to nothing else.</b> A retro card and a Jira issue can carry the
    /// same key, and one of them counting as the other would hide a real issue behind a card that has
    /// nothing to do with it.
    ///
    /// The status is what stops a closure suggestion from coming back once it has been accepted, the
    /// title is what a reconciled row falls back on when Jira answers nothing at all, and the reason
    /// this reads <em>every</em> key rather than a named list is that the preview has to see what was
    /// missing from the fetch. The twin in AdoEndpoints carries the trade in full, and the
    /// <c>DistinctBy</c> with it: <c>IX_Tasks_SourceId_ExternalKey</c> is not unique, and without that
    /// line a duplicate key would be an uncaught ArgumentException - a 500 on every preview and every
    /// import for this source - where before the widening it needed a key the fetch had just answered
    /// with. No path through the app produces one, so the guard closes a fault nobody can currently
    /// reach.
    /// </summary>
    private static async Task<Dictionary<string, ImportedTask>> ImportedKeysAsync(TodoDbContext db)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == JiraTaskSource.Id && t.ExternalKey != null)
            .Select(t => new { Key = t.ExternalKey!, t.Status, t.Title })
            .ToListAsync();

        return found
            .DistinctBy(row => row.Key, StringComparer.Ordinal)
            .ToDictionary(
                row => row.Key, row => new ImportedTask(row.Status, row.Title), StringComparer.Ordinal);
    }

    // The source hands over UTC, because SQLite cannot sort a DateTimeOffset and one must never
    // reach the entity. The contract's field is a DateTimeOffset, so the zero offset is put back on
    // at the edge rather than left for the reader to assume.
    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } moment
        ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
        : null;
}
