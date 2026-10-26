using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Todo.Core.Errors;
using Todo.Core.Tags;

namespace Todo.Host.Endpoints;

/// <summary>
/// The user's own tags, and which of them a task carries. See the tags design for why a task's tags
/// have a route of their own rather than a field on the task's full-replacement update.
/// </summary>
public static class TagEndpoints
{
    public static IEndpointRouteBuilder MapTags(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tags", async (TodoDbContext db) => TypedResults.Ok(new TagListResponse
        {
            Items = [.. await AllAsync(db)],
        }))
        .WithName("listTags")
        .WithTags("Tags");

        app.MapPost("/api/tags", async Task<Results<Created<TagRef>, BadRequest<ApiError>>> (
            CreateTagRequest request, TodoDbContext db) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;

            if (await NameRefusalAsync(name, db, self: null) is { } refused)
            {
                return refused;
            }

            var tag = new Tag { Name = name, Color = TagColors.Next(await db.Tags.Select(t => t.Color).ToListAsync()) };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            return TypedResults.Created($"/api/tags/{tag.Id}", new TagRef { Id = tag.Id, Name = tag.Name, Color = tag.Color });
        })
        .WithName("createTag")
        .WithTags("Tags");

        app.MapPut("/api/tags/{id:long}", async Task<Results<Ok<TagRef>, BadRequest<ApiError>, NotFound>> (
            long id, UpdateTagRequest request, TodoDbContext db) =>
        {
            var tag = await db.Tags.FindAsync(id);

            if (tag is null)
            {
                return TypedResults.NotFound();
            }

            var name = request.Name?.Trim() ?? string.Empty;

            if (await NameRefusalAsync(name, db, self: id) is { } refused)
            {
                return refused;
            }

            if (request.Color is < 0 or >= TagColors.Count)
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.TagColorInvalid, $"A colour is a slot from 0 to {TagColors.Count - 1}.");
            }

            tag.Name = name;
            tag.Color = request.Color;
            await db.SaveChangesAsync();

            return TypedResults.Ok(new TagRef { Id = tag.Id, Name = tag.Name, Color = tag.Color });
        })
        .WithName("updateTag")
        .WithTags("Tags");

        app.MapDelete("/api/tags/{id:long}", async Task<Results<NoContent, NotFound>> (long id, TodoDbContext db) =>
        {
            // The join rows go with it through the cascade on TaskTags.
            return await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync() == 0
                ? TypedResults.NotFound()
                : TypedResults.NoContent();
        })
        .WithName("deleteTag")
        .WithTags("Tags");

        app.MapPut("/api/tasks/{id:long}/tags", async Task<Results<NoContent, BadRequest<ApiError>, NotFound>> (
            long id, SetTaskTagsRequest request, TodoDbContext db) =>
        {
            var task = await db.Tasks.Include(t => t.Tags).FirstOrDefaultAsync(t => t.Id == id);

            if (task is null)
            {
                return TypedResults.NotFound();
            }

            var wanted = (request.TagIds ?? []).Distinct().ToList();
            var tags = await db.Tags.Where(t => wanted.Contains(t.Id)).ToListAsync();

            if (tags.Count != wanted.Count)
            {
                return ApiErrors.BadRequest(ErrorCodes.TagUnknown, "One of the tags does not exist.");
            }

            task.Tags.Clear();
            task.Tags.AddRange(tags);
            await db.SaveChangesAsync();

            return TypedResults.NoContent();
        })
        .WithName("setTaskTags")
        .WithTags("Tags");

        return app;
    }

    /// <summary>
    /// Why a name cannot be a tag's, or nothing. Asked before the write, so the answer is a code the
    /// user can read rather than the unique index's exception. The index is NOCASE, and so is the
    /// comparison; <paramref name="self"/> is left out, so a tag may take its own name in another case.
    /// </summary>
    private static async Task<BadRequest<ApiError>?> NameRefusalAsync(string name, TodoDbContext db, long? self)
    {
        if (name.Length == 0)
        {
            return ApiErrors.BadRequest(ErrorCodes.TagNameRequired, "A tag needs a name.");
        }

        if (name.Length > Tag.NameMaxLength)
        {
            return ApiErrors.BadRequest(
                ErrorCodes.TagNameTooLong, $"A tag name may be at most {Tag.NameMaxLength} characters.");
        }

        var lower = name.ToLowerInvariant();

        return await db.Tags.AnyAsync(t => t.Id != self && t.Name.ToLower() == lower)
            ? ApiErrors.BadRequest(ErrorCodes.TagDuplicate, "A tag of that name exists already.")
            : null;
    }

    internal static async Task<List<TagRef>> AllAsync(TodoDbContext db) =>
        [.. (await db.Tags.OrderBy(t => t.Name).ToListAsync()).Select(t => new TagRef { Id = t.Id, Name = t.Name, Color = t.Color })];
}
