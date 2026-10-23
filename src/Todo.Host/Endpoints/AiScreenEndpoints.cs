using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Ado;
using Todo.Core.Ai;
using Todo.Core.Jira;
using Todo.Core.Errors;
using Todo.Core.Retro;
using Todo.Core.Sources;
using Todo.Host.Ai;
using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Host.Endpoints;

/// <summary>
/// The AI routes for the retro import, the search field, the import previews and the weekly review:
/// the second round of features, kept apart from the panel's so neither file has to be read whole to
/// change one feature. Like the first round, every route suggests and none saves.
/// </summary>
public static class AiScreenEndpoints
{
    /// <summary>
    /// EmbeddingGemma's own convention for a query, so it is read as a question rather than as a
    /// document. Tasks are embedded as plain text: both document prefixes scored worse (measured).
    /// </summary>
    public const string QueryPrefix = "task: search result | query: ";

    /// <summary>A board export can hold a hundred notes, and every one of them would be prompt to read.</summary>
    private const int MaxRetroNotes = 30;

    public static IEndpointRouteBuilder MapAiOnOtherScreens(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/retro-actions", async Task<Results<Ok<AiRetroActionsResponse>, BadRequest<ApiError>>> (
            AiRetroActionsRequest request, TodoDbContext db, OllamaClient ollama, CancellationToken ct) =>
        {
            var rows = (request.Rows ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Text)).Take(MaxRetroNotes).ToList();

            if (rows.Count == 0)
            {
                return ApiErrors.BadRequest(ErrorCodes.AiTextRequired, "There are no notes to suggest actions for.");
            }

