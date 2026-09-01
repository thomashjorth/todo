using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Errors;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Tasks;
using Todo.Core.Time;
using Todo.TestSupport.Jira;
using Todo.TestSupport.Time;
using ApiError = Todo.Contracts.ApiError;
// The contract's status enum, not the core one. Todo.Core.Tasks.TodoStatus carries no
// JsonStringEnumConverter, so reading "waitingFor" off the wire into it throws — measured, and it
// is the right type here anyway: this asserts what the API answered, not what the entity holds.
using TodoStatus = Todo.Contracts.TodoStatus;

namespace Todo.Api.Tests;

public class JiraEndpointsTests : ApiTest
{
    /// <summary>
    /// Pinned so a handed-over closure - which has no completion time from the source - can be
    /// asserted against the very instant the endpoint saw. Azure DevOps' twin has had one since it was
    /// written; this file ran on the system clock until the reconciliation needed the fallback
    /// measured. The date is FakeJira's own, so nothing that reads a fixture date shifts under it.
    /// </summary>
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 20));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

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
    ///
    /// <c>includeWaiting: false</c>, changed 2026-08-31, and it is the assertion on <c>excluded</c>
    /// that needed it. With the toggle on, that line was null whichever role won, so it could not see
    /// the question this test asks — measured by stripping the other three assertions and deleting
    /// the duty branch outright, whereupon it still passed. With the toggle off the two roles answer
    /// differently: Waiting is excluded, Duty is not. The other three assertions are untouched and
    /// mean what they did, because the toggle changes no role.
    /// </summary>
    [Fact]
    public async Task Duty_beats_waiting_when_a_status_is_in_both_lists()
    {
        await using var jira = await ConfigureAsync(
            waitingStatuses: ["Afventer general"],
            includeWaiting: false,
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

        // The reason is decided rather than left out, and this row is where that matters: a body
        // without "reason" deserialises silently to Resolved, because Reason is a non-nullable enum
        // whose zero is Resolved and nothing validates it. So an absent reason and a resolved one are
        // the same thing on the wire, and only a row that carries it lets the client's
        // `?? ClosureReason.Resolved` fallback be taken away.
        Assert.Equal("resolved", row.ClosureReason);
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
    /// The other direction, walked through the real import and the real task route: the issue is
    /// imported while it is in progress, the user finishes the task here, and the next preview offers
    /// to put it back.
    ///
    /// No reconciliation is involved. An issue still assigned to the user and still unresolved meets
    /// the fetch's own conditions, so it is in this page already - standing as alreadyImported with a
    /// disabled checkbox until this rule speaks. That is why this half costs a condition rather than a
    /// round trip.
    ///
    /// The assertion on <c>reopenTo</c> pins the suggestion to the import's own mapping: "I gang" is
    /// Actionable, and Open is what the import loop writes for an Actionable row.
    /// </summary>
    [Fact]
    public async Task A_task_finished_here_but_open_in_the_source_suggests_reopening()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await FinishAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.SuggestsReopening);
        Assert.Equal("open", row.ReopenTo);
        Assert.True(row.AlreadyImported);

        // Deliberately no Assert.False(row.SuggestsClosing) here, though it reads as the obvious
        // companion. Measured: with Actionable in the source and Done here, no single change to the
        // closure rule can make it true - the status is not in the done list and the local task is
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
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.AlreadyImported);
        Assert.False(row.SuggestsReopening);
        Assert.Null(row.ReopenTo);
        Assert.False(row.SuggestsClosing);
    }

    /// <summary>
    /// The source fact negated. "I gang" is called finished, so the two places agree - and offering to
    /// reopen something the source has also finished would be the app arguing with itself.
    ///
    /// Seen to fail on the mutation that drops the role from the condition, leaving it as
    /// <c>localStatus is Done</c>: this and its Azure DevOps twin are the only two red in the suite.
    /// Worth naming, because the near mutations do <em>not</em> reach it - rewriting the role half as
    /// <c>role == Actionable</c> leaves this green, since "I gang" is in the done list here.
    /// </summary>
    [Fact]
    public async Task A_task_finished_in_both_places_suggests_nothing()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await FinishAsync();
        await SetDoneStatusesAsync("I gang");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.False(row.SuggestsReopening);
        Assert.Null(row.ReopenTo);

        // And the closure is not offered either, for the older guard's reason: the local task is
        // already done, so there is nothing left to close. Unlike the same line would be in the
        // headline test above, this one can fall - measured, by inverting the closure's local-status
        // condition, which makes it true on exactly this fixture. Jira has no
        // A_task_that_is_already_done_is_not_offered_again the way Azure DevOps does, so this is the
        // only Jira assertion on that direction of the closure rule - other Jira tests fall to the
        // same mutation, but from the other side, by losing an offer rather than gaining one.
        Assert.False(row.SuggestsClosing);

        // And nothing is excluded either. This is the state where the two guards on Excluded part
        // company: neither suggestion fires here, so the action guard lets the chain run, and it is
        // the done branch's own localStatus test that keeps a row finished in both places from being
        // labelled as one import will skip. Drop that test and this line is what falls.
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The third fact: which status the reopening would restore. "Afventer general" is the waiting
    /// status, and waitingFor is what the import loop writes for a waiting row - so this holds the
    /// suggestion to the import's mapping at its other value. Its twin below arranges identically and
    /// differs in one setting.
    /// </summary>
    [Fact]
    public async Task A_waiting_state_reopens_to_waiting_rather_than_open()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        await Import(WaitingIssue());
        await FinishAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

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
    /// one and the Azure DevOps twin that says the same thing about the other source.
    /// </summary>
    [Fact]
    public async Task A_waiting_state_reopens_even_when_including_waiting_is_off()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        await Import(WaitingIssue());
        await FinishAsync();
        await TurnIncludeWaitingOffAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-2");

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
    /// The role Azure DevOps has no counterpart for, and it needs no decision of its own: the target is
    /// read off the same mapping the import uses, and that mapping parks only <c>Waiting</c>. So duty
    /// reopens to open, and the reason it is the right answer rather than merely the derived one is the
    /// one the import loop already gives - an issue you hold the duty for waits for <em>you</em>, and
    /// putting it in "Venter på" would hide exactly the work the duty is.
    ///
    /// <c>isDuty</c> is asserted because it is what says the role really was Duty: without it a
    /// settings write that never landed would leave the issue Actionable, which answers open too.
    /// </summary>
    [Fact]
    public async Task A_duty_status_reopens_to_open_because_the_duty_is_the_users_own()
    {
        await using var jira = await ConfigureAsync(dutyStatuses: ["I gang"], onDuty: true);

        await Import(InProgressIssue());
        await FinishAsync();

        var row = Assert.Single((await Preview()).Rows, r => r.Key == "SAAS-1");

        Assert.True(row.IsDuty);
        Assert.True(row.SuggestsReopening);
        Assert.Equal("open", row.ReopenTo);
    }

    /// <summary>
    /// The first of the four reconciliation outcomes. SAAS-6 carries a resolution, which the fetch's
    /// <c>resolution = Unresolved</c> drops, so nothing but a reconciliation can report it - and the
    /// user has since called its status finished.
    ///
    /// <b>The teeth are the title and doneAt.</b> The title is the source's rather than the one the
    /// import wrote, so the row cannot have been built from the local task alone; and doneAt is the
    /// exact changelog entry, which the clock's now could not answer by accident - this suite runs on
    /// the real clock, so a lost timestamp would read as today.
    /// </summary>
    [Fact]
    public async Task A_key_that_fell_out_of_the_fetch_and_is_done_in_the_source_suggests_closing()
    {
        await using var jira = await ConfigureAsync(doneStatuses: ["Løst"]);

        await Import(Reconcilable(FakeJira.ResolvedKey));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeJira.ResolvedKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("resolved", row.ClosureReason);
        Assert.Equal("Løst", row.Status);
        Assert.Equal("Løst i kilden", row.Title);
        Assert.Equal(new DateTimeOffset(2026, 8, 13, 7, 20, 0, TimeSpan.Zero), row.DoneAt);
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The second outcome, and the one the feature was asked for: SAAS-5 is still open but belongs to
    /// somebody else, so <c>assignee = currentUser()</c> stops answering with it and only the
    /// ownership question can tell.
    ///
    /// <c>doneAt</c> is null and is asserted, the opposite of its sibling above on the same field: the
    /// issue was never finished, it stopped being the user's, so there is no completion time and the
    /// import falls back on the clock. The pair says the field follows the reason and not the
    /// suggestion.
    /// </summary>
    [Fact]
    public async Task A_key_that_is_no_longer_assigned_to_me_suggests_closing_for_that_reason()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.HandedOverKey));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeJira.HandedOverKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("unassigned", row.ClosureReason);
        Assert.Equal("Overdraget til en anden", row.Title);
        Assert.Null(row.DoneAt);
        Assert.Null(row.Excluded);
    }

    /// <summary>
    /// The third outcome, where nothing has changed and the row says so. Same issue as the first, one
    /// setting apart: with "Løst" out of the done list the role is actionable, so a resolved issue
    /// that is still the user's is simply one the fetch left out.
    ///
    /// <b>That the two differ in the done list alone is what makes this readable</b> - it is the
    /// resolution clause that keeps SAAS-6 out of the fetch in both, so this cannot be passing because
    /// the issue never left it.
    /// </summary>
    [Fact]
    public async Task A_key_that_is_still_mine_and_open_is_shown_as_outside_the_filter()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.ResolvedKey));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeJira.ResolvedKey);

        Assert.Equal(ErrorCodes.JiraOutsideFilter, row.Excluded);
        Assert.False(row.SuggestsClosing);
        Assert.Null(row.ClosureReason);
        Assert.Equal("Løst", row.Status);
    }

    /// <summary>
    /// The fourth outcome. Jira answers 404 for a key it does not have, and only a 404 is read as an
    /// absence - a 403 is raised, because the app's answer to an absence is to talk about closing
    /// tasks.
    ///
    /// <b>The title is the local task's, and that is the assertion this outcome exists for.</b> No
    /// answer came back to take one from, so the only title there is is the one the import wrote -
    /// which is why the key lookup had to grow past the status. The status is null for the same
    /// reason, and it is the field the contract loosened for it.
    /// </summary>
    [Fact]
    public async Task A_key_the_source_does_not_know_is_shown_as_missing_and_carries_the_local_title()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.DeletedKey, "Den der ikke findes mere"));

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeJira.DeletedKey);

        Assert.Equal(ErrorCodes.JiraMissingInSource, row.Excluded);
        Assert.Equal("Den der ikke findes mere", row.Title);
        Assert.False(row.SuggestsClosing);
        Assert.Null(row.Status);
    }

    /// <summary>
    /// What keeps the reconciliation off the archive. A task finished here is settled whatever Jira
    /// says, and asking anyway would grow the question by every task the user has ever completed - and
    /// Jira pays one call per key.
    ///
    /// <b>The second key is the tooth.</b> Without it this would pass just as happily if the
    /// reconciliation never ran, which is the likelier fault than a filter written the wrong way
    /// round.
    /// </summary>
    [Fact]
    public async Task A_task_that_is_done_here_is_never_reconciled()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.HandedOverKey, "Den der er færdig her"));
        await Import(Reconcilable(FakeJira.ResolvedKey, "Den der stadig er åben her"));
        await FinishAsync("Den der er færdig her");

        var rows = (await Preview()).Rows;

        Assert.DoesNotContain(rows, r => r.Key == FakeJira.HandedOverKey);
        Assert.Contains(rows, r => r.Key == FakeJira.ResolvedKey);
    }

    /// <summary>
    /// The order of the two closing branches, and the only test on this source that can see it: both
    /// outcomes are legal, so swapping them compiles and reads right. SAAS-5 belongs to somebody else
    /// <em>and</em> stands in a status the user has since called finished, so both are true at once,
    /// and resolved is the more precise of the two.
    ///
    /// The done list is set after the import rather than in <see cref="ConfigureAsync"/>, because a
    /// row whose status is already in it would be skipped on the way in, leaving no task to reconcile.
    ///
    /// <c>doneAt</c> is asserted beside the reason because it follows the reason rather than the
    /// suggestion: swapping the branches turns it null in the same move.
    /// </summary>
    [Fact]
    public async Task Resolved_outranks_unassigned_when_both_are_true()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.HandedOverKey));
        await SetDoneStatusesAsync("I gang");

        var row = Assert.Single((await Preview()).Rows, r => r.Key == FakeJira.HandedOverKey);

        Assert.True(row.SuggestsClosing);
        Assert.Equal("resolved", row.ClosureReason);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 6, 5, 0, TimeSpan.Zero), row.DoneAt);
    }

    /// <summary>
    /// The safety net, and Jira is the source it was written for: Jira Data Center hides an issue you
    /// may not browse behind <c>404 Issue Does Not Exist</c> rather than a 403, so a lost project
    /// permission answers exactly like a deleted issue - for every key at once. The app says that once
    /// instead of printing a row per key claiming Jira has never heard of it.
    ///
    /// The false negative is accepted rather than overlooked and is written down in the design's
    /// section 5: delete both of your only two reconciled issues for real and neither is reported.
    /// </summary>
    [Fact]
    public async Task Every_key_missing_at_once_suggests_nothing_because_it_is_more_likely_a_permission()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.DeletedKey, "Den ene"));
        await Import(Reconcilable("SAAS-99", "Den anden"));

        var preview = await Preview();

        Assert.Equal(ErrorCodes.JiraNoneRecognized, preview.ReconciliationNotice);
        Assert.DoesNotContain(preview.Rows, r => r.Key == FakeJira.DeletedKey);
        Assert.DoesNotContain(preview.Rows, r => r.Key == "SAAS-99");
    }

    /// <summary>
    /// The tooth in the one above, and the reason the boundary is "all of them" rather than "any of
    /// them": with one key asked, all and one are the same answer, and a single issue that really was
    /// deleted still has to be reported. Written as "at least one absence" the guard would pass its
    /// sibling and kill the outcome the reconciliation exists for.
    ///
    /// Named as reported rather than as suggested, which is what the plan called it: a key the source
    /// does not know carries <c>missingInSource</c> and offers nothing.
    /// </summary>
    [Fact]
    public async Task A_single_missing_key_is_still_reported_because_all_and_one_are_the_same_there()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.DeletedKey, "Den eneste"));

        var preview = await Preview();

        Assert.Null(preview.ReconciliationNotice);

        var row = Assert.Single(preview.Rows, r => r.Key == FakeJira.DeletedKey);

        Assert.Equal(ErrorCodes.JiraMissingInSource, row.Excluded);
    }

    /// <summary>
    /// The mixed batch, and the half of the boundary its two siblings cannot reach - one asks about
    /// n≥2 with everything absent, the other about n=1, and neither about the middle, which is the
    /// whole of the difference between <c>TrueForAll</c> and <c>Exists</c>. The twin in
    /// AdoEndpointsTests carries the measurement and the note about why the answer-keyed loop has no
    /// tooth of its own; both faults were measured on both sources at once.
    /// </summary>
    [Fact]
    public async Task One_missing_key_among_answered_ones_still_shows_every_row_because_the_notice_needs_all_of_them()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.HandedOverKey, "Den kilden svarer på"));
        await Import(Reconcilable(FakeJira.DeletedKey, "Den kilden ikke kender"));

        var preview = await Preview();

        Assert.Null(preview.ReconciliationNotice);

        var answered = Assert.Single(preview.Rows, r => r.Key == FakeJira.HandedOverKey);
        var missing = Assert.Single(preview.Rows, r => r.Key == FakeJira.DeletedKey);

        // The suggestion survives beside the absence, which is the operational point rather than an
        // extra: the cost of the weakened boundary is not a missing notice but a swallowed offer.
        Assert.True(answered.SuggestsClosing);
        Assert.Equal(ErrorCodes.JiraMissingInSource, missing.Excluded);
    }

    /// <summary>
    /// A refused key costs the reconciliation and nothing else, and Jira is the source that makes it
    /// urgent: only a 404 is read as an absence, so an issue the token may not browse raises. The
    /// fetch has already succeeded by then, so letting the exception out would answer 400 for the
    /// whole screen - one local task, and the import screen is unusable until the user finds and
    /// deletes exactly that task.
    ///
    /// <b>The 200 is the assertion</b>, and the surviving rows are what it is for.
    /// </summary>
    [Fact]
    public async Task A_refused_key_costs_the_reconciliation_rather_than_the_whole_preview()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.ForbiddenKey, "Den tokenet ikke må læse"));

        var response = await Host.Client.PostAsync("/api/jira/preview", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = (await response.Content.ReadFromJsonAsync<PreviewBody>())!;

        // The code travels in the notice rather than as a 400, and jira.refused is already translated
        // in both language files - so the screen can say what happened without a code of its own.
        Assert.Equal(ErrorCodes.JiraRefused, preview.ReconciliationNotice);

        Assert.NotEmpty(preview.Rows);
        Assert.DoesNotContain(preview.Rows, r => r.Reconciled);
    }

    /// <summary>
    /// The flag the "showing N of total" line needs, at both its values in one preview - the twin in
    /// AdoEndpointsTests carries the reasoning. The arithmetic is asserted rather than the flag alone,
    /// because the arithmetic is the thing that was wrong.
    /// </summary>
    [Fact]
    public async Task A_reconciled_row_says_so_because_the_total_counts_only_the_fetch()
    {
        await using var jira = await ConfigureAsync();

        await Import(Reconcilable(FakeJira.HandedOverKey));

        var preview = await Preview();
        var reconciled = Assert.Single(preview.Rows, r => r.Key == FakeJira.HandedOverKey);

        Assert.True(reconciled.Reconciled);
        Assert.Contains(preview.Rows, r => !r.Reconciled);
        Assert.Equal(preview.Total, preview.Rows.Count(r => !r.Reconciled));
        Assert.True(preview.Rows.Length > preview.Total);
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

    /// <summary>
    /// The status check belongs to the resolved reason alone. An issue handed to somebody else is
    /// still in an open status - which is why somebody else now has it - so an unconditional check
    /// would skip every unassigned closure and count it, and the feature would look finished while
    /// doing nothing. AdoEndpointsTests' twin carries the note about what required does not buy.
    /// </summary>
    [Fact]
    public async Task Closing_an_issue_that_was_handed_over_does_not_need_a_done_status()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());

        var result = await CloseAsync(HandedOver());

        Assert.Equal(1, result.Closed);
        Assert.Equal(0, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks?includeCompleted=true");

        Assert.Equal(TodoStatus.Done, Assert.Single(tasks!.Items).Status);
    }

    /// <summary>
    /// A handed-over issue was never finished, so the source has no completion time to give and the
    /// clock stands in. Asserted on the clock's exact value rather than on not-null, which would also
    /// pass an implementation that reached for the row's absent timestamp and got the fallback by
    /// accident.
    /// </summary>
    [Fact]
    public async Task A_handed_over_closure_takes_its_completion_time_from_the_clock()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await CloseAsync(HandedOver());

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks?includeCompleted=true");

        Assert.Equal(Clock.UtcNow, Assert.Single(tasks!.Items).CompletedAt);
    }

    /// <summary>
    /// The tooth is the cleared completion time, and it is only a tooth because the closure above puts
    /// one there first. A fixture that arrived with null would have passed an implementation that
    /// cleared nothing.
    /// </summary>
    [Fact]
    public async Task Reopening_puts_the_status_back_and_clears_the_completion_time()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await CloseAsync(HandedOver());

        var result = await ReopenAsync(Reopening());

        Assert.Equal(1, result.Reopened);
        Assert.Equal(0, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = Assert.Single(tasks!.Items);

        Assert.Equal(TodoStatus.Open, task.Status);
        Assert.Null(task.CompletedAt);
    }

    /// <summary>
    /// The mirror of the closure's own last guard. A task that is open here has nothing to reopen, and
    /// the preview never offers it.
    /// </summary>
    [Fact]
    public async Task Reopening_refuses_a_task_that_is_not_done_here()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());

        var result = await ReopenAsync(Reopening());

        Assert.Equal(0, result.Reopened);
        Assert.Equal(1, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");

        Assert.Equal(TodoStatus.Open, Assert.Single(tasks!.Items).Status);
    }

    /// <summary>
    /// The decision taken again on the server: the client previewed under an older done list, and the
    /// status has since become one that means finished, so reopening would undo what the source now
    /// agrees with.
    /// </summary>
    [Fact]
    public async Task A_reopening_whose_status_is_now_done_is_skipped_and_not_counted()
    {
        await using var jira = await ConfigureAsync();

        await Import(InProgressIssue());
        await CloseAsync(HandedOver());
        await SetDoneStatusesAsync("I gang");

        var result = await ReopenAsync(Reopening());

        Assert.Equal(0, result.Reopened);
        Assert.Equal(1, result.Skipped);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks?includeCompleted=true");

        Assert.Equal(TodoStatus.Done, Assert.Single(tasks!.Items).Status);
    }

    /// <summary>
    /// A waiting status restores the waiting clock with it. WaitingSince travels on the row for the
    /// reason doneAt does: the import makes no call, so the source's timestamp has no other way in.
    /// </summary>
    [Fact]
    public async Task Reopening_into_a_waiting_status_restores_the_waiting_clock()
    {
        await using var jira = await ConfigureAsync(includeWaiting: true);

        await Import(WaitingIssue());
        await CloseAsync(new { key = "SAAS-2", status = "Afventer general", reason = "unassigned", doneAt = (string?)null });

        var result = await ReopenAsync(new
        {
            key = "SAAS-2",
            status = "Afventer general",
            waitingSince = "2026-08-10T07:00:00Z",
        });

        Assert.Equal(1, result.Reopened);

        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");

        Assert.Equal(TodoStatus.WaitingFor, Assert.Single(tasks!.Items).Status);
    }

    /// <summary>
    /// A reopening for a key this source never wrote is skipped rather than matched by key alone - the
    /// same source check the closure makes.
    /// </summary>
    [Fact]
    public async Task A_reopening_for_an_issue_that_was_never_imported_is_skipped()
    {
        await using var jira = await ConfigureAsync();

        var result = await ReopenAsync(Reopening());

        Assert.Equal(0, result.Reopened);
        Assert.Equal(1, result.Skipped);
    }

    /// <summary>The same two facts a closure is refused for, with the same two codes.</summary>
    [Theory]
    [InlineData(null, "I gang", ErrorCodes.JiraRowKeyRequired)]
    [InlineData("SAAS-1", null, ErrorCodes.JiraRowStatusRequired)]
    public async Task A_reopening_without_its_two_facts_is_refused(
        string? key, string? status, string code)
    {
        await using var jira = await ConfigureAsync();

        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import",
            new { rows = Array.Empty<object>(), reopenings = new[] { new { key, status } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// A closure that needs no done status, so a reopening has something to undo. The status is the
    /// issue's own, unchanged - which is the whole point of the unassigned reason.
    /// </summary>
    private static object HandedOver()
        => new { key = "SAAS-1", status = "I gang", reason = "unassigned", doneAt = (string?)null };

    private static object Reopening()
        => new { key = "SAAS-1", status = "I gang", waitingSince = (string?)null };

    private static object InProgressIssue()
        => new { key = "SAAS-1", title = "Kunden kan ikke logge ind", status = "I gang" };

    /// <summary>SAAS-2, the issue sitting in the status this fixture calls waiting.</summary>
    private static object WaitingIssue()
        => new { key = "SAAS-2", title = "Venter på svar fra kunden", status = "Afventer general" };

    /// <summary>
    /// A row for one of the issues the ordinary fetch cannot reach, so importing it leaves a local
    /// task the next preview has to reconcile.
    ///
    /// The status is <c>I gang</c> whatever the issue says now, and that is the honest sequence rather
    /// than a trick: the import writes what the client sends, and what the client saw was the status
    /// before the issue was resolved or handed over. A row carrying a status the user calls finished
    /// would be skipped on the way in, so there would be no task to reconcile.
    /// </summary>
    private static object Reconcilable(string key, string title = "Hentet mens den stadig var min")
        => new { key, title, status = "I gang" };

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
    /// key inside a URL would pass for <c>SAAS-5</c> against <c>SAAS-50</c>.
    /// </summary>
    private async Task FinishAsync(string? title)
    {
        var tasks = await Host.Client.GetFromJsonAsync<TaskList>("/api/tasks");
        var task = title is null
            ? Assert.Single(tasks!.Items)
            : Assert.Single(tasks!.Items, t => t.Title == title);
        var response = await Host.Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}", new { title = task.Title, status = "done" });

        response.EnsureSuccessStatusCode();
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
        var row = await db.Settings.SingleAsync(s => s.Key == SettingKeys.JiraIncludeWaiting);

        db.Settings.Remove(row);

        await db.SaveChangesAsync();

        Assert.Contains(
            "\"jiraIncludeWaiting\":false", await Host.Client.GetStringAsync("/api/settings"));
    }

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

    private async Task<ImportBody> ReopenAsync(object reopening)
    {
        var response = await Host.Client.PostAsJsonAsync(
            "/api/jira/import", new { rows = Array.Empty<object>(), reopenings = new[] { reopening } });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportBody>())!;
    }

    private sealed record Connection(string DisplayName);
    private sealed record Statuses(string[] Names);
    private sealed record PreviewBody(PreviewRow[] Rows, int Total, string? ReconciliationNotice);
    private sealed record PreviewRow(
        // Status is nullable since the reconciliation arrived: a key the source does not know has
        // none, because no answer came back to take one from.
        string Key, string Title, string? Status, bool IsWaiting, bool IsDuty,
        DateTimeOffset? WaitingSince, bool AlreadyImported, bool Reconciled, bool SuggestsClosing,
        // A string rather than the contract's enum, for the reason ReopenTo below gives.
        string? ClosureReason, DateTimeOffset? DoneAt,
        // A string rather than the contract's enum, and for the same reason Excluded is one: what is
        // being asserted is the name on the wire. Reading it into ReopenTarget would let a renamed
        // enum member pass, because the converter would map the new name back to the same value.
        bool SuggestsReopening, string? ReopenTo,
        string? Excluded, string Url);
    private sealed record ImportBody(int Imported, int Skipped, int Closed, int Reopened);
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
