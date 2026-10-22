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

            if (name.Length == 0)
            {
                return ApiErrors.BadRequest(ErrorCodes.TagNameRequired, "A tag needs a name.");
            }

            if (name.Length > Tag.NameMaxLength)
            {
                return ApiErrors.BadRequest(
                    ErrorCodes.TagNameTooLong, $"A tag name may be at most {Tag.NameMaxLength} characters.");
            }

            // Asked before the insert, so the answer is a code the user can read rather than the
            // unique index's exception. The index is NOCASE, and so is this comparison.
            var lower = name.ToLowerInvariant();
            if (await db.Tags.AnyAsync(t => t.Name.ToLower() == lower))
            {
                return ApiErrors.BadRequest(ErrorCodes.TagDuplicate, "A tag of that name exists already.");
            }

            var tag = new Tag { Name = name };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();

            return TypedResults.Created($"/api/tags/{tag.Id}", new TagRef { Id = tag.Id, Name = tag.Name });
        })
        .WithName("createTag")
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

    internal static async Task<List<TagRef>> AllAsync(TodoDbContext db) =>
        [.. (await db.Tags.OrderBy(t => t.Name).ToListAsync()).Select(t => new TagRef { Id = t.Id, Name = t.Name })];
}
