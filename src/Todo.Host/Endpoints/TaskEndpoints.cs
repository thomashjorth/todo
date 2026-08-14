using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ContractBucket = Todo.Contracts.DeadlineBucket;
using ContractStatus = Todo.Contracts.TodoStatus;
using CoreBucket = Todo.Core.Tasks.DeadlineBucket;
using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Host.Endpoints;

public static class TaskEndpoints
{
    private const int TitleMaxLength = 500;

    public static IEndpointRouteBuilder MapTasks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tasks", async (TodoDbContext db, IClock clock, bool includeCompleted = false) =>
        {
            IQueryable<TaskItem> query = db.Tasks.Include(t => t.SubTasks);

            if (!includeCompleted)
            {
                query = query.Where(t => t.Status != CoreStatus.Done);
            }

            var tasks = await query
                .OrderBy(t => t.Deadline == null)
                .ThenBy(t => t.Deadline)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync();

            return new TodoTaskListResponse
            {
                Items = [.. tasks.Select(t => ToContract(t, clock.Today))],
            };
        })
        .WithName("listTasks")
        .WithTags("Tasks")
        .Produces<TodoTaskListResponse>();

        app.MapPost("/api/tasks", async Task<Results<Created<TodoTask>, BadRequest>> (
            CreateTodoTaskRequest request, TodoDbContext db, IClock clock) =>
        {
            if (!IsValidTitle(request.Title))
            {
                return TypedResults.BadRequest();
            }

            var task = new TaskItem
            {
                SourceId = "manual",
                Title = request.Title,
                Note = request.Note,
                Deadline = request.Deadline,
                Requester = request.Requester,
                Status = CoreStatus.Open,
                CreatedAt = clock.UtcNow,
            };

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            return TypedResults.Created($"/api/tasks/{task.Id}", ToContract(task, clock.Today));
        })
        .WithName("createTask")
        .WithTags("Tasks");

        app.MapPut("/api/tasks/{id:guid}", async Task<Results<Ok<TodoTask>, BadRequest, NotFound>> (
            Guid id, UpdateTodoTaskRequest request, TodoDbContext db, IClock clock) =>
        {
            if (!IsValidTitle(request.Title))
            {
                return TypedResults.BadRequest();
            }

            var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
            {
                return TypedResults.NotFound();
            }

            var status = ToCore(request.Status);
            if (status != task.Status)
            {
                task.CompletedAt = status == CoreStatus.Done ? clock.UtcNow : null;
            }

            task.Title = request.Title;
            task.Note = request.Note;
            task.Deadline = request.Deadline;
            task.Requester = request.Requester;
            task.Status = status;

            await db.SaveChangesAsync();

            return TypedResults.Ok(ToContract(task, clock.Today));
        })
        .WithName("updateTask")
        .WithTags("Tasks");

        app.MapDelete("/api/tasks/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, TodoDbContext db) =>
        {
            var task = await db.Tasks.FindAsync(id);
            if (task is null)
            {
                return TypedResults.NotFound();
            }

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();

            return TypedResults.NoContent();
        })
        .WithName("deleteTask")
        .WithTags("Tasks");

        app.MapPost("/api/tasks/{id:guid}/subtasks", async Task<Results<Created<TodoSubTask>, BadRequest, NotFound>> (
            Guid id, CreateSubTaskRequest request, TodoDbContext db) =>
        {
            if (!IsValidTitle(request.Title))
            {
                return TypedResults.BadRequest();
            }

            if (!await db.Tasks.AnyAsync(t => t.Id == id))
            {
                return TypedResults.NotFound();
            }

            var highest = await db.SubTasks
                .Where(s => s.TaskItemId == id)
                .MaxAsync(s => (int?)s.SortOrder);

            var subTask = new SubTask
            {
                TaskItemId = id,
                Title = request.Title,
                SortOrder = highest + 1 ?? 0,
            };

            db.SubTasks.Add(subTask);
            await db.SaveChangesAsync();

            return TypedResults.Created($"/api/tasks/{id}/subtasks/{subTask.Id}", ToContract(subTask));
        })
        .WithName("createSubTask")
        .WithTags("Tasks");

        app.MapPut("/api/tasks/{id:guid}/subtasks/{subTaskId:guid}", async Task<Results<Ok<TodoSubTask>, BadRequest, NotFound>> (
            Guid id, Guid subTaskId, UpdateSubTaskRequest request, TodoDbContext db) =>
        {
            if (!IsValidTitle(request.Title))
            {
                return TypedResults.BadRequest();
            }

            var subTask = await FindSubTaskAsync(db, id, subTaskId);
            if (subTask is null)
            {
                return TypedResults.NotFound();
            }

            // Ticking every box is not the same as finishing the task; only the user closes it.
            subTask.Title = request.Title;
            subTask.IsDone = request.IsDone;

            await db.SaveChangesAsync();

            return TypedResults.Ok(ToContract(subTask));
        })
        .WithName("updateSubTask")
        .WithTags("Tasks");

        app.MapDelete("/api/tasks/{id:guid}/subtasks/{subTaskId:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, Guid subTaskId, TodoDbContext db) =>
        {
            var subTask = await FindSubTaskAsync(db, id, subTaskId);
            if (subTask is null)
            {
                return TypedResults.NotFound();
            }

            db.SubTasks.Remove(subTask);
            await db.SaveChangesAsync();

            return TypedResults.NoContent();
        })
        .WithName("deleteSubTask")
        .WithTags("Tasks");

        return app;
    }

    private static Task<SubTask?> FindSubTaskAsync(TodoDbContext db, Guid taskId, Guid subTaskId)
        => db.SubTasks.FirstOrDefaultAsync(s => s.Id == subTaskId && s.TaskItemId == taskId);

    private static bool IsValidTitle(string? title)
        => !string.IsNullOrWhiteSpace(title) && title.Length <= TitleMaxLength;

    private static TodoTask ToContract(TaskItem task, DateOnly today) => new()
    {
        Id = task.Id,
        SourceId = task.SourceId,
        Title = task.Title,
        Note = task.Note,
        Deadline = task.Deadline,
        Requester = task.Requester,
        Status = ToContract(task.Status),
        Bucket = ToContract(DeadlineBuckets.For(task.Deadline, today)),
        CompletedAt = AsUtc(task.CompletedAt),
        CreatedAt = AsUtc(task.CreatedAt),
        SubTasks = [.. task.SubTasks.OrderBy(s => s.SortOrder).Select(ToContract)],
    };

    private static TodoSubTask ToContract(SubTask subTask) => new()
    {
        Id = subTask.Id,
        Title = subTask.Title,
        IsDone = subTask.IsDone,
    };

    // SQLite hands back DateTime with Kind=Unspecified, which would otherwise be read as local time.
    private static DateTimeOffset AsUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } v ? AsUtc(v) : null;

    private static ContractStatus ToContract(CoreStatus status) => status switch
    {
        CoreStatus.Open => ContractStatus.Open,
        CoreStatus.InProgress => ContractStatus.InProgress,
        CoreStatus.Done => ContractStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static CoreStatus ToCore(ContractStatus status) => status switch
    {
        ContractStatus.Open => CoreStatus.Open,
        ContractStatus.InProgress => CoreStatus.InProgress,
        ContractStatus.Done => CoreStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static ContractBucket ToContract(CoreBucket bucket) => bucket switch
    {
        CoreBucket.Overdue => ContractBucket.Overdue,
        CoreBucket.Today => ContractBucket.Today,
        CoreBucket.ThisWeek => ContractBucket.ThisWeek,
        CoreBucket.Later => ContractBucket.Later,
        CoreBucket.NoDeadline => ContractBucket.NoDeadline,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };
}
