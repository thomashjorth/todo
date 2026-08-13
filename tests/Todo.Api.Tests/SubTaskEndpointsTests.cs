using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.TestSupport;
using TodoDbContext = Todo.Core.TodoDbContext;

namespace Todo.Api.Tests;

public class SubTaskEndpointsTests
{
    [Fact]
    public async Task Added_subtasks_are_listed_with_the_task_in_the_order_they_were_added()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Flytning");
        await AddSubTaskAsync(host, task.Id, "Bestil flyttebil");
        await AddSubTaskAsync(host, task.Id, "Pak køkkenet");
        await AddSubTaskAsync(host, task.Id, "Aflæs måler");

        var listed = Assert.Single(await ListAsync(host));

        Assert.Equal(
            ["Bestil flyttebil", "Pak køkkenet", "Aflæs måler"],
            listed.SubTasks.Select(s => s.Title));
    }

    [Fact]
    public async Task Subtasks_are_listed_by_sort_order_not_by_insertion_order()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Ude af orden");
        var first = await AddSubTaskAsync(host, task.Id, "Først");
        var second = await AddSubTaskAsync(host, task.Id, "Sidst");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            var stored = await db.SubTasks.OrderBy(s => s.SortOrder).ToListAsync();
            Assert.Equal([0, 1], stored.Select(s => s.SortOrder));

            (await db.SubTasks.FirstAsync(s => s.Id == first.Id)).SortOrder = 5;
            await db.SaveChangesAsync();
        }

        var listed = Assert.Single(await ListAsync(host));

        Assert.Equal([second.Id, first.Id], listed.SubTasks.Select(s => s.Id));
    }

    [Fact]
    public async Task Subtask_sort_order_continues_after_the_highest_one_in_the_task()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Rækkefølge");
        var first = await AddSubTaskAsync(host, task.Id, "Et");
        await AddSubTaskAsync(host, task.Id, "To");
        await DeleteSubTaskAsync(host, task.Id, first.Id);
        await AddSubTaskAsync(host, task.Id, "Tre");

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var stored = await db.SubTasks.OrderBy(s => s.SortOrder).ToListAsync();

        Assert.Equal([("To", 1), ("Tre", 2)], stored.Select(s => (s.Title, s.SortOrder)));
    }

    [Fact]
    public async Task Each_task_numbers_its_own_subtasks_from_zero()
    {
        await using var host = await RunningHost.StartAsync();

        var first = await CreateTaskAsync(host, "Første opgave");
        var second = await CreateTaskAsync(host, "Anden opgave");
        await AddSubTaskAsync(host, first.Id, "A");
        await AddSubTaskAsync(host, first.Id, "B");
        var onSecond = await AddSubTaskAsync(host, second.Id, "C");

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        Assert.Equal(0, (await db.SubTasks.FirstAsync(s => s.Id == onSecond.Id)).SortOrder);
    }

    [Fact]
    public async Task Created_subtask_reports_its_location()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Med underopgave");

        var response = await host.Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks", new CreateSubTaskRequest { Title = "Trin et" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(created);
        Assert.False(created.IsDone);
        Assert.Equal(
            $"/api/tasks/{task.Id}/subtasks/{created.Id}",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Ticking_a_subtask_is_remembered()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Tjekliste");
        var subTask = await AddSubTaskAsync(host, task.Id, "Punkt et");

        var updated = await UpdateSubTaskAsync(host, task.Id, subTask.Id, "Punkt et", isDone: true);
        Assert.True(updated.IsDone);

        var listed = Assert.Single(await ListAsync(host));
        Assert.True(Assert.Single(listed.SubTasks).IsDone);
    }

    [Fact]
    public async Task Renaming_a_subtask_keeps_it_ticked()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Tjekliste");
        var subTask = await AddSubTaskAsync(host, task.Id, "Gammelt navn");
        await UpdateSubTaskAsync(host, task.Id, subTask.Id, "Gammelt navn", isDone: true);

        var renamed = await UpdateSubTaskAsync(host, task.Id, subTask.Id, "Nyt navn", isDone: true);

        Assert.Equal("Nyt navn", renamed.Title);
        Assert.True(renamed.IsDone);
    }

    [Fact]
    public async Task Ticking_every_subtask_leaves_the_task_open()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Ikke udtømmende tjekliste");
        var first = await AddSubTaskAsync(host, task.Id, "Et");
        var second = await AddSubTaskAsync(host, task.Id, "To");

        await UpdateSubTaskAsync(host, task.Id, first.Id, "Et", isDone: true);
        await UpdateSubTaskAsync(host, task.Id, second.Id, "To", isDone: true);

        var listed = Assert.Single(await ListAsync(host));
        Assert.Equal(TodoStatus.Open, listed.Status);
        Assert.Null(listed.CompletedAt);
    }

    [Fact]
    public async Task Deleting_a_subtask_leaves_the_others_and_the_task_alone()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "To punkter");
        var first = await AddSubTaskAsync(host, task.Id, "Bliver");
        var second = await AddSubTaskAsync(host, task.Id, "Forsvinder");

        var response = await host.Client.DeleteAsync($"/api/tasks/{task.Id}/subtasks/{second.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = Assert.Single(await ListAsync(host));
        Assert.Equal(first.Id, Assert.Single(listed.SubTasks).Id);
    }

    [Fact]
    public async Task Deleting_a_task_deletes_its_subtasks()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Med bagage");
        await AddSubTaskAsync(host, task.Id, "Punkt et");
        await AddSubTaskAsync(host, task.Id, "Punkt to");

        var response = await host.Client.DeleteAsync($"/api/tasks/{task.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        Assert.Empty(await db.SubTasks.ToListAsync());
    }

    [Fact]
    public async Task Adding_a_subtask_to_an_unknown_task_is_not_found()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            $"/api/tasks/{Guid.NewGuid()}/subtasks", new CreateSubTaskRequest { Title = "Forældreløs" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_subtask_belonging_to_another_task_cannot_be_updated()
    {
        await using var host = await RunningHost.StartAsync();

        var owner = await CreateTaskAsync(host, "Ejer");
        var stranger = await CreateTaskAsync(host, "Fremmed");
        var subTask = await AddSubTaskAsync(host, owner.Id, "Tilhører ejeren");

        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{stranger.Id}/subtasks/{subTask.Id}",
            new UpdateSubTaskRequest { Title = "Kapret", IsDone = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var untouched = await ListAsync(host);
        var subTasks = Assert.Single(untouched, t => t.Id == owner.Id).SubTasks;
        Assert.Equal("Tilhører ejeren", Assert.Single(subTasks).Title);
    }

    [Fact]
    public async Task A_subtask_belonging_to_another_task_cannot_be_deleted()
    {
        await using var host = await RunningHost.StartAsync();

        var owner = await CreateTaskAsync(host, "Ejer");
        var stranger = await CreateTaskAsync(host, "Fremmed");
        var subTask = await AddSubTaskAsync(host, owner.Id, "Tilhører ejeren");

        var response = await host.Client.DeleteAsync(
            $"/api/tasks/{stranger.Id}/subtasks/{subTask.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var subTasks = Assert.Single(await ListAsync(host), t => t.Id == owner.Id).SubTasks;
        Assert.Single(subTasks);
    }

    [Fact]
    public async Task Updating_an_unknown_subtask_is_not_found()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Uden underopgaver");

        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks/{Guid.NewGuid()}",
            new UpdateSubTaskRequest { Title = "Spøgelse", IsDone = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_subtask_is_not_found()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Uden underopgaver");

        var response = await host.Client.DeleteAsync(
            $"/api/tasks/{task.Id}/subtasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adding_a_subtask_without_a_real_title_is_rejected(string title)
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Har en tjekliste");

        var response = await host.Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks", new CreateSubTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_subtask_with_an_over_long_title_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Har en tjekliste");

        var response = await host.Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks",
            new CreateSubTaskRequest { Title = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_subtask_to_an_empty_title_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var task = await CreateTaskAsync(host, "Har en tjekliste");
        var subTask = await AddSubTaskAsync(host, task.Id, "Rigtigt navn");

        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks/{subTask.Id}",
            new UpdateSubTaskRequest { Title = " ", IsDone = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<TodoTask> CreateTaskAsync(RunningHost host, string title)
    {
        var response = await host.Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<TodoSubTask> AddSubTaskAsync(
        RunningHost host, Guid taskId, string title)
    {
        var response = await host.Client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/subtasks", new CreateSubTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(created);
        return created;
    }

    private static async Task<TodoSubTask> UpdateSubTaskAsync(
        RunningHost host, Guid taskId, Guid subTaskId, string title, bool isDone)
    {
        var response = await host.Client.PutAsJsonAsync(
            $"/api/tasks/{taskId}/subtasks/{subTaskId}",
            new UpdateSubTaskRequest { Title = title, IsDone = isDone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(updated);
        return updated;
    }

    private static async Task DeleteSubTaskAsync(RunningHost host, Guid taskId, Guid subTaskId)
    {
        var response = await host.Client.DeleteAsync($"/api/tasks/{taskId}/subtasks/{subTaskId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<IReadOnlyList<TodoTask>> ListAsync(RunningHost host)
    {
        var body = await host.Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");

        Assert.NotNull(body);
        return [.. body.Items];
    }
}
