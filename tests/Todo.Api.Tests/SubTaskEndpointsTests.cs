using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using TodoDbContext = Todo.Core.Persistence.TodoDbContext;

namespace Todo.Api.Tests;

public class SubTaskEndpointsTests : ApiTest
{
    [Fact]
    public async Task Added_subtasks_are_listed_with_the_task_in_the_order_they_were_added()
    {
        var task = await CreateTaskAsync("Flytning");
        await AddSubTaskAsync(task.Id, "Bestil flyttebil");
        await AddSubTaskAsync(task.Id, "Pak køkkenet");
        await AddSubTaskAsync(task.Id, "Aflæs måler");

        var listed = Assert.Single(await ListAsync());

        Assert.Equal(
            ["Bestil flyttebil", "Pak køkkenet", "Aflæs måler"],
            listed.SubTasks.Select(s => s.Title));
    }

    [Fact]
    public async Task Subtasks_are_listed_by_sort_order_not_by_insertion_order()
    {
        var task = await CreateTaskAsync("Ude af orden");
        var first = await AddSubTaskAsync(task.Id, "Først");
        var second = await AddSubTaskAsync(task.Id, "Sidst");

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            var stored = await db.SubTasks.OrderBy(s => s.SortOrder).ToListAsync();
            Assert.Equal([0, 1], stored.Select(s => s.SortOrder));

            (await db.SubTasks.FirstAsync(s => s.Id == first.Id)).SortOrder = 5;
            await db.SaveChangesAsync();
        }

        var listed = Assert.Single(await ListAsync());

