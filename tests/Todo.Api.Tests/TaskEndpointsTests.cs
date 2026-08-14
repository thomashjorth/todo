using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Time;
using Todo.TestSupport;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

namespace Todo.Api.Tests;

public class TaskEndpointsTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static Task<RunningHost> StartWithClockAsync(FixedClock clock)
        => RunningHost.StartWithAsync(services => services.AddSingleton<IClock>(clock));

    [Fact]
    public async Task Created_task_is_returned_by_the_list()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Write the report");

        Assert.Equal("manual", created.SourceId);
        Assert.Equal(TodoStatus.Open, created.Status);
        Assert.Equal(DeadlineBucket.NoDeadline, created.Bucket);
        Assert.Empty(created.SubTasks);

        var listed = Assert.Single(await ListAsync(host));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Write the report", listed.Title);
        Assert.Equal(DeadlineBucket.NoDeadline, listed.Bucket);
    }

    [Fact]
    public async Task Created_task_reports_its_location()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = "Buy milk" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        Assert.Equal($"/api/tasks/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Wire_format_uses_the_names_the_contract_declares()
    {
        await using var host = await RunningHost.StartAsync();

        var yesterday = Today.AddDays(-1);
        await CreateAsync(host, "On the wire", yesterday);

        var json = await host.Client.GetStringAsync("/api/tasks");

        Assert.Contains("\"status\":\"open\"", json);
        Assert.Contains("\"bucket\":\"overdue\"", json);
        Assert.Contains($"\"deadline\":\"{yesterday:yyyy-MM-dd}\"", json);
    }

    [Fact]
    public async Task Task_with_yesterdays_deadline_is_overdue()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Late thing", Today.AddDays(-1));

        Assert.Equal(DeadlineBucket.Overdue, created.Bucket);

        var listed = Assert.Single(await ListAsync(host));
        Assert.Equal(DeadlineBucket.Overdue, listed.Bucket);
        Assert.Equal(Today.AddDays(-1), listed.Deadline);
    }

    [Fact]
    public async Task Completed_tasks_are_hidden_unless_asked_for()
    {
        await using var host = await RunningHost.StartAsync();

        var open = await CreateAsync(host, "Still open");
        var done = await CreateAsync(host, "Finished");
        await UpdateAsync(host, done.Id, "Finished", TodoStatus.Done);

        var visible = Assert.Single(await ListAsync(host));
        Assert.Equal(open.Id, visible.Id);

        var all = await ListAsync(host, includeCompleted: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == done.Id);
    }

    [Fact]
    public async Task Waiting_for_someone_stamps_the_day_the_wait_started()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Ask Bo for the numbers");
        Assert.Null(created.WaitingSince);
        Assert.Null(created.WaitingDays);

        var waiting = await UpdateAsync(
            host, created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");

        Assert.Equal("Bo", waiting.WaitingOn);
        Assert.NotNull(waiting.WaitingSince);
        Assert.Equal(0, waiting.WaitingDays);
    }

    [Fact]
    public async Task Editing_something_else_on_a_waiting_task_does_not_restart_the_wait()
    {
        var clock = new FixedClock(new DateOnly(2026, 8, 14));
        await using var host = await StartWithClockAsync(clock);

        await host.AddAndSaveChangesAsync(new TaskItemBuilder(clock)
            .Titled("Ask Bo for the numbers")
            .WaitingFor("Bo", clock.UtcNow.AddDays(-5))
            .Build());

        var waiting = Assert.Single(await ListAsync(host));
        Assert.Equal(5, waiting.WaitingDays);

        var renamed = await UpdateAsync(
            host, waiting.Id, "Ask Bo for the numbers again", TodoStatus.WaitingFor, "Bo");

        Assert.Equal(waiting.WaitingSince, renamed.WaitingSince);
        Assert.Equal(5, renamed.WaitingDays);
    }

    [Fact]
    public async Task Leaving_the_waiting_state_forgets_who_was_waited_on_and_since_when()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Ask Bo for the numbers");
        var waiting = await UpdateAsync(
            host, created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");
        Assert.NotNull(waiting.WaitingSince);

        var reopened = await UpdateAsync(
            host, created.Id, "Ask Bo for the numbers", TodoStatus.Open, "Bo");

        Assert.Null(reopened.WaitingOn);
        Assert.Null(reopened.WaitingSince);
        Assert.Null(reopened.WaitingDays);
    }

    [Fact]
    public async Task A_waiting_task_is_listed_without_asking_for_it()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Ask Bo for the numbers");
        await UpdateAsync(host, created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");

        var listed = Assert.Single(await ListAsync(host));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(TodoStatus.WaitingFor, listed.Status);
    }

    [Fact]
    public async Task Parked_tasks_are_hidden_unless_asked_for()
    {
        await using var host = await RunningHost.StartAsync();

        var open = await CreateAsync(host, "Still open");
        var parked = await CreateAsync(host, "One day, maybe");
        await UpdateAsync(host, parked.Id, "One day, maybe", TodoStatus.Someday);

        var visible = Assert.Single(await ListAsync(host));
        Assert.Equal(open.Id, visible.Id);

        var all = await ListAsync(host, includeSomeday: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == parked.Id);
    }

    [Theory]
    [InlineData(false, false, new[] { "Still open" })]
    [InlineData(true, false, new[] { "Still open", "Finished" })]
    [InlineData(false, true, new[] { "Still open", "One day, maybe" })]
    [InlineData(true, true, new[] { "Still open", "Finished", "One day, maybe" })]
    public async Task Completed_and_parked_are_asked_for_separately(
        bool includeCompleted, bool includeSomeday, string[] expected)
    {
        await using var host = await RunningHost.StartAsync();

        await CreateAsync(host, "Still open");
        var done = await CreateAsync(host, "Finished");
        await UpdateAsync(host, done.Id, "Finished", TodoStatus.Done);
        var parked = await CreateAsync(host, "One day, maybe");
        await UpdateAsync(host, parked.Id, "One day, maybe", TodoStatus.Someday);

        var listed = await ListAsync(host, includeCompleted, includeSomeday);

        Assert.Equal([.. expected.Order()], [.. listed.Select(t => t.Title).Order()]);
    }

    [Fact]
    public async Task A_task_nobody_is_waiting_on_has_no_waiting_days()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Write the report");

        Assert.Null(created.WaitingDays);
        Assert.Null(Assert.Single(await ListAsync(host)).WaitingDays);
    }

    [Fact]
    public async Task A_wait_that_started_twelve_days_ago_is_twelve_days_long()
    {
        var clock = new FixedClock(new DateOnly(2026, 8, 14));
        await using var host = await StartWithClockAsync(clock);

        await host.AddAndSaveChangesAsync(new TaskItemBuilder(clock)
            .Titled("Ask Bo for the numbers")
            .WaitingFor("Bo", clock.UtcNow.AddDays(-12))
            .Build());

        var waiting = Assert.Single(await ListAsync(host));

        Assert.Equal("Bo", waiting.WaitingOn);
        Assert.Equal(12, waiting.WaitingDays);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Waiting_on_nobody_in_particular_is_stored_as_nothing(string waitingOn)
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Ask around");

        var waiting = await UpdateAsync(
            host, created.Id, "Ask around", TodoStatus.WaitingFor, waitingOn);

        Assert.Null(waiting.WaitingOn);
    }

    [Fact]
    public async Task Completing_a_task_stamps_completed_at_and_reopening_clears_it()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Toggle me");
        Assert.Null(created.CompletedAt);

        var completed = await UpdateAsync(host, created.Id, "Toggle me", TodoStatus.Done);
        Assert.NotNull(completed.CompletedAt);

        var reopened = await UpdateAsync(host, created.Id, "Toggle me", TodoStatus.Open);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Updating_a_task_that_stays_done_keeps_the_original_completion_time()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Done once");
        var completed = await UpdateAsync(host, created.Id, "Done once", TodoStatus.Done);

        var renamed = await UpdateAsync(host, created.Id, "Done once, renamed", TodoStatus.Done);

        Assert.Equal(completed.CompletedAt, renamed.CompletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creating_a_task_without_a_real_title_is_rejected(string title)
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_task_with_no_title_at_all_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/tasks", new { note = "orphan" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_task_with_an_over_long_title_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_task_to_an_empty_title_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Has a title");

        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{created.Id}",
            new UpdateTodoTaskRequest { Title = " ", Status = TodoStatus.Open });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_an_unknown_task_is_not_found()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}",
            new UpdateTodoTaskRequest { Title = "Ghost", Status = TodoStatus.Open });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_task_is_not_found()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.DeleteAsync($"/api/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_task_removes_it()
    {
        await using var host = await RunningHost.StartAsync();

        var created = await CreateAsync(host, "Temporary");

        var response = await host.Client.DeleteAsync($"/api/tasks/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ListAsync(host, includeCompleted: true));
    }

    [Fact]
    public async Task Tasks_with_deadlines_come_before_tasks_without()
    {
        await using var host = await RunningHost.StartAsync();

        var undated = await CreateAsync(host, "Someday");
        var later = await CreateAsync(host, "Later", Today.AddDays(30));
        var soon = await CreateAsync(host, "Soon", Today.AddDays(1));

        var listed = await ListAsync(host);

        Assert.Equal([soon.Id, later.Id, undated.Id], listed.Select(t => t.Id));
    }

    private static async Task<TodoTask> CreateAsync(
        RunningHost host, string title, DateOnly? deadline = null)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title, Deadline = deadline });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<TodoTask> UpdateAsync(
        RunningHost host, Guid id, string title, TodoStatus status, string? waitingOn = null)
    {
        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new UpdateTodoTaskRequest { Title = title, Status = status, WaitingOn = waitingOn });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(updated);
        return updated;
    }

    private static async Task<IReadOnlyList<TodoTask>> ListAsync(
        RunningHost host, bool? includeCompleted = null, bool? includeSomeday = null)
    {
        var url = QueryString(
            ("includeCompleted", includeCompleted), ("includeSomeday", includeSomeday));
        var body = await host.Client.GetFromJsonAsync<TodoTaskListResponse>($"/api/tasks{url}");

        Assert.NotNull(body);
        return [.. body.Items];
    }

    /// <summary>Leaves out a parameter that was not asked for, so a test can list with none.</summary>
    private static string QueryString(params (string Name, bool? Value)[] parameters)
    {
        var set = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{p.Name}={p.Value.ToString()!.ToLowerInvariant()}")
            .ToList();

        return set.Count == 0 ? string.Empty : $"?{string.Join('&', set)}";
    }
}
