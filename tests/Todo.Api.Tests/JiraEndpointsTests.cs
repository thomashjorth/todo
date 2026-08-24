using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Errors;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Tasks;
using Todo.TestSupport.Jira;
using ApiError = Todo.Contracts.ApiError;
// The contract's status enum, not the core one. Todo.Core.Tasks.TodoStatus carries no
// JsonStringEnumConverter, so reading "waitingFor" off the wire into it throws — measured, and it
// is the right type here anyway: this asserts what the API answered, not what the entity holds.
using TodoStatus = Todo.Contracts.TodoStatus;

namespace Todo.Api.Tests;

public class JiraEndpointsTests : ApiTest
{
    /// <summary>
    /// The duty pair defaults to off and empty, so slice 11's seventeen tests measure the same thing
    /// they did before duty existed.
    /// </summary>
    private async Task<FakeJira> ConfigureAsync(
        bool includeWaiting = false,
        string? projectKey = "SAAS",
        string[]? waitingStatuses = null,
        string[]? dutyStatuses = null,
        bool onDuty = false,
        string[]? doneStatuses = null)
    {
        var jira = await FakeJira.StartAsync();

        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = FakeJira.Token });
        await Host.Client.PutAsJsonAsync("/api/settings", new
        {
            jiraBaseUrl = jira.BaseUrl,
            jiraProjectKey = projectKey,
            jiraWaitingStatuses = waitingStatuses ?? ["Afventer general"],
            jiraIncludeWaiting = includeWaiting,
            jiraDutyStatuses = dutyStatuses ?? [],
            jiraOnDuty = onDuty,
            jiraDoneStatuses = doneStatuses ?? [],
        });

        return jira;
    }

    [Fact]
    public async Task Testing_the_connection_reports_who_the_token_belongs_to()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsync("/api/jira/test", null);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Connection>();

        Assert.Equal("Thomas", body!.DisplayName);
    }

    [Fact]
    public async Task Testing_without_a_configured_jira_is_a_bad_request_rather_than_a_crash()
    {
        var response = await Host.Client.PostAsync("/api/jira/test", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraNotConfigured, error!.Code);
    }

    /// <summary>
    /// The user's requirement, and the reason it is a guard rather than a default: the token sees
    /// four projects including a customer one, so an empty project key must refuse rather than
    /// quietly widen the query to everything.
    /// </summary>
    [Fact]
    public async Task An_empty_project_key_refuses_rather_than_importing_every_project()
    {
        await using var jira = await ConfigureAsync(projectKey: null);

        var response = await Host.Client.PostAsync("/api/jira/preview", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraProjectKeyRequired, error!.Code);
        Assert.Empty(jira.SearchRequests);
    }

    [Fact]
    public async Task The_statuses_come_from_the_configured_project()
    {
        await using var jira = await ConfigureAsync();

        var body = await Host.Client.GetFromJsonAsync<Statuses>("/api/jira/statuses");

        Assert.Contains("Afventer general", body!.Names);
        Assert.Contains("Venter på support", body.Names);
    }

    [Fact]
    public async Task The_preview_reports_the_total_the_source_gave()
    {
        await using var jira = await ConfigureAsync();

        var body = await Preview();

        Assert.Equal(3, body.Total);
        Assert.Equal(3, body.Rows.Length);
    }

    /// <summary>
    /// Default off. The waiting row is present and marked excluded rather than missing — hiding it
    /// would look like Jira lost an issue, and it would make the setting invisible.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_shown_as_excluded_when_waiting_is_not_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: false);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsWaiting);
        Assert.Equal(ErrorCodes.JiraExcludedWaiting, row.Excluded);
    }

    [Fact]
    public async Task A_waiting_row_is_included_when_waiting_is_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsWaiting);
        Assert.Null(row.Excluded);
        // A DateTimeOffset, because the wire carries the offset: reading "+00:00" into a DateTime
        // gives Kind=Local converted to local time, so on a Danish machine this same value arrives
        // as 14:10 and the assertion would fail for a reason that has nothing to do with Jira.
        Assert.Equal(
            new DateTimeOffset(new DateTime(2026, 8, 17, 12, 10, 13, 593, DateTimeKind.Utc)),
            row.WaitingSince);

        // Only the waiting row's changelog was read, though the page carried three issues. The
        // changelog is one HTTP call per issue, so reading it for every row would multiply the
        // preview's cost by the page size for values no row would show — and slice 11's task 5 left
        // this assertion to be made here, where the decision to ask is taken.
        Assert.Equal(["SAAS-2"], jira.ChangelogRequests);
    }

    /// <summary>
    /// A status not in the user's list is not waiting, whatever it is called. This is what stops
    /// the code growing a startsWith("Afventer") shortcut — measured 2026-08-18, that heuristic
    /// loses "Venter på support".
    /// </summary>
    [Fact]
    public async Task A_status_outside_the_list_is_not_treated_as_waiting()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true, waitingStatuses: []);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.False(row.IsWaiting);
        Assert.Null(row.Excluded);
        Assert.Null(row.WaitingSince);
    }

    /// <summary>
    /// The comparison is Ordinal, and that is a choice rather than an accident: the status names
    /// come from the instance in the same spelling both ways — the user picks them from
    /// /api/jira/statuses, and they come back on the issues — so nothing legitimate needs a
    /// case-insensitive match. The price of one is real, though. "Afventer Kunden" and "Afventer
    /// kunden" <em>can</em> be two different statuses in Jira, and OrdinalIgnoreCase would fold
    /// them into one where nobody could see it happen.
    ///
    /// This guard exists because a mutation felled nothing: swapping Ordinal for OrdinalIgnoreCase
    /// in the preview left all 166 Api tests green, because every other fixture spells the status
    /// identically on both sides. So the next person weighing OrdinalIgnoreCase can see the choice
    /// was measured rather than arbitrary.
    /// </summary>
    [Fact]
    public async Task A_status_that_differs_only_in_case_is_not_the_waiting_status()
    {
        await using var jira = await ConfigureAsync(
            includeWaiting: true, waitingStatuses: ["afventer general"]);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.False(row.IsWaiting);
        // The half that makes this two-sided: not merely that the row is not waiting, but that the
        // code never went out for a changelog it had no reason to want.
        Assert.Empty(jira.ChangelogRequests);
    }

    /// <summary>
    /// The plan's core decision, and the assertion that makes it real. `Afventer general` means
    /// "waiting for the shared pool"; when you *are* the pool, the issue is waiting for you, so it
    /// has to arrive actionable. Imported as WaitingFor it would land in "Venter på" — hidden from the
    /// deadline sections, which is exactly the work you hold the duty for.
    /// </summary>
    [Fact]
    public async Task A_duty_row_arrives_open_rather_than_waiting()
    {
        await using var jira = await ConfigureAsync(
            dutyStatuses: ["Afventer general"], onDuty: true);

        await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
        });

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Null(task.WaitingOn);
    }

    /// <summary>
    /// The two lists overlap on purpose. This pair — same fixture, opposite switch — is the proof
    /// that the switch decides and not the status.
    /// </summary>
    [Fact]
    public async Task Duty_beats_waiting_when_a_status_is_in_both_lists()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.True(row.IsDuty);
        Assert.False(row.IsWaiting);
        Assert.Null(row.Excluded);
        Assert.Null(row.WaitingSince);
    }

    [Fact]
    public async Task The_same_status_is_waiting_when_off_duty()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: false);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

        Assert.False(row.IsDuty);
        Assert.True(row.IsWaiting);
    }

    /// <summary>
    /// Decision 3. The changelog is one HTTP call per issue, and WaitingSince is only meaningful for
    /// something waiting on somebody else — so a duty row must not pay for one.
    /// </summary>
    [Fact]
    public async Task A_duty_row_does_not_fetch_the_changelog()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: true,
            dutyStatuses: ["Afventer general"],
            onDuty: true);

        await Preview();

        Assert.Empty(jira.ChangelogRequests);
    }

    [Fact]
    public async Task A_row_that_is_not_in_the_duty_list_is_not_marked_as_duty()
    {
        await using var jira = await ConfigureAsync(
            dutyStatuses: ["Afventer general"], onDuty: true);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.False(row.IsDuty);
    }

    [Fact]
    public async Task Importing_writes_the_rows_as_tasks()
    {
        await using var jira = await ConfigureAsync();

        var imported = await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        Assert.Equal(1, imported.Imported);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal("Kunden kan ikke logge ind", task.Title);
        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Equal($"{jira.BaseUrl.TrimEnd('/')}/browse/SAAS-1", task.ExternalUrl);
    }

    [Fact]
    public async Task A_waiting_row_arrives_as_waiting_for_rather_than_open()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        // The row carries Jira's status name, not the waiting decision. The server looks the name
        // up in the user's list — see the note under the import bullet on why a required boolean
        // could not be enforced on the wire.
        await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
            // The reporter is filled in on purpose: it is the field an implementation would reach
            // for to fill WaitingOn, and without a value here that mistake writes null anyway and
            // the assertion below passes. Measured — WaitingOn = row.Requester killed no test until
            // this line existed.
            requester = "Bo Bertelsen",
            waitingSince = "2026-08-17T12:10:13.593Z",
        });

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.WaitingFor, task.Status);
        // WaitingOn is deliberately empty: an issue assigned to you that is waiting is waiting on
        // somebody who is not in the assignee field, so the app cannot know who. Section 4a.
        Assert.Null(task.WaitingOn);
    }

    [Fact]
    public async Task Importing_the_same_issue_twice_skips_it()
    {
        await using var jira = await ConfigureAsync();

        await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        var second = await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
    }

    [Fact]
    public async Task A_previously_imported_issue_is_marked_in_the_preview()
    {
        await using var jira = await ConfigureAsync();

        await Import(new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" });

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.AlreadyImported);
    }

    /// <summary>
    /// Dedup is scoped by source. A retro row and a Jira issue could carry the same key, and one
    /// must not hide the other.
    /// </summary>
    [Fact]
    public async Task A_retro_row_with_the_same_key_does_not_count_as_imported()
    {
        await using var jira = await ConfigureAsync();

        await Host.AddAndSaveChangesAsync(new TaskItem
        {
            SourceId = "retro",
            ExternalKey = "SAAS-1",
            Title = "Et retro-kort",
            CreatedAt = DateTime.UtcNow,
        });

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.False(row.AlreadyImported);
    }

    /// <summary>
    /// The status is valid here on purpose. Without it the row would be rejected for the missing
    /// status instead, and this test would pass while proving nothing about the title.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_title_is_rejected()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import",
            new { rows = new[] { new { key = "SAAS-1", title = "  ", status = "I gang" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraRowTitleRequired, error!.Code);
    }

    /// <summary>
    /// The status is what the server derives waiting-ness from, so a row without one is not
    /// importable. A required boolean could not be enforced on the wire — an absent bool is
    /// `false`, which is a legal value — but an absent string is null, and that can be refused.
    /// This assertion is the whole reason the contract carries `status` rather than `isWaiting`.
    /// </summary>
    [Fact]
    public async Task A_row_without_a_status_is_rejected()
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import", new { rows = new[] { new { key = "SAAS-1", title = "En sag" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.JiraRowStatusRequired, error!.Code);
    }

    /// <summary>
    /// The setting is authoritative at import time, not at preview time. A row the user previewed
    /// while waiting was allowed must not slip in after they turned it off — the payload carries
    /// Jira's status, so the server re-derives the decision from the list as it stands now.
    /// </summary>
    [Fact]
    public async Task A_waiting_row_is_skipped_when_waiting_is_not_asked_for()
    {
        await using var jira = await ConfigureAsync(includeWaiting: false);

        var result = await Import(new
        {
            key = "SAAS-2",
            title = "Venter på svar fra kunden",
            status = "Afventer general",
        });

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");

        Assert.Empty(tasks!.Items);
    }

    /// <summary>
    /// The button on each row needs somewhere to go, and the server owns the URL shape so `/browse/`
    /// is spelled in one place — the same decision as TodoTask.externalUrl.
    /// </summary>
    [Fact]
    public async Task A_preview_row_carries_the_url_of_the_issue()
    {
        await using var jira = await ConfigureAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.Equal($"{jira.BaseUrl.TrimEnd('/')}/browse/SAAS-1", row.Url);
    }

    private async Task<PreviewBody> Preview()
    {
        var response = await Host.Client.PostAsync("/api/jira/preview", null);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private async Task<ImportBody> Import(object row)
    {
        var response = await Host.Client.PostAsJsonAsync("/api/jira/import", new { rows = new[] { row } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    /// <summary>
    /// The Jira half of the closure offer, and the one thing it does differently from Azure DevOps:
    /// the timestamp is not on the issue, so the preview pays a changelog call for it — the same
    /// bargain a waiting row already makes. SAAS-1 last changed status on the 12th at 13:45 +0200,
    /// which is 11:45 UTC, and the assertion is on the exact value.
    /// </summary>
    [Fact]
    public async Task A_done_issue_that_was_imported_before_offers_to_close_the_task()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await SetDoneStatusesAsync("I gang");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.SuggestsClosing);
        Assert.True(row.AlreadyImported);
        Assert.Null(row.Excluded);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 11, 45, 0, TimeSpan.Zero), row.DoneAt);
    }

    /// <summary>
    /// A finished issue that was never imported is kept out rather than brought in as a fresh open
    /// task. Shown rather than hidden, the same choice the waiting rows make.
    /// </summary>
    [Fact]
    public async Task A_done_issue_that_was_never_imported_is_kept_out()
    {
        await using var jira = await ConfigureAsync(doneStatuses: ["I gang"]);

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.Equal(ErrorCodes.JiraExcludedDone, row.Excluded);
        Assert.False(row.SuggestsClosing);
        Assert.False(row.AlreadyImported);
    }

    /// <summary>
    /// The completion time is the changelog's, not the clock's. Without the fixture's six-day gap this
    /// would be a test of nothing: PUT /api/tasks/{id} writes the clock's now on every move into Done,
    /// so a closure that lost its timestamp would answer today and look plausible.
    /// </summary>
    [Fact]
    public async Task Closing_takes_the_completion_time_from_the_changelog()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await SetDoneStatusesAsync("I gang");

        var result = await CloseAsync(new { key = "SAAS-1", status = "I gang", doneAt = "2026-08-12T11:45:00Z" });

        Assert.Equal(1, result.Closed);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks?includeCompleted=true");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.Done, task.Status);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 11, 45, 0, TimeSpan.Zero), task.CompletedAt);
    }

    /// <summary>The rule is taken again on the way in, exactly as Azure DevOps' twin does.</summary>
    [Fact]
    public async Task A_closure_whose_status_is_not_in_the_done_list_is_skipped()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());

        var result = await CloseAsync(new { key = "SAAS-1", status = "I gang", doneAt = (string?)null });

        Assert.Equal(0, result.Closed);
        Assert.Equal(1, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");

        Assert.Equal(TodoStatus.Open, Assert.Single(tasks!.Items).Status);
    }

    private static object InProgressIssue()
        => new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" };

    /// <summary>
    /// Upserts, because an empty list is stored as no row at all — so the row does not exist until
    /// somebody picks a status.
    /// </summary>
    private async Task SetDoneStatusesAsync(params string[] statuses)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var value = System.Text.Json.JsonSerializer.Serialize(statuses);
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKeys.JiraDoneStatuses);

        if (row is null)
        {
            db.Settings.Add(new Setting { Key = SettingKeys.JiraDoneStatuses, Value = value });
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
            "/api/jira/import", new { rows = Array.Empty<object>(), closures = new[] { closure } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    private sealed record Connection(string DisplayName);
    private sealed record Statuses(string[] Names);
    private sealed record PreviewBody(PreviewRow[] Rows, int Total);
    private sealed record PreviewRow(
        string Key, string Title, string Status, bool IsWaiting, bool IsDuty,
        DateTimeOffset? WaitingSince, bool AlreadyImported, bool SuggestsClosing,
        DateTimeOffset? DoneAt, string? Excluded, string Url);
    private sealed record ImportBody(int Imported, int Skipped, int Closed);
    private sealed record TaskList(TaskBody[] Items);

    /// <remarks>
    /// The converter is spelled out because the generated client puts it on each property rather
    /// than on the enum, and the wire names live in JsonStringEnumMemberName, which only the string
    /// converter reads. Without it the default enum converter meets "waitingFor" and throws.
    /// </remarks>
    private sealed record TaskBody(
        long Id,
        string Title,
        [property: JsonConverter(typeof(JsonStringEnumConverter<TodoStatus>))] TodoStatus Status,
        string? WaitingOn,
        string? ExternalUrl,
        // Read off the wire, because the whole question is which timestamp survived the round trip.
        DateTimeOffset? CompletedAt);
}
