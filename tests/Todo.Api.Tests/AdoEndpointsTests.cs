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
        int defaultDeadlineDays = AdoDefaults.DeadlineDays)
    {
        var ado = await FakeAdo.StartAsync();

        await Host.Client.PutAsJsonAsync("/api/settings/ado-token", new { token = FakeAdo.Token });
        await Host.Client.PutAsJsonAsync("/api/settings", new
        {
            adoBaseUrl = ado.BaseUrl,
            adoProject = project,
            adoWaitingStates = waitingStates ?? ["Blocked"],
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

    private async Task<TaskList> Tasks()
        => (await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks"))!;

    private sealed record Connection(string DisplayName);
    private sealed record States(string[] Names);
    private sealed record PreviewBody(PreviewRow[] Rows, int Total);
    private sealed record PreviewRow(
        string Key, string Title, string Url, string? Note, DateOnly? Deadline, string? Requester,
        string State, string WorkItemType, bool IsWaiting, DateTimeOffset? WaitingSince,
        bool AlreadyImported, string? Excluded);
    private sealed record ImportBody(int Imported, int Skipped);
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
        string? ExternalUrl);
}
