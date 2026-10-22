using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Ai;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.Host.Ado;
using Todo.Host.Ai;
using Todo.Host.Jira;

namespace Todo.Host.Endpoints;

/// <summary>
/// The AI routes. Every one of them suggests and none of them saves: the user accepts a suggestion
/// through the ordinary task routes, so a model's answer can never change data without a click.
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAi(this IEndpointRouteBuilder app)
    {
        // Both asked at once, so the answer takes one round trip's time rather than two.
        app.MapGet("/api/ai/status", async (OllamaClient ollama, CancellationToken ct) =>
        {
            var ready = ollama.IsReadyAsync(ct);
            var searchReady = ollama.IsSearchReadyAsync(ct);

            return TypedResults.Ok(new AiStatusResponse { Ready = await ready, SearchReady = await searchReady });
        })
        .WithName("getAiStatus")
        .WithTags("Ai");

        app.MapAiOnOtherScreens();

        app.MapPost("/api/ai/warm", async (OllamaClient ollama, CancellationToken ct) =>
        {
            await ollama.WarmAsync(ct);

            return TypedResults.NoContent();
        })
        .WithName("warmAi")
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

                var title = AiSuggestions.Title(answer.Title, request.Text);

                return TypedResults.Ok(new AiTaskSuggestion
                {
                    Title = title,
                    SubTasks = [.. AiSuggestions.ListedSteps(answer.SubTasks, title)],
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
                    AiPrompts.Split(task.Title, task.Note, existing, LanguageOf(task.SourceId, $"{task.Title} {task.Note}")),
                    AiPrompts.SplitSchema(),
                    ct);

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
                    AiPrompts.Summarize(task.Title, task.Note, LanguageOf(task.SourceId, $"{task.Title} {task.Note}")),
                    AiPrompts.SummarySchema(),
                    ct);

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

        // Asked in the background right after a task is created, so it must never be in the way:
        // with no tags defined it answers at once without asking the model at all.
        app.MapPost("/api/ai/tasks/{id:long}/tag-suggestions", async Task<Results<Ok<AiTagSuggestions>, BadRequest<ApiError>, NotFound>> (
            long id, TodoDbContext db, OllamaClient ollama, CancellationToken ct) =>
        {
            var task = await db.Tasks.Include(t => t.Tags).SingleOrDefaultAsync(t => t.Id == id, ct);

            if (task is null)
            {
                return TypedResults.NotFound();
            }

            var tags = await db.Tags.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(ct);

            if (tags.Count == 0)
            {
                return TypedResults.Ok(new AiTagSuggestions { TagIds = [] });
            }

            try
            {
                var names = tags.Select(t => t.Name).ToList();
                var answer = await ollama.CompleteAsync<TagSuggestionAnswer>(
                    AiPrompts.SuggestTags(task.Title, task.Note, names), AiPrompts.SuggestTagsSchema(names), ct);

                return TypedResults.Ok(new AiTagSuggestions
                {
                    TagIds = [.. AiSuggestions.TagIds(
                        answer.Tags, [.. tags.Select(t => (t.Id, t.Name))], [.. task.Tags.Select(t => t.Id)])],
                });
            }
            catch (SourceException exception)
            {
                return Refused(exception);
            }
        })
        .WithName("suggestTags")
        .WithTags("Ai");

        return app;
    }

    /// <summary>
    /// The language an answer about a task is written in. The source decides for an imported task,
    /// because the user knows how each one is written (2026-10-22): retro boards and Azure DevOps in
    /// English, Jira in Danish. Only a task made in the app is guessed from its text. Public, so the
    /// eval runner asks the model exactly what the app asks it.
    /// </summary>
    public static string LanguageOf(string sourceId, string text) => sourceId switch
    {
        JiraTaskSource.Id => AiLanguage.Danish,
        AdoTaskSource.Id or RetroEndpoints.RetroSource => AiLanguage.English,
        _ => AiLanguage.Of(text),
    };

    private static BadRequest<ApiError> Refused(SourceException exception) =>
        ApiErrors.BadRequest(exception.Code, exception.Message);
}
