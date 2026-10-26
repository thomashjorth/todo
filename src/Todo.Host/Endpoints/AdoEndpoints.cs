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
                var imported = await ImportedKeysAsync(db);
                var fetched = new HashSet<string>(
                    page.Items.Select(item => item.Key), StringComparer.Ordinal);
                var rows = new List<AdoPreviewRow>();

                foreach (var item in page.Items)
                {
                    // One rule, in Todo.Core, because this decision is taken twice - here and in the
                    // import below - and two places is two places it can be forgotten.
                    var role = AdoStateRoles.For(item.StatusName, settings);
                    var isWaiting = role == AdoStateRole.Waiting;

                    // Three facts, combined here rather than shipped separately: the lists live on the
                    // server, so the decision is the server's, and the import takes it again. The
                    // local status is what stops the offer once it has been accepted - without it the
                    // same row would keep suggesting a closure it already got.
                    var localStatus = imported.TryGetValue(item.Key, out var found)
                        ? (CoreStatus?)found.Status
                        : null;
                    var suggestsClosing = role == AdoStateRole.Done
                        && localStatus is { } status
                        && status != CoreStatus.Done;

                    // The mirror image of suggestsClosing, on the same two facts with each of them
                    // negated: the source is not finished, and the local task is. No reconciliation is
                    // needed for this half, because the rows it speaks about are the ones the fetch
                    // has already handed over - they stand here as alreadyImported with nothing to do
                    // until this rule speaks. Stated that way round on purpose: the converse would not
                    // hold, since the WIQL also filters on work item type, so a finished task whose
                    // item has since changed type is out of reach here. That hole is named in the
                    // design's section 8 and left open there.
                    //
                    // IncludeWaiting is deliberately not asked, though every other waiting branch on
                    // this route asks it: that toggle governs what comes in as new work, and a task
                    // the user already owns is not held back by a filter for new ones.
                    var suggestsReopening = role != AdoStateRole.Done && localStatus is CoreStatus.Done;

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
                        // Null only for a source that reports no type, and Jira is that source. On
                        // this fetch path the WIQL matched on System.WorkItemType and the batch asked
                        // for it, so a null is unreachable here - which is why this is an empty string
                        // rather than a branch. Note that the contract now calls the field nullable,
                        // and that is not a contradiction: a reconciled row can carry no type at all,
                        // because no answer came back to take one from. The narrow claim is about this
                        // loop, not about the field.
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
                        AlreadyImported = localStatus is not null,
                        // This loop is the fetch, so false throughout. Not derivable by the client:
                        // a reconciled row suggesting a resolved closure is byte-identical to one
                        // from here, and total counts only what the fetch answered - so without this
                        // the screen says "showing 4 of 3" the moment anything is reconciled.
                        //
                        // Both values are held, and by different tests. Flipped to true here, five go
                        // red: A_reconciled_row_says_so_because_the_total_counts_only_the_fetch and
                        // A_refused_key_costs_the_reconciliation_rather_than_the_whole_preview on
                        // both sources, plus TaskEndpointsTests.Wire_format_uses_the_names_the_
                        // contract_declares, which reads "reconciled":false off the raw body. Flipped
                        // to false on the reconciled row instead, only the first pair goes red.
                        Reconciled = false,
                        SuggestsClosing = suggestsClosing,
                        // Decided rather than left out, and the server cannot tell the two apart: a
                        // closure body without "reason" deserialises to Resolved, because Reason is a
                        // non-nullable enum whose zero is Resolved, nothing validates it, and
                        // ValidateClosure asks only about the key and the state. So the value here is
                        // the same one an absent field would have produced - what changes is that it
                        // was chosen. That is what lets ado-store.ts drop its
                        // `?? ClosureReason.Resolved` fallback: the fallback can only go once every
                        // closing row carries a reason, and these are the older half of them.
                        //
                        // Measured by writing a bare null here: the only red is
                        // A_done_work_item_that_was_imported_before_offers_to_close_the_task, and its
                        // Jira twin A_done_issue_that_was_imported_before_offers_to_close_the_task
                        // for the same line over there. Nothing downstream can see it yet, which is
                        // the reason the assertion had to be added rather than found.
                        ClosureReason = suggestsClosing ? ClosureReason.Resolved : null,
                        // Only for the rows that will use it, which is the same rule WaitingSince
                        // follows one line up - and the reason this is its own field rather than a
                        // loosened WaitingSince: that one is null for every row that is not waiting,
                        // and a finished row never is.
                        DoneAt = suggestsClosing ? AsUtc(item.StatusChangedAt) : null,
                        SuggestsReopening = suggestsReopening,
                        // Only where there is a suggestion to explain, the same rule DoneAt follows.
                        ReopenTo = suggestsReopening ? ReopenTargetFor(role) : null,
                        // A row with something to do is never excluded, and that is what the word
                        // means rather than a special case for the reopening: this field is the row's
                        // "nothing will happen here" mark. Before the reopening existed no row could
                        // be both, which is why the waiting branch below never needed a guard while
                        // the done branch did.
                        //
                        // What that costs today, measured against the frontend that is actually
                        // there rather than the one this comment first described: the visible fault is
                        // excludedCount in ado-import.ts, which counts every row carrying a code into
                        // "N left out" - so a reopening row would be counted as left out while being
                        // offered. The label chain shows excluded first, so it would also print the
                        // skip sentence over the suggestion.
                        //
                        // The checkbox half is a prediction about tasks 8 and 9, not a fact: today
                        // isBlocked is !selectable && !closable, selectable already drops every
                        // alreadyImported row, and a reopening row is one - so the box is disabled in
                        // both worlds and select() refuses it either way. Nothing here makes
                        // reopenable() unnecessary; it is still the thing that has to be built.
                        //
                        // Asked here rather than in the label chain, whose order has its own reasoning
                        // in the template, and asked once rather than as a !suggestsReopening on the
                        // waiting branch alone: the reconciliation adds two more codes below this, and
                        // a rule at the top is one their author cannot forget.
                        //
                        // The suggestsClosing half is inert, and saying so keeps a green suite from
                        // being read as proof that it works: measured, replacing this whole condition
                        // with suggestsReopening alone leaves zero red. It stands for task 6's two
                        // codes. It cannot fire today because role is one enum value - suggestsClosing
                        // needs Done and isWaiting is Waiting - which is a narrower reason than the
                        // branch order in AdoStateRoles.For and does not depend on it. Measured at
                        // this endpoint with a state in both lists and the toggle off: suggestsClosing
                        // true, isWaiting false, excluded null.
                        Excluded = suggestsClosing || suggestsReopening
                            ? null
                            // Done first, so a finished work item that was never imported is kept out
                            // rather than brought in as a fresh open task. Shown rather than hidden,
                            // the same choice the waiting rows make.
                            //
                            // The localStatus test stays, and it is not the guard above written twice:
                            // the two part company when the task is done here and done in the source,
                            // where neither suggestion fires and this is what keeps a finished row
                            // from being labelled as one import will skip. Pinned by
                            // A_task_finished_in_both_places_suggests_nothing.
                            : role == AdoStateRole.Done && localStatus is null
                                ? ErrorCodes.AdoExcludedDone
                                : isWaiting && !settings.IncludeWaiting
                                    ? ErrorCodes.AdoExcludedWaiting
                                    : null,
                    });
                }

                // Its own catch, and the fetch's rows are what it protects. The outer handler turns a
                // SourceException into a 400 for the whole response, which was right while the only
                // call was the fetch itself - but by here the fetch has already succeeded, and taking
                // its rows down because one reconciled key was refused loses work that is on the
                // screen for a question asked about something else.
                //
                // The path this closes was introduced by the reconciliation and did not exist before
                // it: one local task whose work item the token has lost access to made the whole
                // import screen answer 400 until the user found and deleted that task. Worth naming
                // the irony, because it is what makes the shape obvious in hindsight - noneRecognized
                // exists so a lost permission cannot ruin the screen, and it only covers the 404
                // shape; the 403 shape took the screen down completely.
                //
                // The code goes in the notice rather than into a field of its own: ado.refused and
                // ado.unreachable are already translated in both language files, and the notice
                // already means "the reconciliation has nothing to say and here is why".
                //
                // Measured by deleting this catch and letting the outer one have it:
                // A_refused_key_costs_the_reconciliation_rather_than_the_whole_preview goes red on
                // both sources with "Expected: OK / Actual: BadRequest", and nothing else moves.
                List<AdoPreviewRow> reconciled;
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

                return TypedResults.Ok(new AdoPreviewResponse
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
            var closures = request.Closures ?? [];
            var reopenings = request.Reopenings ?? [];

            foreach (var row in rows)
            {
                if (ValidateRow(row) is { } invalid)
                {
                    return invalid;
                }
            }

            // Validated in the same pass and before anything is written, so a bad closure cannot leave
            // half an import behind. The codes are the row codes rather than new ones: it is the same
            // two facts being refused, and a second pair would be two more translations for a sentence
            // that already exists.
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

            // Derived once from the clock rather than taken from the row, which is why AdoImportRow
            // carries no deadline: the date is relative to the import, so previewing today and
            // importing tomorrow gives tomorrow's arithmetic - decision A, and it is the intended
            // outcome rather than a rounding error.
            var deadline = AdoDeadline.For(clock.Today, settings.DefaultDeadlineDays);

            var known = await ImportedKeysAsync(db);
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
                var role = AdoStateRoles.For(row.State, settings);
                var isWaiting = role == AdoStateRole.Waiting;

                // A finished work item is never imported as a new task. The preview already marks it
                // excluded, so reaching this needs a client that previewed under an older done list -
                // which is exactly the case the rule is re-applied for.
                if (role == AdoStateRole.Done)
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
                    SourceId = AdoTaskSource.Id,
                    ExternalKey = row.Key,
                    Title = row.Title.Trim(),
                    Note = row.Note,
                    Deadline = deadline,
                    Requester = row.Requester,
                    Status = StatusFor(role),
                    // WaitingOn is left unset on purpose. A work item assigned to you that sits in a
                    // waiting state is waiting on somebody who is not in the AssignedTo field, so there
                    // is nobody the app could name here without inventing one.
                    WaitingSince = isWaiting ? row.WaitingSince?.UtcDateTime : null,
                    CreatedAt = clock.UtcNow,
                });

                imported++;
            }

            var closed = await CloseAsync(db, settings, clock, closures, () => skipped++);
            var reopened = await ReopenAsync(db, settings, reopenings, () => skipped++);

            await db.SaveChangesAsync();

            return TypedResults.Ok(new AdoImportResponse
            {
                Imported = imported,
                Skipped = skipped,
                Closed = closed,
                Reopened = reopened,
            });
        })
        .WithName("importAdo")
        .WithTags("Ado")
        .Produces<AdoImportResponse>();

        return app;
    }

    /// <summary>
    /// The rows for the keys the fetch could not speak about, and the notice that stands in for all of
    /// them when the answer is not to be trusted.
    ///
    /// <b>Who gets asked:</b> the keys this source wrote that are still open here and did not come back
    /// in today's fetch. The ones that came back already have a row. Done is left out because a task
    /// finished here is settled whatever the source says, and asking anyway would grow the question by
    /// every task the user has ever completed - which Jira pays for one call at a time.
    ///
    /// <b>Every lookup is by the key we asked with</b>, never by a key off the answer. Both sources
    /// respell keys - Jira canonicalises case, Azure DevOps reads its keys as numbers - and
    /// ITaskSource.ReconcileAsync makes the caller's spelling the one that counts. Iterating the answer
    /// instead would silently lose every key that produced no answer at all, which is the outcome this
    /// whole method exists to report. Measured by looping over <c>answer.Found</c>'s keys rather than
    /// over the ones asked: every test that speaks about a key with no answer goes red, on both
    /// sources, because a key with an answer is the only kind such a loop can see.
    ///
    /// <b>That fault has no test of its own, and deliberately so.</b> It loses the rows for
    /// unanswered keys; a boundary written as <c>Exists</c> rather than <c>TrueForAll</c> loses those
    /// <em>and</em> the answered ones - so the first is a strict subset of the second and nothing can
    /// separate them. The guard that was actually missing was the boundary's, and it is
    /// <c>One_missing_key_among_answered_ones_still_shows_every_row_because_the_notice_needs_all_of_them</c>.
    /// </summary>
    private static async Task<(List<AdoPreviewRow> Rows, string? Notice)> ReconciledRowsAsync(
        AdoTaskSource source,
        AdoSettings settings,
        Dictionary<string, ImportedTask> imported,
        HashSet<string> fetched)
    {
        // Ordered so the screen is stable between two previews of the same state. Nothing reads
        // meaning into the order - a dictionary promises none, and these rows sit after the fetch's.
        //
        // Dropping the status test here fells A_task_that_is_done_here_is_never_reconciled and its
        // Jira twin of the same name, and nothing else: no other fixture holds a task that is done
        // here and absent from the fetch at the same time.
        var keys = imported
            .Where(entry => entry.Value.Status != CoreStatus.Done && !fetched.Contains(entry.Key))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        var answer = await source.ReconcileAsync(keys);
        var found = answer.Found.ToDictionary(item => item.Key, StringComparer.Ordinal);

        // The safety net, and it is the reason the feature can be trusted at all. Every key gone at
        // once is what a lost permission looks like: Jira Data Center hides an unbrowsable issue behind
        // 404 rather than 403, and a token that may no longer read an Azure DevOps project leaves the
        // whole batch read omitted. A working list deleted in one go is the rarer reading, so the app
        // says the likelier cause once instead of printing missingInSource against every task the user
        // still has open - which reads as an invitation to clear them out by hand.
        //
        // Asked on Found alone, though ExternalReconciliation defines an absence as "in neither set".
        // The two agree on what matters here: a key absent from Found carries no fact any other outcome
        // could rest on - there is no state, so neither closing branch can fire - so it produces a
        // missingInSource row whatever StillMine says. Reading the absence off Found is therefore the
        // same question the row below asks, and the notice stands in for exactly the rows it replaces.
        //
        // More than one key, because with one asked all and one are the same answer and a genuinely
        // deleted item must still be reported. The false negative that leaves is accepted and written
        // down in the design's section 5.
        //
        // Three parts, each with its own guard, and each measured by changing that part alone. A
        // compound mutation is what let the third one hide in the first place, so they are written
        // out one at a time:
        //
        //   the whole line deleted        -> Every_key_missing_at_once_suggests_nothing_because_it_
        //                                    is_more_likely_a_permission
        //   Count > 1 dropped, all kept   -> A_single_missing_key_is_still_reported_because_all_and_
        //                                    one_are_the_same_there and A_key_the_source_does_not_
        //                                    know_is_shown_as_missing_and_carries_the_local_title
        //   TrueForAll -> Exists, count kept
        //                                 -> One_missing_key_among_answered_ones_still_shows_every_
        //                                    row_because_the_notice_needs_all_of_them
        //
        // each on both sources, and nothing else red in any of the three. The last one is the one
        // that hid: it left the whole Api suite green until that test was written for it. Worth
        // knowing which way that fault points, because it is the opposite of what the rule is for -
        // one genuinely deleted item beside one ordinary key would hide every reconciliation row
        // behind this line, the closing offers and the reopenings with them.
        if (keys.Count > 1 && keys.TrueForAll(key => !found.ContainsKey(key)))
        {
            return ([], ErrorCodes.AdoNoneRecognized);
        }

        var rows = new List<AdoPreviewRow>();

        foreach (var key in keys)
        {
            var local = imported[key];
            var item = found.GetValueOrDefault(key);
            var role = item is null ? (AdoStateRole?)null : AdoStateRoles.For(item.StatusName, settings);

            // Resolved outranks unassigned, and the order of these two branches is the whole of that
            // rule. Both outcomes are legal, so swapping them compiles and reads right. An item can be
            // finished and handed on at the same time, and then "resolved" is the more precise of the
            // two while the action the user takes is the same either way: it is the label being
            // decided here, not the tick box.
            //
            // Measured by swapping them: the only red is Resolved_outranks_unassigned_when_both_are_
            // true, once per source. Every other fixture has at most one of the two facts true, so
            // the order cannot reach them - which is why that test exists rather than being folded
            // into the two it sits between.
            //
            // The unassigned branch tests the item rather than only the set, and that is what keeps
            // the fourth outcome from being swallowed: a key the source never answered for is in
            // neither set, so a bare !StillMine would call every deleted item a hand-over.
            ClosureReason? reason = role == AdoStateRole.Done
                ? ClosureReason.Resolved
                : item is not null && !answer.StillMine.Contains(key)
                    ? ClosureReason.Unassigned
                    : null;

            rows.Add(new AdoPreviewRow
            {
                Key = key,
                // The source's title where there is one, because it is the fresher of the two, and the
                // local task's where the source answered nothing at all - there was no answer to take
                // one from, and a row with no title would say less than the task the user is looking
                // at. This is the half of the key lookup that grew past the status.
                Title = item?.Title ?? local.Title,
                Url = settings.BrowseUrl(key)
                    ?? throw new SourceException(
                        ErrorCodes.AdoNotConfigured,
                        "A reconciled row has no work item URL, which cannot happen once the "
                            + "collection and the project have both been checked."),
                Note = item?.Note,
                // Null on every reconciled item, and that is ExternalReconciliation's rule rather than
                // this line's: a reconciled row is never imported as new work, so a suggested deadline
                // is noise on a row whose tick box means "close this one". Read off the item rather
                // than written as null here, so there is one place the rule lives.
                Deadline = item?.Deadline,
                Requester = item?.Requester,
                // Both null for a key the source does not know, which is why the contract loosened
                // them: no answer came back to take either from, and an empty string would be a value
                // saying one thing while its type said another.
                State = item?.StatusName,
                WorkItemType = item?.ItemType,
                IsWaiting = role == AdoStateRole.Waiting,
                // Never carried on a reconciled row, on the same grounds as the deadline: it travels
                // so a newly imported waiting task knows when its wait began, and none of these is
                // imported as new work. Jira's twin has a price attached as well - the field costs one
                // changelog call per row there - and the two sources answer alike rather than one
                // paying for what the other leaves out.
                WaitingSince = null,
                // True by construction: every key here came out of the imported lookup.
                AlreadyImported = true,
                // And this is the half alreadyImported cannot say: a fetched row can be imported
                // too. This one says the fetch did not answer with it and the app went looking.
                Reconciled = true,
                SuggestsClosing = reason is not null,
                ClosureReason = reason,
                // Follows the reason and not the suggestion. An unassigned item was never finished -
                // it stopped being the user's - so there is no completion time to carry and the import
                // falls back on the clock, which the contract says in so many words.
                DoneAt = reason == ClosureReason.Resolved ? AsUtc(item?.StatusChangedAt) : null,
                // Unreachable rather than merely false: a task that is done here is not reconciled at
                // all, and being done here is the whole of what a reopening rests on.
                SuggestsReopening = false,
                ReopenTo = null,
                // The same rule the fetch's rows follow: a row with something to do never carries a
                // code, because this field is the row's "nothing will happen here" mark. The two codes
                // below are the two outcomes that have nothing to offer.
                Excluded = reason is not null
                    ? null
                    : item is null
                        ? ErrorCodes.AdoMissingInSource
                        : ErrorCodes.AdoOutsideFilter,
            });
        }

        return (rows, null);
    }

    /// <summary>
    /// A 400 rather than a 500, caught at the edge of each route that reached outside the process - the
    /// same shape and the same reason as JiraEndpoints.Refused: a global handler would also turn a
    /// SourceException thrown by something that never called out into a bad request.
    /// </summary>
    private static BadRequest<ApiError> Refused(SourceException exception)
        => ApiErrors.BadRequest(exception.Code, exception.Message);

    // The helper first, for the same reason as at Jira.
    private static BadRequest<ApiError>? NotConfigured(AdoSettings settings) =>
        !settings.Online
            ? ApiErrors.BadRequest(ErrorCodes.HelperOffline, "The helper on the PC is not connected.")
            : settings.IsConfigured
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

    /// <summary>
    /// The status a fresh import gives a row in this role, and the one place that mapping is written.
    /// Only <see cref="AdoStateRole.Waiting"/> parks the task; every other role arrives open.
    ///
    /// Extracted when the reopening suggestion needed the same answer: the suggestion says which status
    /// it would restore, and if it worked that out for itself the two could disagree without the
    /// compiler or a single test noticing - the same class of trap two spellings of one fact always
    /// are. Note what this is <em>not</em>: the import's own waiting branches still ask the role
    /// directly, because they decide whether to write a row at all rather than what to put in it.
    /// </summary>
    private static CoreStatus StatusFor(AdoStateRole role)
        => role == AdoStateRole.Waiting ? CoreStatus.WaitingFor : CoreStatus.Open;

    /// <summary>
    /// What a reopening would restore, read off <see cref="StatusFor"/> rather than off the role a
    /// second time - so the suggestion can only ever name a status the import would actually write,
    /// whatever that mapping becomes.
    ///
    /// <b>That is structure, not a guard, and the difference is worth knowing.</b> Rewriting this
    /// method alone to disagree with StatusFor fells six tests - but changing StatusFor <em>and</em>
    /// un-extracting this in the same edit leaves only A_duty_row_arrives_open_rather_than_waiting
    /// red, while A_duty_status_reopens_to_open_because_the_duty_is_the_users_own goes green on a
    /// lie: it claims open while the import would write WaitingFor. What keeps the two honest is that
    /// there is one expression to change, not a test standing over two.
    /// </summary>
    private static ReopenTarget ReopenTargetFor(AdoStateRole role)
        => StatusFor(role) == CoreStatus.WaitingFor ? ReopenTarget.WaitingFor : ReopenTarget.Open;

    private static BadRequest<ApiError>? NoWorkItemTypes(AdoSettings settings)
        => AdoWorkItemTypes.Effective(settings).Count == 0
            ? ApiErrors.BadRequest(
                ErrorCodes.AdoWorkItemTypesRequired,
                "At least one work item type is needed, or the import would take everything.")
            : null;

    /// <summary>
    /// The two facts a closure has to carry, refused with the row codes rather than codes of their
    /// own: it is the same key and the same state name being demanded, so a second pair would be two
    /// more translations for a sentence that already exists.
    /// </summary>
    private static BadRequest<ApiError>? ValidateClosure(AdoClosureRow closure)
    {
        if (string.IsNullOrWhiteSpace(closure.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.AdoRowKeyRequired, "Every row needs a key.");
        }

        return string.IsNullOrWhiteSpace(closure.State)
            ? ApiErrors.BadRequest(
                ErrorCodes.AdoRowStateRequired, "Every row needs its Azure DevOps state name.")
            : null;
    }

    /// <summary>
    /// The same two facts and the same two codes a closure is refused for. A reopening carries no
    /// reason: the closure needs one because ownership is a fact the server cannot recompute, while a
    /// reopening's target is derived from the state through <see cref="StatusFor"/> - so there is
    /// nothing here the client could tell the server that the server does not already work out.
    /// </summary>
    private static BadRequest<ApiError>? ValidateReopening(AdoReopeningRow reopening)
    {
        if (string.IsNullOrWhiteSpace(reopening.Key))
        {
            return ApiErrors.BadRequest(ErrorCodes.AdoRowKeyRequired, "Every row needs a key.");
        }

        return string.IsNullOrWhiteSpace(reopening.State)
            ? ApiErrors.BadRequest(
                ErrorCodes.AdoRowStateRequired, "Every row needs its Azure DevOps state name.")
            : null;
    }

    /// <summary>
    /// Reopens the local tasks the source is not finished with, and answers how many.
    ///
    /// The mirror of <see cref="CloseAsync"/>, and every decision is taken again for the same reason:
    /// the done list lives on the server, the task has to exist and belong to this source, and it must
    /// <em>be</em> done here - the one condition that is inverted. A client that previewed before
    /// somebody reopened the task by hand is the ordinary case, not the adversarial one.
    ///
    /// The status comes from <see cref="StatusFor"/> rather than from the row, so the suggestion and
    /// the import cannot name different things; the row carries the state, not the status it wants.
    ///
    /// <c>CompletedAt</c> is cleared rather than left: a task that is open again was not completed,
    /// and both the completed filter and the weekly review read that field. <c>WaitingSince</c>
    /// travels on the row for the reason <c>doneAt</c> does - the import makes no call of its own, so
    /// the source's clock has no other way in.
    /// </summary>
    private static async Task<int> ReopenAsync(
        TodoDbContext db,
        AdoSettings settings,
        ICollection<AdoReopeningRow> reopenings,
        Action skip)
    {
        if (reopenings.Count == 0)
        {
            return 0;
        }

        var keys = reopenings.Select(reopening => reopening.Key).ToList();
        var tasks = await db.Tasks
            .Where(t => t.SourceId == AdoTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .ToDictionaryAsync(t => t.ExternalKey!, t => t, StringComparer.Ordinal);

        var reopened = 0;

        foreach (var reopening in reopenings)
        {
            var role = AdoStateRoles.For(reopening.State, settings);

            if (role == AdoStateRole.Done
                || !tasks.TryGetValue(reopening.Key, out var task)
                || task.Status != CoreStatus.Done)
            {
                skip();
                continue;
            }

            task.Status = StatusFor(role);
            task.CompletedAt = null;
            task.WaitingSince = role == AdoStateRole.Waiting
                ? reopening.WaitingSince?.UtcDateTime
                : null;
            reopened++;
        }

        return reopened;
    }

    /// <summary>
    /// Closes the local tasks whose work item is finished, and answers how many.
    ///
    /// Every decision is taken again here rather than trusted: the done list lives on the server, the
    /// task has to exist and belong to this source, and it must not already be done. A client that
    /// previewed under an older list is the ordinary case, not the adversarial one.
    ///
    /// The completion time comes from the row because the import deliberately does not call Azure
    /// DevOps - the same way <c>waitingSince</c> travels. It cannot come from
    /// <c>PUT /api/tasks/{id}</c> either: that route sets <c>CompletedAt</c> to the clock's now on
    /// every move into Done, so routing a closure through it would throw the source's timestamp away
    /// silently. <c>clock.UtcNow</c> is only the fallback for a row whose timestamp was unreadable.
    ///
    /// <c>WaitingSince</c> is cleared for the same reason TaskEndpoints clears it: only the move into
    /// waiting starts that clock, and a task that is finished is not waiting on anybody.
    /// </summary>
    private static async Task<int> CloseAsync(
        TodoDbContext db,
        AdoSettings settings,
        IClock clock,
        ICollection<AdoClosureRow> closures,
        Action skip)
    {
        if (closures.Count == 0)
        {
            return 0;
        }

        var keys = closures.Select(closure => closure.Key).ToList();
        var tasks = await db.Tasks
            .Where(t => t.SourceId == AdoTaskSource.Id
                && t.ExternalKey != null
                && keys.Contains(t.ExternalKey))
            .ToDictionaryAsync(t => t.ExternalKey!, t => t, StringComparer.Ordinal);

        var closed = 0;

        foreach (var closure in closures)
        {
            // The state check belongs to the resolved reason alone, and making it unconditional is
            // the bug this branch exists to prevent: a work item handed to somebody else is still
            // Active, so every unassigned closure would be skipped and counted, and the feature would
            // look finished while doing nothing.
            //
            // The price is that the re-decision is weaker for unassigned. Ownership is a fact only the
            // source can settle, and the import deliberately makes no call - the same shape doneAt and
            // waitingSince already have - so what stays checkable is that the task exists, belongs to
            // this source, and is not already done.
            //
            // Note what required does not buy here. Reason is a non-nullable enum whose Resolved is 0,
            // the Required attribute is inert (nothing configures validation, and Required on a
            // non-nullable enum can never fail), and ValidateClosure asks only about the key and the
            // state - so a body without a reason deserialises to resolved in silence. The weaker
            // branch is reached only when a client says unassigned outright, which is a property of
            // what the client sends rather than of the contract.
            var stateSaysDone = AdoStateRoles.For(closure.State, settings) == AdoStateRole.Done;

            if ((closure.Reason == ClosureReason.Resolved && !stateSaysDone)
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
    /// Every key this source has imported, each with the local task's status and title.
    ///
    /// <b>Scoped to this source and to nothing else.</b> A Jira issue, a retro card and an Azure DevOps
    /// work item can all carry the same key - a work item id is a bare number, so the collision is
    /// likelier here than it was for Jira - and one counting as another would hide real work behind
    /// something unrelated.
    ///
    /// The status is what turns a one-off suggestion into one that stops coming back: a closure is
    /// only offered while the local task is <em>not</em> done, and a set of keys cannot say that. The
    /// title is what a reconciled row falls back on when the source answers nothing at all. It was a
    /// <c>HashSet&lt;string&gt;</c> until the closure suggestion needed the status, and a dictionary of
    /// statuses until the reconciliation needed the title.
    ///
    /// <b>Every key rather than a named list, and that is a trade worth stating.</b> The preview has to
    /// see what was <em>missing</em> from the fetch, and a lookup narrowed to the keys that came back
    /// cannot answer that - the whole reconciliation would be blind by construction. What it costs is
    /// reading the source's finished tasks along with the open ones on every preview and every import,
    /// which for one person's list is a few hundred rows of three narrow columns. The caller that does
    /// not want the finished ones filters on the status it now has.
    ///
    /// <b><see cref="Enumerable.DistinctBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey},
    /// IEqualityComparer{TKey}?)"/> is a guard rather than tidiness.</b>
    /// <c>IX_Tasks_SourceId_ExternalKey</c> is not unique, so nothing in the database stops two tasks
    /// sharing a key, and <c>ToDictionary</c> answers a duplicate with an ArgumentException nothing
    /// here catches - a 500 on every preview <em>and</em> every import for this source, with no way
    /// back from inside the app. Reading every key rather than a named list is what widened that from
    /// a key the fetch had just answered with to the whole set, which is why the line is here and not
    /// in the delivery that wrote the lookup.
    ///
    /// No path through the app produces a duplicate today - the import's own <c>TryAdd</c> is what
    /// writes them - so this closes a fault that is currently unreachable rather than one anybody has
    /// met. The real answer is a unique index, which is its own migration and does not belong here.
    /// Which of two rows survives is deliberately not decided: with no way to have two, there is no
    /// right one to prefer, and pretending otherwise would be a rule nothing could check.
    /// </summary>
    private static async Task<Dictionary<string, ImportedTask>> ImportedKeysAsync(TodoDbContext db)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == AdoTaskSource.Id && t.ExternalKey != null)
            .Select(t => new { Key = t.ExternalKey!, t.Status, t.Title })
            .ToListAsync();

        return found
            .DistinctBy(row => row.Key, StringComparer.Ordinal)
            .ToDictionary(
                row => row.Key, row => new ImportedTask(row.Status, row.Title), StringComparer.Ordinal);
    }

    // The source hands over UTC, because SQLite cannot sort a DateTimeOffset and one must never reach
    // the entity. The contract's field is a DateTimeOffset, so the zero offset is put back on at the
    // edge rather than left for the reader to assume.
    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } moment
        ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc))
        : null;
}
