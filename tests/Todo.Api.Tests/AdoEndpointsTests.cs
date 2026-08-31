using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Tasks;
using Todo.Core.Time;
using Todo.TestSupport.Ado;
using Todo.TestSupport.Time;
using ApiError = Todo.Contracts.ApiError;
// The contract's status enum, not the core one: Todo.Core.Tasks.TodoStatus carries no
// JsonStringEnumConverter, so reading "waitingFor" off the wire into it throws - and this asserts what
// the API answered rather than what the entity holds. Same note as JiraEndpointsTests.
using TodoStatus = Todo.Contracts.TodoStatus;

namespace Todo.Api.Tests;

/// <summary>
/// The four /api/ado routes, over real HTTP against a real host and a FakeAdo on loopback.
///
/// The clock is fixed, which JiraEndpointsTests had no need for: Azure DevOps has no due date field at
/// all, so every deadline here is arithmetic on <em>today</em> - decision A - and a test asserting a
/// date would otherwise be asserting the day the suite happens to run. It is set to the day the
/// instance was measured, which is also FakeAdo's own today, so the source's derivation and the
/// import's agree by construction rather than by luck.
/// </summary>
public class AdoEndpointsTests : ApiTest
{
    private static readonly DateOnly Today = FakeAdo.Today;

    private static readonly DateOnly DefaultDeadline = Today.AddDays(AdoDefaults.DeadlineDays);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(new FixedClock(Today));

    /// <summary>
    /// Blocked is the waiting state by default, because work item 15664 - the measured Bug - is the one
    /// carrying a readable state change date, so the waiting assertions and the "no extra round trip"
    /// assertion can be made about the same row.
    /// </summary>
    private async Task<FakeAdo> ConfigureAsync(
        bool includeWaiting = false,
        string? project = FakeAdo.Project,
        string[]? waitingStates = null,
        string[]? workItemTypes = null,
        int defaultDeadlineDays = AdoDefaults.DeadlineDays,
        string[]? doneStates = null)
    {
        var ado = await FakeAdo.StartAsync();

        await Host.Client.PutAsJsonAsync("/api/settings/ado-token", new { token = FakeAdo.Token });
        await Host.Client.PutAsJsonAsync("/api/settings", new
        {
            adoBaseUrl = ado.BaseUrl,
            adoProject = project,
            adoWaitingStates = waitingStates ?? ["Blocked"],
            adoDoneStates = doneStates ?? [],
            adoIncludeWaiting = includeWaiting,
            adoWorkItemTypes = workItemTypes ?? [.. AdoDefaults.WorkItemTypes],
            adoDefaultDeadlineDays = defaultDeadlineDays,
        });

        return ado;
    }

    [Fact]
    public async Task Testing_the_connection_reports_who_the_token_belongs_to()
    {
        await using var ado = await ConfigureAsync();

        var response = await Host.Client.PostAsync("/api/ado/test", null);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Connection>();

        Assert.Equal(FakeAdo.Owner, body!.DisplayName);
    }

    [Fact]
    public async Task Testing_without_a_configured_collection_is_a_bad_request_rather_than_a_crash()
    {
        var response = await Host.Client.PostAsync("/api/ado/test", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.AdoNotConfigured, error!.Code);
    }

    /// <summary>
    /// One place Jira's shape did fit, and it is worth pinning because the other three routes go the
    /// other way. Testing the connection asks _apis/connectionData at collection level and never looks
    /// at a project, so guarding it for a blank project would send the user to fill in a field this
    /// request does not use - and it would break the natural order of setting the thing up, where you
    /// test the token before you know the project name is spelled right.
    /// </summary>
    [Fact]
    public async Task Testing_the_connection_does_not_need_a_project()
    {
        await using var ado = await ConfigureAsync(project: null);

        var response = await Host.Client.PostAsync("/api/ado/test", null);

        response.EnsureSuccessStatusCode();
        Assert.Equal(FakeAdo.Owner, (await response.Content.ReadFromJsonAsync<Connection>())!.DisplayName);
    }

    /// <summary>
    /// The states come off the user's own work items, so the list is whatever they are standing in
    /// today - measurement 0g is what would change that. Both of the plan's waiting candidates are
    /// here, and so is the type filter's absence: In Progress belongs to the Test Suite, which no
    /// import would take, and it still has to be offerable or a state only that type uses could never
    /// be named.
    /// </summary>
    [Fact]
    public async Task The_states_come_from_the_users_own_work_items()
    {
        await using var ado = await ConfigureAsync();

        var body = await Host.Client.GetFromJsonAsync<States>("/api/ado/states");

        Assert.Contains("Blocked", body!.Names);
        Assert.Contains("PO Review", body.Names);
        Assert.Contains("In Progress", body.Names);
    }

