using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Ai;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.Host.Ai;

namespace Todo.Host.Endpoints;

/// <summary>
/// The AI routes. Every one of them suggests and none of them saves: the user accepts a suggestion
/// through the ordinary task routes, so a model's answer can never change data without a click.
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ai/status", async (OllamaClient ollama, CancellationToken ct) =>
            TypedResults.Ok(new AiStatusResponse { Ready = await ollama.IsReadyAsync(ct) }))
            .WithName("getAiStatus")
            .WithTags("Ai");

        app.MapPost("/api/ai/parse-task", async Task<Results<Ok<AiTaskSuggestion>, BadRequest<ApiError>>> (
            AiParseTaskRequest request, OllamaClient ollama, IClock clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return ApiErrors.BadRequest(ErrorCodes.AiTextRequired, "There is no text to read a task from.");
            }

            try
            {
                var answer = await ollama.CompleteAsync<ParsedTaskAnswer>(
                    AiPrompts.ParseTask(request.Text.Trim(), clock.Today), AiPrompts.ParseTaskSchema(), ct);

                return TypedResults.Ok(new AiTaskSuggestion
                {
                    Title = AiSuggestions.Title(answer.Title, request.Text),
                    Requester = AiSuggestions.Requester(answer.Requester),
                    Deadline = AiSuggestions.Deadline(answer.Deadline),
                });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("parseTask")
        .WithTags("Ai");

        app.MapPost("/api/ai/tasks/{id:long}/split", async Task<Results<Ok<AiSubTaskSuggestions>, BadRequest<ApiError>, NotFound>> (
            long id, TodoDbContext db, OllamaClient ollama, CancellationToken ct) =>
        {
            var task = await db.Tasks.Include(t => t.SubTasks).SingleOrDefaultAsync(t => t.Id == id, ct);

            if (task is null)
            {
                return TypedResults.NotFound();
            }

            var existing = task.SubTasks.OrderBy(s => s.SortOrder).Select(s => s.Title).ToList();

            try
            {
                var answer = await ollama.CompleteAsync<SplitAnswer>(
                    AiPrompts.Split(task.Title, task.Note, existing), AiPrompts.SplitSchema(), ct);

                return TypedResults.Ok(new AiSubTaskSuggestions
                {
                    Titles = [.. AiSuggestions.SubTasks(answer.SubTasks, existing)],
                });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("suggestSubTasks")
        .WithTags("Ai");

        app.MapPost("/api/ai/tasks/{id:long}/summary", async Task<Results<Ok<AiNoteSummary>, BadRequest<ApiError>, NotFound>> (
            long id, TodoDbContext db, OllamaClient ollama, CancellationToken ct) =>
        {
            var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == id, ct);

            if (task is null)
            {
                return TypedResults.NotFound();
            }

            if (string.IsNullOrWhiteSpace(task.Note))
            {
                return ApiErrors.BadRequest(ErrorCodes.AiNoteRequired, "The task has no note to summarise.");
            }

            try
            {
                var answer = await ollama.CompleteAsync<SummaryAnswer>(
                    AiPrompts.Summarize(task.Title, task.Note), AiPrompts.SummarySchema(), ct);

                return AiSuggestions.Summary(answer.Summary) is { } summary
                    ? TypedResults.Ok(new AiNoteSummary { Summary = summary })
                    : ApiErrors.BadRequest(ErrorCodes.AiUnreadableAnswer, "The model answered with an empty summary.");
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("summarizeNote")
        .WithTags("Ai");

        return app;
    }

    private static BadRequest<ApiError> Refused(SourceException exception) =>
        ApiErrors.BadRequest(exception.Code, exception.Message);
}
