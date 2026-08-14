using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Contracts;
using Todo.Core;
using CoreStatus = Todo.Core.TodoStatus;

namespace Todo.Host;

public static class RetroEndpoints
{
    private const string RetroSource = "retro";
    private const int TitleMaxLength = 500;

    public static IEndpointRouteBuilder MapRetro(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/retro/preview", async Task<Results<Ok<RetroPreviewResponse>, BadRequest<string>>> (
            RetroPreviewRequest request, TodoDbContext db) =>
        {
            RetroParseResult parsed;

            try
            {
                parsed = RetroCsvParser.Parse(request.Csv ?? string.Empty);
            }
            catch (FormatException exception)
            {
                return TypedResults.BadRequest(exception.Message);
            }

            var aliases = await LoadAliasesAsync(db);
            var imported = await ImportedKeysAsync(db, [.. parsed.Rows.Select(r => r.DedupKey)]);

            return TypedResults.Ok(new RetroPreviewResponse
            {
                Rows = [.. parsed.Rows.Select(row => ToContract(row, aliases, imported))],
                SkippedRatingCards = parsed.SkippedRatingCards,
            });
        })
        .WithName("previewRetro")
        .WithTags("Retro")
        .Produces<RetroPreviewResponse>();

        app.MapPost("/api/retro/import", async Task<Results<Ok<RetroImportResponse>, BadRequest<string>>> (
            RetroImportRequest request, TodoDbContext db, IClock clock) =>
        {
            var rows = request.Rows ?? [];

            if (rows.Any(row => string.IsNullOrWhiteSpace(row.Key) || !IsValidTitle(row.Title)))
            {
                return TypedResults.BadRequest("Every row needs a key and a title of at most 500 characters.");
            }

            var known = await ImportedKeysAsync(db, [.. rows.Select(row => row.Key)]);
            var imported = 0;
            var skipped = 0;

            foreach (var row in rows)
            {
                if (!known.Add(row.Key))
                {
                    skipped++;
                    continue;
                }

                db.Tasks.Add(new TaskItem
                {
                    SourceId = RetroSource,
                    ExternalKey = row.Key,
                    Title = row.Title.Trim(),
                    Requester = row.Requester,
                    Deadline = row.Deadline,
                    Status = CoreStatus.Open,
                    CreatedAt = clock.UtcNow,
                });

                imported++;
            }

            await db.SaveChangesAsync();

            return TypedResults.Ok(new RetroImportResponse { Imported = imported, Skipped = skipped });
        })
        .WithName("importRetro")
        .WithTags("Retro")
        .Produces<RetroImportResponse>();

        app.MapGet("/api/retro/aliases", async (TodoDbContext db) =>
            new RetroAliasesResponse { Aliases = [.. await LoadAliasesAsync(db)] })
        .WithName("listRetroAliases")
        .WithTags("Retro")
        .Produces<RetroAliasesResponse>();

        app.MapPut("/api/retro/aliases", async Task<Results<Ok<RetroAliasesResponse>, BadRequest<string>>> (
            RetroAliasesRequest request, TodoDbContext db) =>
        {
            var kept = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var alias in request.Aliases ?? [])
            {
                var value = alias?.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!seen.Add(value))
                {
                    return TypedResults.BadRequest($"'{value}' is listed more than once.");
                }

                kept.Add(value);
            }

            // Two saves: SQLite checks the unique index per statement, so the old rows have to be
            // gone before names that differ only in case can be written back.
            db.Aliases.RemoveRange(await db.Aliases.ToListAsync());
            await db.SaveChangesAsync();

            db.Aliases.AddRange(kept.Select(value => new UserAlias { Value = value }));
            await db.SaveChangesAsync();

            return TypedResults.Ok(new RetroAliasesResponse { Aliases = [.. await LoadAliasesAsync(db)] });
        })
        .WithName("replaceRetroAliases")
        .WithTags("Retro");

        return app;
    }

    private static async Task<List<string>> LoadAliasesAsync(TodoDbContext db)
        => await db.Aliases.OrderBy(a => a.Value).Select(a => a.Value).ToListAsync();

    private static async Task<HashSet<string>> ImportedKeysAsync(TodoDbContext db, List<string> keys)
    {
        var found = await db.Tasks
            .Where(t => t.SourceId == RetroSource && t.ExternalKey != null && keys.Contains(t.ExternalKey))
            .Select(t => t.ExternalKey!)
            .ToListAsync();

        return new HashSet<string>(found, StringComparer.Ordinal);
    }

    private static bool IsValidTitle(string? title)
        => !string.IsNullOrWhiteSpace(title) && title.Length <= TitleMaxLength;

    private static RetroPreviewRow ToContract(
        RetroRow row, IReadOnlyCollection<string> aliases, IReadOnlySet<string> importedKeys) => new()
    {
        Key = row.DedupKey,
        Title = RetroOwnership.StripOwnerPrefix(row.Title, aliases),
        Owner = row.Owner,
        Author = row.Author,
        Zone = row.Zone,
        Deadline = row.DueDate,
        IsMine = RetroOwnership.IsOwnedBy(row.Owner, aliases),
        AlreadyImported = importedKeys.Contains(row.DedupKey),
    };
}
