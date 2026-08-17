using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.TestSupport.Builders;

namespace Todo.Api.Tests;

public class TaskEndpointsTests : TaskApiTest
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public async Task Created_task_is_returned_by_the_list()
    {
        var created = await CreateAsync("Write the report");

        Assert.Equal("manual", created.SourceId);
        Assert.Equal(TodoStatus.Open, created.Status);
        Assert.Equal(DeadlineBucket.NoDeadline, created.Bucket);
        Assert.Empty(created.SubTasks);

        var listed = Assert.Single(await ListAsync());
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Write the report", listed.Title);
        Assert.Equal(DeadlineBucket.NoDeadline, listed.Bucket);
    }

    [Fact]
    public async Task Created_task_reports_its_location()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = "Buy milk" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        Assert.Equal($"/api/tasks/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Wire_format_uses_the_names_the_contract_declares()
    {
        var yesterday = Today.AddDays(-1);
        await CreateAsync("On the wire", yesterday);

        // A start date cannot be posted yet, so this one is arranged straight in the database.
        var nextWeek = Today.AddDays(7);
        await Host.AddAndSaveChangesAsync(
            new TaskItemBuilder().Titled("Deferred on the wire").DeferredUntil(nextWeek).Build());

        var json = await Client.GetStringAsync("/api/tasks");

        Assert.Contains("\"status\":\"open\"", json);
        Assert.Contains("\"bucket\":\"overdue\"", json);
        Assert.Contains($"\"deadline\":\"{yesterday:yyyy-MM-dd}\"", json);
        Assert.Contains("\"bucket\":\"deferred\"", json);
        Assert.Contains($"\"deferUntil\":\"{nextWeek:yyyy-MM-dd}\"", json);
    }

    [Fact]
    public async Task Task_with_yesterdays_deadline_is_overdue()
    {
        var created = await CreateAsync("Late thing", Today.AddDays(-1));

        Assert.Equal(DeadlineBucket.Overdue, created.Bucket);

        var listed = Assert.Single(await ListAsync());
        Assert.Equal(DeadlineBucket.Overdue, listed.Bucket);
        Assert.Equal(Today.AddDays(-1), listed.Deadline);
    }

    [Fact]
    public async Task Completed_tasks_are_hidden_unless_asked_for()
    {
        var open = await CreateAsync("Still open");
        var done = await CreateAsync("Finished");
        await UpdateAsync(done.Id, "Finished", TodoStatus.Done);

        var visible = Assert.Single(await ListAsync());
        Assert.Equal(open.Id, visible.Id);

        var all = await ListAsync(includeCompleted: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.Id == done.Id);
    }

    [Fact]
    public async Task Waiting_for_someone_stamps_the_day_the_wait_started()
    {
        var created = await CreateAsync("Ask Bo for the numbers");
        Assert.Null(created.WaitingSince);
        Assert.Null(created.WaitingDays);

        var waiting = await UpdateAsync(
            created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");

        Assert.Equal("Bo", waiting.WaitingOn);
        Assert.NotNull(waiting.WaitingSince);
        Assert.Equal(0, waiting.WaitingDays);
    }

    [Fact]
    public async Task Leaving_the_waiting_state_forgets_who_was_waited_on_and_since_when()
    {
        var created = await CreateAsync("Ask Bo for the numbers");
        var waiting = await UpdateAsync(
            created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");
        Assert.NotNull(waiting.WaitingSince);

        var reopened = await UpdateAsync(
            created.Id, "Ask Bo for the numbers", TodoStatus.Open, "Bo");

        Assert.Null(reopened.WaitingOn);
        Assert.Null(reopened.WaitingSince);
        Assert.Null(reopened.WaitingDays);
    }

    [Fact]
    public async Task A_waiting_task_is_listed_without_asking_for_it()
    {
        var created = await CreateAsync("Ask Bo for the numbers");
        await UpdateAsync(created.Id, "Ask Bo for the numbers", TodoStatus.WaitingFor, "Bo");

        var listed = Assert.Single(await ListAsync());
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(TodoStatus.WaitingFor, listed.Status);
    }

    [Fact]
    public async Task Parked_tasks_are_hidden_unless_asked_for()
    {
        var open = await CreateAsync("Still open");
        var parked = await CreateAsync("One day, maybe");
        await UpdateAsync(parked.Id, "One day, maybe", TodoStatus.Someday);

        var visible = Assert.Single(await ListAsync());
        Assert.Equal(open.Id, visible.Id);

        var all = await ListAsync(includeSomeday: true);
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
        await CreateAsync("Still open");
        var done = await CreateAsync("Finished");
        await UpdateAsync(done.Id, "Finished", TodoStatus.Done);
        var parked = await CreateAsync("One day, maybe");
        await UpdateAsync(parked.Id, "One day, maybe", TodoStatus.Someday);

        var listed = await ListAsync(includeCompleted, includeSomeday);

        Assert.Equal([.. expected.Order()], [.. listed.Select(t => t.Title).Order()]);
    }

    [Fact]
    public async Task A_task_nobody_is_waiting_on_has_no_waiting_days()
    {
        var created = await CreateAsync("Write the report");

        Assert.Null(created.WaitingDays);
        Assert.Null(Assert.Single(await ListAsync()).WaitingDays);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Waiting_on_nobody_in_particular_is_stored_as_nothing(string waitingOn)
    {
        var created = await CreateAsync("Ask around");

        var waiting = await UpdateAsync(
            created.Id, "Ask around", TodoStatus.WaitingFor, waitingOn);

        Assert.Null(waiting.WaitingOn);
    }

    [Fact]
    public async Task Completing_a_task_stamps_completed_at_and_reopening_clears_it()
    {
        var created = await CreateAsync("Toggle me");
        Assert.Null(created.CompletedAt);

        var completed = await UpdateAsync(created.Id, "Toggle me", TodoStatus.Done);
        Assert.NotNull(completed.CompletedAt);

        var reopened = await UpdateAsync(created.Id, "Toggle me", TodoStatus.Open);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Updating_a_task_that_stays_done_keeps_the_original_completion_time()
    {
        var created = await CreateAsync("Done once");
        var completed = await UpdateAsync(created.Id, "Done once", TodoStatus.Done);

        var renamed = await UpdateAsync(created.Id, "Done once, renamed", TodoStatus.Done);

        Assert.Equal(completed.CompletedAt, renamed.CompletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creating_a_task_without_a_real_title_is_rejected(string title)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_task_with_no_title_at_all_is_rejected()
    {
        var response = await Client.PostAsJsonAsync("/api/tasks", new { note = "orphan" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_task_with_an_over_long_title_is_rejected()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_task_to_an_empty_title_is_rejected()
    {
        var created = await CreateAsync("Has a title");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{created.Id}",
            new UpdateTodoTaskRequest { Title = " ", Status = TodoStatus.Open });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_an_unknown_task_is_not_found()
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}",
            new UpdateTodoTaskRequest { Title = "Ghost", Status = TodoStatus.Open });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_task_is_not_found()
    {
        var response = await Client.DeleteAsync($"/api/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_task_removes_it()
    {
        var created = await CreateAsync("Temporary");

        var response = await Client.DeleteAsync($"/api/tasks/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ListAsync(includeCompleted: true));
    }

    [Fact]
    public async Task Tasks_with_deadlines_come_before_tasks_without()
    {
        var undated = await CreateAsync("Someday");
        var later = await CreateAsync("Later", Today.AddDays(30));
        var soon = await CreateAsync("Soon", Today.AddDays(1));

        var listed = await ListAsync();

        Assert.Equal([soon.Id, later.Id, undated.Id], listed.Select(t => t.Id));
    }
}