            try
            {
                var answer = await ollama.CompleteAsync<RetroActionsAnswer>(
                    AiPrompts.RetroActions([.. rows.Select(r => r.Text.Trim())]), AiPrompts.RetroActionsSchema(), ct);
                var actions = AiSuggestions.RetroActions(answer.Actions, [.. rows.Select(r => (r.Text, r.Owner))]);
                var aliases = await RetroEndpoints.LoadAliasesAsync(db);

                return TypedResults.Ok(new AiRetroActionsResponse
                {
                    Rows = [.. actions.OrderBy(a => a.Key).Select(a => new AiRetroAction
                    {
                        Key = rows[a.Key].Key,
                        Title = a.Value.Title,
                        Owner = a.Value.Owner,
                        OwnerIsMine = a.Value.Owner is not null && RetroOwnership.IsOwnedBy(a.Value.Owner, aliases),
                    })],
                });
            }
            catch (SourceException exception)
            {
                return ApiErrors.BadRequest(exception.Code, exception.Message);
            }
        })
        .WithName("suggestRetroActions")
        .WithTags("Ai");

        app.MapPost("/api/ai/search", async Task<Results<Ok<AiSearchResponse>, BadRequest<ApiError>>> (
            AiSearchRequest request,
            TodoDbContext db,
            OllamaClient ollama,
            TaskEmbeddings embeddings,
            JiraSettingsReader jiraReader,
            AdoSettingsReader adoReader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return ApiErrors.BadRequest(ErrorCodes.AiTextRequired, "There is nothing to search for.");
            }

            try
            {
                // Every task, done and someday included: finding what was solved before is what the
                // search is for, and 37 of the user's 44 tasks were done when it was measured.
                var tasks = await db.Tasks
                    .Select(t => new { t.Id, t.Title, t.Status, t.SourceId, t.ExternalKey })
                    .ToDictionaryAsync(t => t.Id, ct);
                var vectors = await embeddings.ForAsync(ollama, [.. tasks.Values.Select(t => (t.Id, t.Title))], ct);
                var query = (await ollama.EmbedAsync([QueryPrefix + request.Query.Trim()], ct))[0];
                var jira = await jiraReader.ReadAsync();
                var ado = await adoReader.ReadAsync();

                return TypedResults.Ok(new AiSearchResponse
                {
                    Hits = [.. AiSimilarity.Rank(query, vectors).Select(id => tasks[id]).Select(t => new AiSearchHit
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Status = TaskEndpoints.ToContract(t.Status),
                        ExternalUrl = TaskEndpoints.ExternalUrlOf(t.SourceId, t.ExternalKey, jira, ado),
                    })],
                });
            }
            catch (SourceException exception)
            {
                return ApiErrors.BadRequest(exception.Code, exception.Message);
            }
        })
        .WithName("searchByMeaning")
        .WithTags("Ai");

        app.MapPost("/api/ai/duplicates", async Task<Results<Ok<AiDuplicatesResponse>, BadRequest<ApiError>>> (
            AiDuplicatesRequest request, TodoDbContext db, OllamaClient ollama, TaskEmbeddings embeddings, CancellationToken ct) =>
        {
            var rows = (request.Rows ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Title)).ToList();

            // Another source only: a same-source duplicate is already caught by the import's own key,
            // and would only repeat what the row's "imported earlier" says.
            var others = db.Tasks.Where(t => t.SourceId != request.SourceId);
            var titles = await others.ToDictionaryAsync(t => t.Id, t => t.Title, ct);

            if (rows.Count == 0 || titles.Count == 0)
            {
                return TypedResults.Ok(new AiDuplicatesResponse { Matches = [] });
            }

            try
            {
                // Titles on both sides: the 0.62 was measured on titles, and the cache is shared with
                // the search, so one text per task.
                var candidates = await embeddings.ForAsync(ollama, [.. titles.Select(t => (t.Key, t.Value))], ct);
                var vectors = await ollama.EmbedAsync([.. rows.Select(r => r.Title)], ct);
                var matches = new List<AiDuplicateMatch>();

                for (var i = 0; i < rows.Count; i++)
                {
                    if (AiSimilarity.ClosestDuplicate(vectors[i], candidates) is { } id)
                    {
                        matches.Add(new AiDuplicateMatch { Key = rows[i].Key, TaskId = id, TaskTitle = titles[id] });
                    }
                }

                return TypedResults.Ok(new AiDuplicatesResponse { Matches = matches });
            }
            catch (SourceException exception)
            {
                return ApiErrors.BadRequest(exception.Code, exception.Message);
            }
        })
        .WithName("findDuplicates")
        .WithTags("Ai");

        app.MapPost("/api/ai/review-overview", async Task<Results<Ok<AiReviewOverview>, BadRequest<ApiError>>> (
            TodoDbContext db, OllamaClient ollama, IClock clock, CancellationToken ct) =>
        {
            try
            {
                var answer = await ollama.CompleteAsync<ReviewOverviewAnswer>(
                    AiPrompts.ReviewOverview(await ReviewFactsAsync(db, clock, ct)), AiPrompts.ReviewOverviewSchema(), ct);

                return AiSuggestions.ReviewPoints(answer.Points) is { Count: > 0 } points
                    ? TypedResults.Ok(new AiReviewOverview { Points = [.. points] })
                    : ApiErrors.BadRequest(ErrorCodes.AiUnreadableAnswer, "The model answered with no points.");
            }
            catch (SourceException exception)
            {
                return ApiErrors.BadRequest(exception.Code, exception.Message);
            }
        })
        .WithName("writeReviewOverview")
        .WithTags("Ai");

        return app;
    }

    /// <summary>
    /// The facts the overview is written from. Read here, not by the client: the client only holds
    /// the tasks it has loaded, and completed ones are not among them unless a switch says so.
    /// </summary>
    internal static async Task<ReviewFacts> ReviewFactsAsync(TodoDbContext db, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var staleBefore = now.AddDays(-ReviewFacts.StaleDays);
        var today = clock.Today;

        var waiting = await db.Tasks
            .Where(t => t.Status == CoreStatus.WaitingFor)
            .OrderBy(t => t.WaitingSince == null)
            .ThenBy(t => t.WaitingSince)
            .Take(ReviewFacts.MaxPerList)
            .Select(t => new { t.Title, t.WaitingOn, t.WaitingSince })
            .ToListAsync(ct);

        var overdue = await db.Tasks
            .Where(t => (t.Status == CoreStatus.Open || t.Status == CoreStatus.InProgress) && t.Deadline < today)
            .OrderBy(t => t.Deadline)
            .Take(ReviewFacts.MaxPerList)
            .Select(t => new { t.Title, t.Deadline })
            .ToListAsync(ct);

        var stale = await db.Tasks
            .Where(t => t.Status == CoreStatus.Someday && t.CreatedAt < staleBefore)
            .OrderBy(t => t.CreatedAt)
            .Take(ReviewFacts.MaxPerList)
            .Select(t => new { t.Title, t.CreatedAt })
            .ToListAsync(ct);

        return new ReviewFacts(
            [.. overdue.Select(o => new AgeFact(o.Title, today.DayNumber - o.Deadline!.Value.DayNumber))],
            [.. waiting.Select(w => new WaitingFact(w.Title, w.WaitingOn, w.WaitingSince is { } since ? (int)(now - since).TotalDays : 0))],
            [.. stale.Select(s => new AgeFact(s.Title, (int)(now - s.CreatedAt).TotalDays))]);
    }
}
