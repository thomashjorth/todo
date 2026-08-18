using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.Api.Tests;

/// <summary>The calls the task endpoints are exercised through, shared by the tests that make them.</summary>
public abstract class TaskApiTest : ApiTest
{
    protected async Task<TodoTask> CreateAsync(string title, DateOnly? deadline = null)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/tasks", new CreateTodoTaskRequest { Title = title, Deadline = deadline });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(created);
        return created;
    }

    protected async Task<TodoTask> UpdateAsync(
        long id, string title, TodoStatus status, string? waitingOn = null)
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new UpdateTodoTaskRequest { Title = title, Status = status, WaitingOn = waitingOn });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TodoTask>();
        Assert.NotNull(updated);
        return updated;
    }

    protected async Task<IReadOnlyList<TodoTask>> ListAsync(
        bool? includeCompleted = null, bool? includeSomeday = null)
    {
        var url = QueryString(
            ("includeCompleted", includeCompleted), ("includeSomeday", includeSomeday));
        var body = await Client.GetFromJsonAsync<TodoTaskListResponse>($"/api/tasks{url}");

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