    /// <summary>
    /// Unlike Jira's status list, this one needs the project, because Azure DevOps scopes a WIQL by URL
    /// path rather than by a clause in the query. The empty WIQL list is the half that matters: the
    /// refusal happens before anything goes out.
    /// </summary>
    [Fact]
    public async Task Listing_the_states_without_a_project_refuses_before_the_call()
    {
        await using var ado = await ConfigureAsync(project: null);

        var response = await Host.Client.GetAsync("/api/ado/states");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoProjectRequired,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    [Fact]
    public async Task Previewing_without_a_project_refuses_before_the_call()
    {
        await using var ado = await ConfigureAsync(project: null);

        var response = await Host.Client.PostAsync("/api/ado/preview", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoProjectRequired,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    /// <summary>
    /// Five of FakeAdo's nine work items, because the other four are test artefacts the type filter
    /// keeps out. The total is the number of ids the query matched rather than the number of rows
    /// mapped, so the two being equal says nothing was lost between the WIQL and the batch.
    /// </summary>
    [Fact]
    public async Task The_preview_reports_the_total_the_source_gave()
    {
        await using var ado = await ConfigureAsync();

        var body = await Preview();

        Assert.Equal(5, body.Total);
        Assert.Equal(5, body.Rows.Length);
        Assert.DoesNotContain("17169", body.Rows.Select(row => row.Key));
    }

    /// <summary>
    /// Field by field, because a mapping that drops one field is the failure nobody sees: the row still
    /// arrives, it is simply missing something. The deadline is the app's own arithmetic rather than
    /// anything Azure DevOps sent, since there is no due date field to send.
    /// </summary>
    [Fact]
    public async Task A_preview_row_maps_the_work_item()
    {
        await using var ado = await ConfigureAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.Equal("Kunden kan ikke logge ind", row.Title);
        Assert.Equal("Blocked", row.State);
        Assert.Equal("Bug", row.WorkItemType);
        Assert.Equal("Anna Andersen", row.Requester);
        Assert.Equal("<div>Trin 1: log ind</div>", row.Note);
        Assert.Equal(DefaultDeadline, row.Deadline);
        Assert.False(row.AlreadyImported);
    }

    /// <summary>
    /// The button on each row needs somewhere to go, and the server owns the URL shape - the URLs Azure
    /// DevOps hands back address the project by GUID and are not humanly navigable. Required rather
    /// than nullable in the contract, so the endpoint throws rather than shipping an empty string: a
    /// preview cannot happen without both halves, and a row whose button goes nowhere would be a
    /// silent one.
    /// </summary>
    [Fact]
    public async Task A_preview_row_carries_the_url_of_the_work_item()
    {
        await using var ado = await ConfigureAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.Equal(
            $"{ado.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(FakeAdo.Project)}"
                + "/_workitems/edit/15664",
            row.Url);
    }

    [Fact]
    public async Task Zero_days_leaves_the_preview_rows_without_a_deadline()
    {
        await using var ado = await ConfigureAsync(defaultDeadlineDays: 0);

        Assert.All((await Preview()).Rows, row => Assert.Null(row.Deadline));
    }

    /// <summary>
    /// Default off. The waiting row is present and marked excluded rather than missing - hiding it
    /// would look like Azure DevOps lost a work item, and it would make the setting invisible.
    ///
    /// The raw JSON is read as well as the field, because the code travels as <em>data</em> here rather
    /// than as an ApiError: a comparison against the constant cannot see it renamed, and a renamed code
    /// leaves a frontend on an older translation file showing the raw string.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_shown_as_excluded_when_waiting_is_not_asked_for()
    {
        await using var ado = await ConfigureAsync(includeWaiting: false);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.True(row.IsWaiting);
        Assert.Equal(ErrorCodes.AdoExcludedWaiting, row.Excluded);

        var json = await (await Host.Client.PostAsync("/api/ado/preview", null))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"excluded\":\"ado.excludedWaiting\"", json);
    }

    /// <summary>
    /// The measured advantage over Jira, asserted where the decision to ask is taken.
    /// Microsoft.VSTS.Common.StateChangeDate arrives with the work item, so the whole page costs no
    /// extra round trip - where Jira pays one changelog call per waiting issue. The empty
    /// WorkItemRequests list is the half that says so, and it is what fails if anybody writes the
    /// per-row call ITaskSource still offers.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_included_when_waiting_is_asked_for()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.True(row.IsWaiting);
        Assert.Null(row.Excluded);
        // A DateTimeOffset, because the wire carries the offset: reading "+00:00" into a DateTime gives
        // Kind=Local converted to local time, so on a Danish machine this same value would arrive as
        // 16:10 and the assertion would fail for a reason that has nothing to do with Azure DevOps.
        Assert.Equal(
            new DateTimeOffset(new DateTime(2026, 8, 17, 14, 10, 13, 593, DateTimeKind.Utc)),
            row.WaitingSince);
        Assert.Empty(ado.WorkItemRequests);
    }

    /// <summary>
    /// The other half of the round trip claim, and the one that catches the fallback rather than an
    /// unconditional call. ITaskSource says to read the row's field first and to call
    /// FetchStatusChangedAtAsync when it is null - and for this source that call reads the very same
    /// field through the very same parse, so it could only answer null a second time while costing a
    /// round trip for every row whose timestamp was unreadable. Work item 17162 is that row.
    /// </summary>
    [Fact]
    public async Task An_unreadable_state_change_date_is_not_chased_with_a_second_call()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true, waitingStates: ["Active"]);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "17162");

        Assert.True(row.IsWaiting);
        Assert.Null(row.WaitingSince);
        Assert.Empty(ado.WorkItemRequests);
    }

    /// <summary>
    /// Ordinal, measured from the endpoint as well as from the rule: this instance really does keep
    /// states apart that differ only in case, and a fold here would be invisible.
    /// </summary>
    [Fact]
    public async Task A_state_that_differs_only_in_case_is_not_the_waiting_state()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true, waitingStates: ["blocked"]);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.False(row.IsWaiting);
        Assert.Null(row.WaitingSince);
    }

    /// <summary>
    /// The deadline is derived from the clock at import time and not from the row, which is why
    /// AdoImportRow carries no deadline field at all - decision A. The consequence to know: previewing
    /// today and importing tomorrow gives tomorrow's arithmetic, and that is right, because the date is
    /// relative to the import.
    /// </summary>
    [Fact]
    public async Task Importing_writes_the_rows_as_tasks_with_the_derived_deadline()
    {
        await using var ado = await ConfigureAsync();

        var imported = await Import(Row());

        Assert.Equal(1, imported.Imported);

        var task = Assert.Single((await Tasks()).Items);

        Assert.Equal("Kunden kan ikke logge ind", task.Title);
        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Equal(DefaultDeadline, task.Deadline);
        // The link on the task itself, which is a second place from the preview's: the row's URL is
        // built for the preview, this one is rebuilt for the list. An Azure DevOps key is a bare
        // number, so the source has to decide which system's URL shape is asked for.
        Assert.Equal(
            $"{ado.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(FakeAdo.Project)}"
                + "/_workitems/edit/15664",
            task.ExternalUrl);
    }

    [Fact]
    public async Task Zero_days_imports_a_task_without_a_deadline()
    {
        await using var ado = await ConfigureAsync(defaultDeadlineDays: 0);

        await Import(Row());

        Assert.Null(Assert.Single((await Tasks()).Items).Deadline);
    }

    [Fact]
    public async Task A_waiting_row_arrives_as_waiting_for_rather_than_open()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true);

        await Import(Row(
            state: "Blocked",
            // Filled in on purpose: it is the field an implementation would reach for to fill WaitingOn,
            // and without a value here that mistake writes null anyway and the assertion below passes.
            // Measured on the Jira side - WaitingOn = row.Requester killed no test until the fixture
            // had a requester.
            requester: "Anna Andersen",
            waitingSince: "2026-08-17T14:10:13.593Z"));

        var task = Assert.Single((await Tasks()).Items);

        Assert.Equal(TodoStatus.WaitingFor, task.Status);
        // Deliberately empty: a work item assigned to you that sits in a waiting state is waiting on
        // somebody who is not in the AssignedTo field, so the app cannot know who. Section 4a.
        Assert.Null(task.WaitingOn);
    }

    /// <summary>
    /// The setting is authoritative at import time, not at preview time. A row the user previewed while
    /// waiting was allowed must not slip in after they turned it off - the payload carries the state
    /// name, so the server re-derives the decision from the list as it stands now.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_skipped_when_waiting_is_not_asked_for()
    {
        await using var ado = await ConfigureAsync(includeWaiting: false);

        var result = await Import(Row(state: "Blocked"));

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty((await Tasks()).Items);
    }

    /// <summary>
    /// The same rule, measured from the import rather than from the preview, and the pair is what says
    /// there is only one rule. Written out a second time inside the import - which is the shape this
    /// endpoint would have had without AdoStateRoles - a case-insensitive copy would park this row in
    /// "Venter på" and skip it, and every preview assertion above would stay green.
    /// </summary>
    [Fact]
    public async Task An_import_rows_state_is_matched_ordinally_too()
    {
        await using var ado = await ConfigureAsync(waitingStates: ["blocked"]);

        var result = await Import(Row(state: "Blocked"));

        Assert.Equal(1, result.Imported);
        Assert.Equal(TodoStatus.Open, Assert.Single((await Tasks()).Items).Status);
    }

    /// <summary>
    /// The whole reason AdoImportRow carries its work item type. The import never calls Azure DevOps,
    /// so the WIQL cannot do the filtering here - and a client that previewed under an older filter, or
    /// that is not our client at all, must not be able to put a Test Suite on the list.
    /// </summary>
    [Fact]
    public async Task A_row_whose_type_the_user_filtered_out_is_skipped()
    {
        await using var ado = await ConfigureAsync();

        var result = await Import(Row(key: "17169", workItemType: "Test Suite", state: "In Progress"));

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty((await Tasks()).Items);
    }

    /// <summary>
    /// The refusal the preview does not need, because nothing on this path builds a query that could
    /// refuse first. The row is written straight into the settings table rather than through the API on
    /// purpose: SettingsEndpoints refuses an all-blank list, so this is the state only a hand-edited
    /// database can be in - and reading it as "every type" is exactly what slice 11 measured must not
    /// happen, since the absence of a limit is not a neutral default.
    /// </summary>
    [Fact]
    public async Task An_all_blank_type_filter_refuses_the_import_rather_than_taking_everything()
    {
        await using var ado = await ConfigureAsync();

        await OverwriteAsync(SettingKeys.AdoWorkItemTypes, "[\"   \"]");

        var response = await Host.Client.PostAsJsonAsync(
            "/api/ado/import", new { rows = new[] { Row() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoWorkItemTypesRequired,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty((await Tasks()).Items);
    }

    [Fact]
    public async Task Importing_the_same_work_item_twice_skips_it()
    {
        await using var ado = await ConfigureAsync();

        await Import(Row());

        var second = await Import(Row());

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
    }

    [Fact]
    public async Task A_previously_imported_work_item_is_marked_in_the_preview()
    {
        await using var ado = await ConfigureAsync();

        await Import(Row());

        Assert.True(Assert.Single((await Preview()).Rows, r => r.Key == "15664").AlreadyImported);
    }

    /// <summary>
    /// Dedup is scoped by source, and it matters more here than it did for Jira: an Azure DevOps key is
    /// a bare number, so "15664" is a perfectly ordinary key for a retro card or a Jira issue too.
    /// </summary>
    [Fact]
    public async Task A_jira_issue_with_the_same_key_does_not_count_as_imported()
    {
        await using var ado = await ConfigureAsync();

        await Host.AddAndSaveChangesAsync(new TaskItem
        {
            SourceId = "jira",
            ExternalKey = "15664",
            Title = "En sag fra Jira",
            CreatedAt = DateTime.UtcNow,
        });

        Assert.False(Assert.Single((await Preview()).Rows, r => r.Key == "15664").AlreadyImported);
    }

    [Fact]
    public async Task A_row_without_a_title_is_rejected()
    {
        await using var ado = await ConfigureAsync();

        await AssertRefusedAsync(Row(title: "   "), ErrorCodes.AdoRowTitleRequired);
    }

    /// <summary>
    /// The state is what waiting-ness is derived from, so a row without one is not importable. A
    /// required boolean could not be enforced on the wire - an absent bool is <c>false</c>, a legal
    /// value - but an absent string is null, and that can be refused. This is the whole reason the
    /// contract carries <c>state</c> rather than <c>isWaiting</c>.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_state_is_rejected()
    {
        await using var ado = await ConfigureAsync();

        await AssertRefusedAsync(Row(state: null), ErrorCodes.AdoRowStateRequired);
    }

    /// <summary>
    /// Jira has no counterpart, because Jira's import has no filter to re-apply. Refused rather than
    /// skipped: a row without a type would otherwise fall out of the import as "not a type you asked
    /// for", which looks like a lost work item rather than a rejected request.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_work_item_type_is_rejected()
    {
        await using var ado = await ConfigureAsync();

        await AssertRefusedAsync(Row(workItemType: null), ErrorCodes.AdoRowWorkItemTypeRequired);
    }

    /// <summary>
    /// One row as the client sends it: the key, the title, the state and the type, and deliberately no
    /// deadline and no isWaiting - the facts, not the decisions.
    /// </summary>
    /// <summary>
    /// A finished work item that was never imported is kept out rather than brought in as a fresh open
    /// task - decision 5. Shown rather than hidden, the same choice the waiting rows make, so the row
    /// is still on screen with a reason on it.
    /// </summary>
    [Fact]
    public async Task A_done_work_item_that_was_never_imported_is_kept_out()
    {
        await using var ado = await ConfigureAsync(doneStates: ["Active"]);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.Equal(ErrorCodes.AdoExcludedDone, row.Excluded);
        Assert.False(row.SuggestsClosing);
        Assert.False(row.AlreadyImported);
    }

    /// <summary>
    /// The whole point of the feature, walked through the real import rather than seeded: the work item
    /// is imported while Active still means work, then the user calls Active finished, and the next
    /// preview offers to close the task.
    /// </summary>
    [Fact]
    public async Task A_done_work_item_that_was_imported_before_offers_to_close_the_task()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await SetDoneStatesAsync("Active");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.True(row.SuggestsClosing);
        Assert.True(row.AlreadyImported);

        // The reason is decided rather than left out, and this row is where that matters: a body
        // without "reason" deserialises silently to Resolved, because Reason is a non-nullable enum
        // whose zero is Resolved and nothing validates it. So an absent reason and a resolved one are
        // the same thing on the wire, and only a row that carries it lets the client's
        // `?? ClosureReason.Resolved` fallback be taken away.
        Assert.Equal("resolved", row.ClosureReason);

        // Not excluded: the row has something to do, and an excluded row is one that has not.
        Assert.Null(row.Excluded);

        // Azure DevOps' own state change date, off the work item rather than fetched - and the exact
        // value, because a near-enough assertion would also pass on the clock's now.
        Assert.Equal(
            new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero), row.DoneAt);
    }

    /// <summary>
    /// The offer stops once it has been taken. This is the whole reason the preview reads the local
    /// <em>status</em> and not just the set of imported keys: with a key set the same row would suggest
    /// a closure it already got, on every preview, forever.
    /// </summary>
    [Fact]
    public async Task A_task_that_is_already_done_is_not_offered_again()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await SetDoneStatesAsync("Active");
        await CloseAsync(Closure());

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.False(row.SuggestsClosing);
        Assert.True(row.AlreadyImported);
    }

    /// <summary>
    /// The other direction, and the whole of what this half adds: the work item is imported while
    /// Active still means work, the user finishes the task here, and the next preview offers to put it
    /// back. Walked through the real import and the real task route rather than seeded, so the row
    /// being answered about is one the app itself produced.
    ///
    /// No reconciliation is involved, and that is why this half is cheap: a work item still assigned to
    /// the user and not Closed meets the fetch's own two conditions, so it is in this page already -
    /// standing there as alreadyImported with a disabled checkbox until this rule speaks.
    ///
    /// The assertion on <c>reopenTo</c> is not decoration. Active is Actionable, and Open is precisely
    /// what the import loop writes for an Actionable row, so this pins the suggestion to the import's
    /// own mapping rather than to a second copy of it.
    /// </summary>
    [Fact]
    public async Task A_task_finished_here_but_open_in_the_source_suggests_reopening()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await FinishAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.True(row.SuggestsReopening);
        Assert.Equal("open", row.ReopenTo);
        Assert.True(row.AlreadyImported);

        // Deliberately no Assert.False(row.SuggestsClosing) here, though it reads as the obvious
        // companion. Measured: with Actionable in the source and Done here, no single change to the
        // closure rule can make it true - the state is not in the done list and the local task is
        // done, so two of its three conditions have to be inverted at once. An assertion nothing can
        // fell is worse than no assertion. Its twin over in A_task_finished_in_both_places_suggests_
        // nothing is a different fixture and is fellable, and that is where it stands.
    }

    /// <summary>
    /// The local fact negated. The task is still open here, so there is nothing to put back - and the
    /// assertion on <c>alreadyImported</c> is what stops this passing for the wrong reason: without it
    /// a row that never reached the database at all would answer false just as happily.
    ///
    /// <b>And this fixture is the only one that can see the closure rule lose its role test.</b>
    /// Imported, open here, Actionable in the source - so dropping <c>role == Done</c> from
    /// suggestsClosing turns every such row into a closure offer, and measured before the line below
    /// was added, the whole suite stayed green on that. Note that the headline test says the opposite
    /// about the same assertion, and the two are not inconsistent: there the local task is
    /// <em>done</em>, which kills the closure's third condition on its own, so no single change can
    /// make the flag true. Here the local task is open, and the role is all that stands in the way.
    /// </summary>
    [Fact]
    public async Task A_task_that_is_not_finished_here_suggests_nothing()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.True(row.AlreadyImported);
        Assert.False(row.SuggestsReopening);
        Assert.Null(row.ReopenTo);
        Assert.False(row.SuggestsClosing);
    }

    /// <summary>
    /// The source fact negated. Active is called finished, so the two places agree - and offering to
    /// reopen something the source has also finished would be the app arguing with itself.
    ///
    /// Seen to fail on the mutation that drops the role from the condition, leaving it as
    /// <c>localStatus is Done</c>: this and its Jira twin are the only two red in the suite. Worth
    /// naming, because the near mutations do <em>not</em> reach it - rewriting the role half as
    /// <c>role == Actionable</c> leaves this green, since Active is in the done list here.
    /// </summary>
    [Fact]
    public async Task A_task_finished_in_both_places_suggests_nothing()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await FinishAsync();
        await SetDoneStatesAsync("Active");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.False(row.SuggestsReopening);
        Assert.Null(row.ReopenTo);

        // And the closure is not offered either, for the older guard's reason: the local task is
        // already done, so there is nothing left to close. Unlike the same line would be in the
        // headline test above, this one can fall - measured, by inverting the closure's local-status
        // condition, which makes it true on exactly this fixture.
        Assert.False(row.SuggestsClosing);

        // And nothing is excluded either. This is the state where the two guards on Excluded part
        // company: neither suggestion fires here, so the action guard lets the chain run, and it is
        // the done branch's own localStatus test that keeps a row finished in both places from being
        // labelled as one import will skip. Drop that test and this line is what falls.
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The third fact: which status the reopening would restore. Blocked is the waiting state, and
    /// waitingFor is what the import loop writes for a waiting row - so this is the assertion that
    /// holds the suggestion to the import's mapping at its other value. Its twin below arranges
    /// identically and differs in one setting.
    /// </summary>
    [Fact]
    public async Task A_waiting_state_reopens_to_waiting_rather_than_open()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true);

        await Import(Blocked());
        await FinishAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.True(row.SuggestsReopening);
        Assert.Equal("waitingFor", row.ReopenTo);
    }

    /// <summary>
    /// The decision, and the reason it is a test of its own rather than a line in the one above: the
    /// two arrange identically and differ in the toggle alone. The toggle governs what comes
    /// <em>in</em> as new work; a task the user already owns is not held back by a filter for new
    /// ones, and the status matching the source is waitingFor.
    ///
    /// <b>What only this test can catch</b>, stated narrowly because the wide version is not true -
    /// several tests here fall to a bare <c>settings.IncludeWaiting &amp;&amp;</c> in the condition,
    /// since most of them run with the toggle off. It is the <em>narrower</em> wrong guess this one is
    /// alone against: gating only the waiting branch, as in
    /// <c>role != Waiting || settings.IncludeWaiting</c>. That is the likelier mistake, because the
    /// toggle means nothing for any other role - and every other reopening test here is Actionable or
    /// Duty, or has the toggle on, so all of them pass under it. Measured rather than reasoned: with
    /// that clause added in both endpoints, the whole Api suite goes red on exactly two tests - this
    /// one and the Jira twin that says the same thing about the other source.
    /// </summary>
    [Fact]
    public async Task A_waiting_state_reopens_even_when_including_waiting_is_off()
    {
        await using var ado = await ConfigureAsync(includeWaiting: true);

        await Import(Blocked());
        await FinishAsync();
        await TurnIncludeWaitingOffAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "15664");

        Assert.True(row.SuggestsReopening);
        Assert.Equal("waitingFor", row.ReopenTo);

        // And the row is not excluded, which is the second guard this test carries and the only place
        // in the suite the collision can be seen. Excluded is the row's "nothing will happen here"
        // mark, so a row offering a reopening must not carry it. The consequence that exists today is
        // excludedCount, which would count this row into "N left out" while it is being offered, and
        // the label chain, which prints excluded first and would put the skip sentence over the
        // suggestion. The disabled checkbox is tasks 8 and 9's problem, not one this proves.
        //
        // That the toggle is off is checked inside TurnIncludeWaitingOffAsync, and it has to be: this
        // rule makes the row look identical with the toggle on and off, so nothing on it can answer
        // for the arrangement any more. That is the price of the fix, and why the check moved rather
        // than being dropped.
        //
        // No Assert.True(row.IsWaiting) beside it, and the twin above has none either - the
        // asymmetry was resolved by removing it rather than by copying it. It cannot fail on its own:
        // reopenTo is waitingFor exactly when the role is Waiting, so the assertion above it fails
        // first in every case that would have felled this one.
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The first of the four reconciliation outcomes: the work item is Closed, which the fetch cannot
    /// see at all, so nothing but a reconciliation can report it.
    ///
    /// The row is imported with the state the client saw at the time, which is the honest sequence
    /// rather than a trick: 17140 was Active when it was taken in, and it has moved to Closed since.
    /// The import writes what the client sends, so the local task is the one the app itself produced.
    ///
    /// <b>Three assertions carry the fixture's teeth.</b> The title is the <em>source's</em>, not the
    /// one the import wrote, so the row cannot have been built out of the local task alone. The
    /// deadline is null, which an ordinary fetch row for this work item could never be - it would
    /// carry <see cref="DefaultDeadline"/> - so this pins the row's provenance on the reconciliation.
    /// And doneAt is the exact state change date, nine days before the fixed clock, so a row that
    /// forgot the timestamp and fell back on the clock could not pass.
    /// </summary>
    [Fact]
    public async Task A_key_that_fell_out_of_the_fetch_and_is_done_in_the_source_suggests_closing()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.ClosedKey));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeAdo.ClosedKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("resolved", row.ClosureReason);
        Assert.Equal(AdoStateRoles.ClosedState, row.State);
        Assert.Equal("Lukket i kilden", row.Title);
        Assert.Null(row.Deadline);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero), row.DoneAt);

        // Not excluded: the row has something to do, and an excluded row is one that has not.
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The second outcome, and the one the whole feature was asked for: the work item is still open,
    /// but it belongs to somebody else now, so the fetch's <c>[System.AssignedTo] = @Me</c> stops
    /// answering with it and only the ownership question can tell.
    ///
    /// <b>doneAt is null on purpose and is asserted.</b> The item was never finished - it stopped
    /// being the user's - so there is no completion time to carry, and the import falls back on the
    /// clock. Its sibling above asserts the opposite on the same field, which is what makes the pair
    /// say something: the field follows the reason rather than the suggestion.
    /// </summary>
    [Fact]
    public async Task A_key_that_is_no_longer_assigned_to_me_suggests_closing_for_that_reason()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.HandedOverKey));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeAdo.HandedOverKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("unassigned", row.ClosureReason);
        Assert.Equal("Overdraget til en anden", row.Title);
        Assert.Null(row.DoneAt);
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The third outcome, where nothing has changed and the row says so. The lever is the work item
    /// type filter, which is the sequence a user actually walks: 16901 was imported while User Story
    /// was in the filter, and the filter has been narrowed since - so the fetch's WIQL leaves it out
    /// while the reconciliation's batch read and <c>@Me</c> both still find it.
    ///
    /// <c>Assert.Null(row.Deadline)</c> is what says the row came from the reconciliation rather than
    /// from the fetch: a fetched 16901 carries <see cref="DefaultDeadline"/>. Without it a narrowing
    /// that never landed would leave an ordinary row here, and the excluded assertion would fail for
    /// a reason nobody could read off the message.
    /// </summary>
    [Fact]
    public async Task A_key_that_is_still_mine_and_open_is_shown_as_outside_the_filter()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await NarrowWorkItemTypesAsync(ado, "Bug");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "16901");

        Assert.Equal(ErrorCodes.AdoOutsideFilter, row.Excluded);
        Assert.False(row.SuggestsClosing);
        Assert.Null(row.ClosureReason);
        Assert.Equal("Active", row.State);
        Assert.Null(row.Deadline);
    }

    /// <summary>
    /// The fourth outcome. The batch read is asked with <c>errorPolicy=omit</c>, so a work item that
    /// is not there any more comes back as an absence rather than a 400 for the whole request.
    ///
    /// <b>The title is the local task's, and that is the assertion this outcome exists for.</b> No
    /// answer came back to take one from, so the only title there is is the one the import wrote -
    /// which is why the key lookup had to grow past the status. State and type are null for the same
    /// reason, and they are asserted because they are the two fields the contract loosened for it.
    /// </summary>
    [Fact]
    public async Task A_key_the_source_does_not_know_is_shown_as_missing_and_carries_the_local_title()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.UnknownKey, "Den der ikke findes mere"));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeAdo.UnknownKey);

        Assert.Equal(ErrorCodes.AdoMissingInSource, row.Excluded);
        Assert.Equal("Den der ikke findes mere", row.Title);
        Assert.False(row.SuggestsClosing);
        Assert.Null(row.State);
        Assert.Null(row.WorkItemType);
    }

    /// <summary>
    /// What keeps the reconciliation off the archive. A task that is done here is settled whatever the
    /// source says, so asking about it would buy nothing and grow the question by every task the user
    /// has ever finished.
    ///
    /// <b>The second key is the tooth.</b> Without it the test would pass just as happily if the
    /// reconciliation never ran at all - and that is the likelier way to break it than a filter
    /// written the wrong way round. 17140 is reconciled in the same preview, so the absence of 17141
    /// is an absence from a reconciliation that demonstrably happened.
    /// </summary>
    [Fact]
    public async Task A_task_that_is_done_here_is_never_reconciled()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.HandedOverKey, "Den der er færdig her"));
        await Import(Reconcilable(FakeAdo.ClosedKey, "Den der stadig er åben her"));
        await FinishAsync("Den der er færdig her");

        var rows = (await Preview()).Rows;

        Assert.DoesNotContain(rows, r => r.Key == FakeAdo.HandedOverKey);
        Assert.Contains(rows, r => r.Key == FakeAdo.ClosedKey);
    }

    /// <summary>
    /// The order of the two closing branches, and the only test in the suite that can see it: both
    /// outcomes are legal, so swapping them compiles and reads right. 17141 is handed to somebody else
    /// <em>and</em> in a state the user has since called finished, so both branches are true at once -
    /// and resolved is the more precise of the two, which is why it has to win.
    ///
    /// The done list is set after the import rather than in <see cref="ConfigureAsync"/>, because a
    /// row whose state is already in it would be skipped on the way in and there would be no local
    /// task to reconcile.
    ///
    /// <c>doneAt</c> is asserted beside the reason and is not decoration: it follows the reason rather
    /// than the suggestion, so swapping the branches turns it null in the same move.
    /// </summary>
    [Fact]
    public async Task Resolved_outranks_unassigned_when_both_are_true()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.HandedOverKey));
        await SetDoneStatesAsync("Active");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeAdo.HandedOverKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("resolved", row.ClosureReason);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero), row.DoneAt);
    }

    /// <summary>
    /// The safety net. Azure DevOps answers nothing for either key, and the app says so once instead
    /// of printing a row per key claiming the source has never heard of it - because a token that
    /// stopped being allowed to read the project answers exactly like that, and a whole working list
    /// deleted at once is the rarer of the two.
    ///
    /// The false negative is accepted rather than overlooked and is written down in the design's
    /// section 5: delete both of your only two reconciled work items for real and neither is reported.
    /// </summary>
    [Fact]
    public async Task Every_key_missing_at_once_suggests_nothing_because_it_is_more_likely_a_permission()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.UnknownKey, "Den ene"));
        await Import(Reconcilable("999998", "Den anden"));

        var preview = await Preview();

        Assert.Equal(ErrorCodes.AdoNoneRecognized, preview.ReconciliationNotice);
        Assert.DoesNotContain(preview.Rows, r => r.Key == FakeAdo.UnknownKey);
        Assert.DoesNotContain(preview.Rows, r => r.Key == "999998");
    }

    /// <summary>
    /// The tooth in the one above, and the reason the boundary is "all of them" rather than "any of
    /// them": with one key asked, all and one are the same answer, and a single work item that really
    /// was deleted still has to be reported. Written as "at least one absence" the guard would pass
    /// its sibling and kill the outcome the reconciliation exists for.
    ///
    /// Named as reported rather than as suggested, which is what the plan called it: a key the source
    /// does not know carries <c>missingInSource</c> and offers nothing - see the note on the outcome
    /// table in the delivery report.
    /// </summary>
    [Fact]
    public async Task A_single_missing_key_is_still_reported_because_all_and_one_are_the_same_there()
    {
        await using var ado = await ConfigureAsync();

        await Import(Reconcilable(FakeAdo.UnknownKey, "Den eneste"));

        var preview = await Preview();

        Assert.Null(preview.ReconciliationNotice);

        var row = Assert.Single(preview.Rows, r => r.Key == FakeAdo.UnknownKey);

        Assert.Equal(ErrorCodes.AdoMissingInSource, row.Excluded);
    }

    /// <summary>
    /// The completion time is the source's, not the clock's, and the fixture is what gives this teeth:
    /// PUT /api/tasks/{id} sets CompletedAt to the clock's now on every move into Done, so a closure
    /// routed through that path - or one that simply forgot the field - would answer 2026-08-20 here.
    /// The work item changed state on 2026-08-15, five days earlier, and the assertion is on the exact
    /// value.
    /// </summary>
    [Fact]
    public async Task Closing_takes_the_completion_time_from_the_source()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());
        await SetDoneStatesAsync("Active");

        var result = await CloseAsync(Closure());

        Assert.Equal(1, result.Closed);
        Assert.Equal(0, result.Imported);

        var task = Assert.Single((await Tasks(includeCompleted: true)).Items);

        Assert.Equal(TodoStatus.Done, task.Status);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero), task.CompletedAt);
    }

    /// <summary>
    /// The rule is taken again on the way in. A client that previewed under an older done list is the
    /// ordinary case rather than the adversarial one, and the settings as they stand now decide.
    /// </summary>
    [Fact]
    public async Task A_closure_whose_state_is_not_in_the_done_list_is_skipped()
    {
        await using var ado = await ConfigureAsync();

        await Import(UserStory());

        var result = await CloseAsync(Closure());

        Assert.Equal(0, result.Closed);
        Assert.Equal(1, result.Skipped);

        var task = Assert.Single((await Tasks()).Items);

        Assert.Equal(TodoStatus.Open, task.Status);
    }

    [Fact]
    public async Task A_closure_for_a_work_item_that_was_never_imported_is_skipped()
    {
        await using var ado = await ConfigureAsync(doneStates: ["Active"]);

        var result = await CloseAsync(Closure());

        Assert.Equal(0, result.Closed);
        Assert.Equal(1, result.Skipped);
        Assert.Empty((await Tasks()).Items);
    }

    /// <summary>
    /// The same two facts an import row is refused for, with the same two codes: a second pair would be
    /// two more translations for a sentence that already exists.
    /// </summary>
    [Theory]
    [InlineData(null, "Active", ErrorCodes.AdoRowKeyRequired)]
    [InlineData("16901", null, ErrorCodes.AdoRowStateRequired)]
    public async Task A_closure_without_its_two_facts_is_refused(string? key, string? state, string code)
    {
        await using var ado = await ConfigureAsync(doneStates: ["Active"]);

        var response = await Host.Client.PostAsJsonAsync(
            "/api/ado/import", new { rows = Array.Empty<object>(), closures = new[] { new { key, state } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// A finished work item is never written as a new task, even when the client asks. The preview
    /// already marks it excluded, so reaching this needs a client that previewed under an older done
    /// list - which is exactly what the re-applied rule is for.
    /// </summary>
    [Fact]
    public async Task A_done_work_item_is_not_imported_as_a_new_task()
    {
        await using var ado = await ConfigureAsync(doneStates: ["Active"]);

        var result = await Import(UserStory());

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty((await Tasks()).Items);
    }

    /// <summary>Work item 16901, which is Active and therefore importable until Active means done.</summary>
    private static object UserStory() => Row(
        key: "16901",
        title: "Som bruger vil jeg kunne filtrere",
        state: "Active",
        workItemType: "User Story");

    /// <summary>
    /// Work item 15664, the measured Bug, sitting in the state this fixture calls waiting - so the
    /// shared <see cref="Row"/> defaults already name it and only the state has to be said.
    /// </summary>
    private static object Blocked() => Row(state: "Blocked");

    /// <summary>
    /// A row for one of the work items the ordinary fetch cannot reach, so importing it leaves a local
    /// task the next preview has to reconcile.
    ///
    /// The state is <c>Active</c> whatever the work item says now, and that is the honest sequence
    /// rather than a trick: the import writes what the client sends, and what the client saw was the
    /// state before it was closed or handed over. A row carrying the item's current state would be
    /// skipped on the way in - Closed is done, and a done row is never imported - so there would be no
    /// task to reconcile.
    /// </summary>
    private static object Reconcilable(string key, string title = "Hentet mens den stadig var min")
        => Row(key: key, title: title, state: "Active", workItemType: "Task");

    /// <summary>
    /// Finishes the imported task the way the user does, through the task route rather than by writing
    /// a status into the database - so the row the preview then answers about is one the app itself
    /// produced. PUT is a full replacement, and every field it drops on the way is one the preview
    /// never reads: it looks at the source id, the external key and the status.
    /// </summary>
    private Task FinishAsync() => FinishAsync(title: null);

    /// <summary>
    /// The same thing for a fixture holding more than one task, picked by title. A key would read
    /// better, but the task list answers the browse URL rather than the external key, and matching a
    /// key inside a URL would pass for <c>16901</c> against <c>116901</c>.
    /// </summary>
    private async Task FinishAsync(string? title)
    {
        var items = (await Tasks()).Items;
        var task = title is null
            ? Assert.Single(items)
            : Assert.Single(items, t => t.Title == title);
        var response = await Host.Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}", new { title = task.Title, status = "done" });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Narrows the work item type filter through the settings route, which is how a user changes it -
    /// so a work item imported under the old filter falls out of the fetch's WIQL while still being
    /// theirs and still being open, which is the only way to reach the outsideFilter outcome.
    ///
    /// <c>PUT /api/settings</c> is a full replacement, so every Azure DevOps field
    /// <see cref="ConfigureAsync"/> wrote has to be written again. The setting is then read back
    /// through the app: nothing on the reconciled row can say the filter really narrowed, so a write
    /// that never landed would otherwise surface as a puzzling assertion about a deadline.
    /// </summary>
    private async Task NarrowWorkItemTypesAsync(FakeAdo ado, params string[] types)
    {
        var response = await Host.Client.PutAsJsonAsync("/api/settings", new
        {
            adoBaseUrl = ado.BaseUrl,
            adoProject = FakeAdo.Project,
            adoWaitingStates = new[] { "Blocked" },
            adoDoneStates = Array.Empty<string>(),
            adoIncludeWaiting = false,
            adoWorkItemTypes = types,
            adoDefaultDeadlineDays = AdoDefaults.DeadlineDays,
        });

        response.EnsureSuccessStatusCode();

        Assert.Contains(
            $"\"adoWorkItemTypes\":[{string.Join(",", types.Select(type => $"\"{type}\""))}]",
            await Host.Client.GetStringAsync("/api/settings"));
    }

    /// <summary>
    /// Turns the waiting toggle off the way the settings route does: <c>PUT /api/settings</c> stores
    /// the flag as a row when it is on and <b>no row at all</b> when it is off, so removing the row is
    /// the state the app itself can produce - where a stored "false" would be a value nothing writes.
    /// SingleAsync rather than SingleOrDefaultAsync, so a caller that never turned it on fails here
    /// rather than in an assertion about something else.
    ///
    /// It then reads the setting back through the app, and that assertion is load-bearing rather than
    /// belt and braces: a row that suggests a reopening is no longer excluded, so it looks the same
    /// with the toggle on and off, and no assertion the caller can make about the row would notice a
    /// write that never landed. Read off the raw JSON, because the question is what the app answers
    /// and not what the row this method just deleted said.
    /// </summary>
    private async Task TurnIncludeWaitingOffAsync()
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var row = await db.Settings.SingleAsync(s => s.Key == SettingKeys.AdoIncludeWaiting);

        db.Settings.Remove(row);

        await db.SaveChangesAsync();

        Assert.Contains(
            "\"adoIncludeWaiting\":false", await Host.Client.GetStringAsync("/api/settings"));
    }

    private static object Closure(string key = "16901", string state = "Active")
        => new { key, state, doneAt = "2026-08-15T09:00:00Z" };

    /// <summary>
    /// Upserts rather than overwrites, because an empty list is stored as no row at all - so the row
    /// this sets does not exist until somebody picks a state.
    /// </summary>
    private async Task SetDoneStatesAsync(params string[] states)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var value = System.Text.Json.JsonSerializer.Serialize(states);
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKeys.AdoDoneStates);

        if (row is null)
        {
            db.Settings.Add(new Setting { Key = SettingKeys.AdoDoneStates, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
    }

    private async Task<ImportBody> CloseAsync(object closure)
    {
        var response = await Host.Client.PostAsJsonAsync(
            "/api/ado/import", new { rows = Array.Empty<object>(), closures = new[] { closure } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    private static object Row(
        string key = "15664",
        string? title = "Kunden kan ikke logge ind",
        string? state = "Active",
        string? workItemType = "Bug",
        string? requester = null,
        string? waitingSince = null)
        => new { key, title, state, workItemType, requester, waitingSince };

    private async Task AssertRefusedAsync(object row, string code)
    {
        var response = await Host.Client.PostAsJsonAsync(
            "/api/ado/import", new { rows = new[] { row } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// Arrange only, and it writes where the API refuses to: an all-blank list cannot be saved through
    /// PUT /api/settings, so the row this replaces is one only a hand-edited database holds.
    /// </summary>
    private async Task OverwriteAsync(string key, string value)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var row = await db.Settings.SingleAsync(s => s.Key == key);

        row.Value = value;

        await db.SaveChangesAsync();
    }

    private async Task<PreviewBody> Preview()
    {
        var response = await Host.Client.PostAsync("/api/ado/preview", null);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private async Task<ImportBody> Import(object row)
    {
        var response = await Host.Client.PostAsJsonAsync("/api/ado/import", new { rows = new[] { row } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    /// <summary>
    /// The list defaults to hiding what is done, which is exactly what a closed task becomes - so a
    /// closure assertion has to ask for them. Measured: without the flag the collection comes back
    /// empty and the failure reads as if nothing had been written at all.
    /// </summary>
    private async Task<TaskList> Tasks(bool includeCompleted = false)
        => (await Host.Client.GetFromJsonAsync<TaskList>(
            $"/api/tasks?includeCompleted={includeCompleted.ToString().ToLowerInvariant()}"))!;

    private sealed record Connection(string DisplayName);
    private sealed record States(string[] Names);
    private sealed record PreviewBody(PreviewRow[] Rows, int Total, string? ReconciliationNotice);
    private sealed record PreviewRow(
        string Key, string Title, string Url, string? Note, DateOnly? Deadline, string? Requester,
        // Nullable since the reconciliation arrived: a key the source does not know has neither, and
        // no answer came back to take one from.
        string? State, string? WorkItemType, bool IsWaiting, DateTimeOffset? WaitingSince,
        bool AlreadyImported, bool SuggestsClosing,
        // A string rather than the contract's enum, for the reason ReopenTo below gives.
        string? ClosureReason, DateTimeOffset? DoneAt,
        // A string rather than the contract's enum, and for the same reason Excluded is one: what is
        // being asserted is the name on the wire. Reading it into ReopenTarget would let a renamed
        // enum member pass, because the converter would map the new name back to the same value.
        bool SuggestsReopening, string? ReopenTo,
        string? Excluded);
    private sealed record ImportBody(int Imported, int Skipped, int Closed);
    private sealed record TaskList(TaskBody[] Items);

    /// <remarks>
    /// The converter is spelled out because the generated client puts it on each property rather than
    /// on the enum, and the wire names live in JsonStringEnumMemberName, which only the string converter
    /// reads. Without it the default enum converter meets "waitingFor" and throws.
    /// </remarks>
    private sealed record TaskBody(
        long Id,
        string Title,
        [property: JsonConverter(typeof(JsonStringEnumConverter<TodoStatus>))] TodoStatus Status,
        DateOnly? Deadline,
        string? WaitingOn,
        string? ExternalUrl,
        // Read off the wire rather than out of the entity, because the whole question here is which
        // timestamp survived the round trip: the source's, or the clock's now.
        DateTimeOffset? CompletedAt);
}