        Assert.Equal([second.Id, first.Id], listed.SubTasks.Select(s => s.Id));
    }

    [Fact]
    public async Task Subtask_sort_order_continues_after_the_highest_one_in_the_task()
    {
        var task = await CreateTaskAsync("Rækkefølge");
        var first = await AddSubTaskAsync(task.Id, "Et");
        await AddSubTaskAsync(task.Id, "To");
        await DeleteSubTaskAsync(task.Id, first.Id);
        await AddSubTaskAsync(task.Id, "Tre");

        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var stored = await db.SubTasks.OrderBy(s => s.SortOrder).ToListAsync();

        Assert.Equal([("To", 1), ("Tre", 2)], stored.Select(s => (s.Title, s.SortOrder)));
    }

    [Fact]
    public async Task Each_task_numbers_its_own_subtasks_from_zero()
    {
        var first = await CreateTaskAsync("Første opgave");
        var second = await CreateTaskAsync("Anden opgave");
        await AddSubTaskAsync(first.Id, "A");
        await AddSubTaskAsync(first.Id, "B");
        var onSecond = await AddSubTaskAsync(second.Id, "C");

        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        Assert.Equal(0, (await db.SubTasks.FirstAsync(s => s.Id == onSecond.Id)).SortOrder);
    }

    [Fact]
    public async Task Created_subtask_reports_its_location()
    {
        var task = await CreateTaskAsync("Med underopgave");

        var response = await Client.PostAsJsonAsync(
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
        var task = await CreateTaskAsync("Tjekliste");
        var subTask = await AddSubTaskAsync(task.Id, "Punkt et");

        var updated = await UpdateSubTaskAsync(task.Id, subTask.Id, "Punkt et", isDone: true);
        Assert.True(updated.IsDone);

        var listed = Assert.Single(await ListAsync());
        Assert.True(Assert.Single(listed.SubTasks).IsDone);
    }

    [Fact]
    public async Task Renaming_a_subtask_keeps_it_ticked()
    {
        var task = await CreateTaskAsync("Tjekliste");
        var subTask = await AddSubTaskAsync(task.Id, "Gammelt navn");
        await UpdateSubTaskAsync(task.Id, subTask.Id, "Gammelt navn", isDone: true);

        var renamed = await UpdateSubTaskAsync(task.Id, subTask.Id, "Nyt navn", isDone: true);

        Assert.Equal("Nyt navn", renamed.Title);
        Assert.True(renamed.IsDone);
    }

    [Fact]
    public async Task Ticking_every_subtask_leaves_the_task_open()
    {
        var task = await CreateTaskAsync("Ikke udtømmende tjekliste");
        var first = await AddSubTaskAsync(task.Id, "Et");
        var second = await AddSubTaskAsync(task.Id, "To");

        await UpdateSubTaskAsync(task.Id, first.Id, "Et", isDone: true);
        await UpdateSubTaskAsync(task.Id, second.Id, "To", isDone: true);

        var listed = Assert.Single(await ListAsync());
        Assert.Equal(TodoStatus.Open, listed.Status);
        Assert.Null(listed.CompletedAt);
    }

    [Fact]
    public async Task Deleting_a_subtask_leaves_the_others_and_the_task_alone()
    {
        var task = await CreateTaskAsync("To punkter");
        var first = await AddSubTaskAsync(task.Id, "Bliver");
        var second = await AddSubTaskAsync(task.Id, "Forsvinder");

        var response = await Client.DeleteAsync($"/api/tasks/{task.Id}/subtasks/{second.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = Assert.Single(await ListAsync());
        Assert.Equal(first.Id, Assert.Single(listed.SubTasks).Id);
    }

    [Fact]
    public async Task Deleting_a_task_deletes_its_subtasks()
    {
        var task = await CreateTaskAsync("Med bagage");
        await AddSubTaskAsync(task.Id, "Punkt et");
        await AddSubTaskAsync(task.Id, "Punkt to");

        var response = await Client.DeleteAsync($"/api/tasks/{task.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        Assert.Empty(await db.SubTasks.ToListAsync());
    }

    [Fact]
    public async Task Adding_a_subtask_to_an_unknown_task_is_not_found()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks/999999/subtasks", new CreateSubTaskRequest { Title = "Forældreløs" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_subtask_belonging_to_another_task_cannot_be_updated()
    {
        var owner = await CreateTaskAsync("Ejer");
        var stranger = await CreateTaskAsync("Fremmed");
        var subTask = await AddSubTaskAsync(owner.Id, "Tilhører ejeren");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{stranger.Id}/subtasks/{subTask.Id}",
            new UpdateSubTaskRequest { Title = "Kapret", IsDone = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var untouched = await ListAsync();
        var subTasks = Assert.Single(untouched, t => t.Id == owner.Id).SubTasks;
        Assert.Equal("Tilhører ejeren", Assert.Single(subTasks).Title);
    }

    [Fact]
    public async Task A_subtask_belonging_to_another_task_cannot_be_deleted()
    {
        var owner = await CreateTaskAsync("Ejer");
        var stranger = await CreateTaskAsync("Fremmed");
        var subTask = await AddSubTaskAsync(owner.Id, "Tilhører ejeren");

        var response = await Client.DeleteAsync(
            $"/api/tasks/{stranger.Id}/subtasks/{subTask.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var subTasks = Assert.Single(await ListAsync(), t => t.Id == owner.Id).SubTasks;
        Assert.Single(subTasks);
    }

    [Fact]
    public async Task Updating_an_unknown_subtask_is_not_found()
    {
        var task = await CreateTaskAsync("Uden underopgaver");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks/999999",
            new UpdateSubTaskRequest { Title = "Spøgelse", IsDone = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_subtask_is_not_found()
    {
        var task = await CreateTaskAsync("Uden underopgaver");

        var response = await Client.DeleteAsync(
            $"/api/tasks/{task.Id}/subtasks/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adding_a_subtask_without_a_real_title_is_rejected(string title)
    {
        var task = await CreateTaskAsync("Har en tjekliste");

        var response = await Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks", new CreateSubTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_subtask_with_an_over_long_title_is_rejected()
    {
        var task = await CreateTaskAsync("Har en tjekliste");

        var response = await Client.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks",
            new CreateSubTaskRequest { Title = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_subtask_to_an_empty_title_is_rejected()
    {
        var task = await CreateTaskAsync("Har en tjekliste");
        var subTask = await AddSubTaskAsync(task.Id, "Rigtigt navn");

        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}/subtasks/{subTask.Id}",
            new UpdateSubTaskRequest { Title = " ", IsDone = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<TodoTask> CreateTaskAsync(string title)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        return created;
    }

    private async Task<TodoSubTask> AddSubTaskAsync(long taskId, string title)
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/subtasks", new CreateSubTaskRequest { Title = title });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(created);
        return created;
    }

    private async Task<TodoSubTask> UpdateSubTaskAsync(
        long taskId, long subTaskId, string title, bool isDone)
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{taskId}/subtasks/{subTaskId}",
            new UpdateSubTaskRequest { Title = title, IsDone = isDone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoSubTask>();
        Assert.NotNull(updated);
        return updated;
    }

    private async Task DeleteSubTaskAsync(long taskId, long subTaskId)
    {
        var response = await Client.DeleteAsync($"/api/tasks/{taskId}/subtasks/{subTaskId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<IReadOnlyList<TodoTask>> ListAsync()
    {
        var body = await Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");

        Assert.NotNull(body);
        return [.. body.Items];
    }
}
