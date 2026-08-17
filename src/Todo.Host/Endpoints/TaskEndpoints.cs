using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Errors;
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
        app.MapGet("/api/tasks", async (
            TodoDbContext db, IClock clock, bool includeCompleted = false, bool includeSomeday = false) =>
        {
            IQueryable<TaskItem> query = db.Tasks.Include(t => t.SubTasks);

            if (!includeCompleted)
            {
                query = query.Where(t => t.Status != CoreStatus.Done);
            }

            if (!includeSomeday)
            {
                query = query.Where(t => t.Status != CoreStatus.Someday);
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

        app.MapPost("/api/tasks", async Task<Results<Created<TodoTask>, BadRequest<ApiError>>> (
            CreateTodoTaskRequest request, TodoDbContext db, IClock clock) =>
        {
            if (ValidateTaskTitle(request.Title) is { } invalid)
            {
                return invalid;
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

        app.MapPut("/api/tasks/{id:guid}", async Task<Results<Ok<TodoTask>, BadRequest<ApiError>, NotFound>> (
            Guid id, UpdateTodoTaskRequest request, TodoDbContext db, IClock clock) =>
        {
            if (ValidateTaskTitle(request.Title) is { } invalid)
            {
                return invalid;
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

                // Only the move into waiting starts the clock, so editing anything else on an
                // item that is already waiting leaves the elapsed days alone.
                task.WaitingSince = status == CoreStatus.WaitingFor ? clock.UtcNow : null;
            }

            task.Title = request.Title;
            task.Note = request.Note;
            task.Deadline = request.Deadline;
            task.Requester = request.Requester;
            task.Status = status;
            task.WaitingOn = status == CoreStatus.WaitingFor ? Trimmed(request.WaitingOn) : null;

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

        app.MapPost("/api/tasks/{id:guid}/subtasks", async Task<Results<Created<TodoSubTask>, BadRequest<ApiError>, NotFound>> (
            Guid id, CreateSubTaskRequest request, TodoDbContext db) =>
        {
            if (ValidateSubTaskTitle(request.Title) is { } invalid)
            {
                return invalid;
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

        app.MapPut("/api/tasks/{id:guid}/subtasks/{subTaskId:guid}", async Task<Results<Ok<TodoSubTask>, BadRequest<ApiError>, NotFound>> (
            Guid id, Guid subTaskId, UpdateSubTaskRequest request, TodoDbContext db) =>
        {
            if (ValidateSubTaskTitle(request.Title) is { } invalid)
            {
                return invalid;
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

    private static BadRequest<ApiError>? ValidateTaskTitle(string? title)
        => ValidateTitle(title, ErrorCodes.TaskTitleRequired, ErrorCodes.TaskTitleTooLong, "task");

    private static BadRequest<ApiError>? ValidateSubTaskTitle(string? title)
        => ValidateTitle(title, ErrorCodes.SubTaskTitleRequired, ErrorCodes.SubTaskTitleTooLong, "subtask");

    private static BadRequest<ApiError>? ValidateTitle(
        string? title, string requiredCode, string tooLongCode, string subject)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return ApiErrors.BadRequest(requiredCode, $"A {subject} needs a title.");
        }

        if (title.Length > TitleMaxLength)
        {
            return ApiErrors.BadRequest(
                tooLongCode, $"A {subject} title may be at most {TitleMaxLength} characters.");
        }

        return null;
    }

    private static TodoTask ToContract(TaskItem task, DateOnly today) => new()
    {
        Id = task.Id,
        SourceId = task.SourceId,
        Title = task.Title,
        Note = task.Note,
        Deadline = task.Deadline,
        DeferUntil = task.DeferUntil,
        Requester = task.Requester,
        Status = ToContract(task.Status),
        Bucket = ToContract(DeadlineBuckets.For(task.Deadline, task.DeferUntil, today)),
        WaitingOn = task.WaitingOn,
        WaitingSince = AsUtc(task.WaitingSince),
        WaitingDays = WaitingDays(task, today),
        CompletedAt = AsUtc(task.CompletedAt),
        CreatedAt = AsUtc(task.CreatedAt),
        SubTasks = [.. task.SubTasks.OrderBy(s => s.SortOrder).Select(ToContract)],
    };

    /// <summary>
    /// How long the wait has lasted, in whole days, which is the signal a date would leave the
    /// reader to work out. Null unless the task is waiting.
    /// </summary>
    private static int? WaitingDays(TaskItem task, DateOnly today)
        => task.Status == CoreStatus.WaitingFor && task.WaitingSince is { } since
            ? today.DayNumber - DateOnly.FromDateTime(since).DayNumber
            : null;

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
        CoreStatus.WaitingFor => ContractStatus.WaitingFor,
        CoreStatus.Someday => ContractStatus.Someday,
        CoreStatus.Done => ContractStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static CoreStatus ToCore(ContractStatus status) => status switch
    {
        ContractStatus.Open => CoreStatus.Open,
        ContractStatus.InProgress => CoreStatus.InProgress,
        ContractStatus.WaitingFor => CoreStatus.WaitingFor,
        ContractStatus.Someday => CoreStatus.Someday,
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
        CoreBucket.Deferred => ContractBucket.Deferred,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };
}
