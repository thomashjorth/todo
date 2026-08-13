using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class TaskEndpointsTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

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
        RunningHost host, Guid id, string title, TodoStatus status)
    {
        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{id}", new UpdateTodoTaskRequest { Title = title, Status = status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(updated);
        return updated;
    }

    private static async Task<IReadOnlyList<TodoTask>> ListAsync(
        RunningHost host, bool includeCompleted = false)
    {
        var url = includeCompleted ? "/api/tasks?includeCompleted=true" : "/api/tasks";
        var body = await host.Client.GetFromJsonAsync<TodoTaskListResponse>(url);

        Assert.NotNull(body);
        return [.. body.Items];
    }
}
