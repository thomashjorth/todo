using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Todo.Core.Persistence;
using Todo.Core.Review;
using Todo.Core.Settings;
using Todo.Core.Time;

namespace Todo.Host.Endpoints;

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReview(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/review", async (ReviewSettingsReader reader, IClock clock) =>
            await StatusAsync(reader, clock))
        .WithName("getReviewStatus")
        .WithTags("Review")
        .Produces<ReviewStatusResponse>();

        app.MapPost("/api/review/complete", async (
            TodoDbContext db, ReviewSettingsReader reader, IClock clock) =>
        {
            // A route of its own rather than a field on PUT /api/settings: that one is a full
            // replacement which reads an absent field as clear, so an unrelated save would wipe the
            // stamp. The same reason each token has a route - and here the current-object trick
            // TaskStore.update uses could not help, because this value is never sent by the client.
            var existing = await db.Settings.FindAsync(SettingKeys.ReviewLastCompletedAt);
            var value = clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            if (existing is null)
            {
                db.Settings.Add(new Setting { Key = SettingKeys.ReviewLastCompletedAt, Value = value });
            }
            else
            {
                existing.Value = value;
            }

            await db.SaveChangesAsync();

            // Answered rather than left to a second call, so finishing the review is one round trip.
            return await StatusAsync(reader, clock);
        })
        .WithName("completeReview")
        .WithTags("Review")
        .Produces<ReviewStatusResponse>();

        return app;
    }

    /// <summary>
    /// Built in one place, so both routes answer the same shape - and so there is one line to read
    /// when asking where the due state is decided. The five groups the review screen walks through
    /// are deliberately not here: they are grouped in the client off the bucket each task already
    /// carries, because TaskRow and TaskDetail inject TaskStore directly and a second copy of the
    /// list would go stale the moment anything was saved.
    /// </summary>
    private static async Task<ReviewStatusResponse> StatusAsync(
        ReviewSettingsReader reader, IClock clock)
    {
        var settings = await reader.ReadAsync();
        var status = ReviewSchedule.For(
            settings.Weekday, settings.TimeOfDay, settings.LastCompletedAt, clock);

        return new ReviewStatusResponse
        {
            Due = status.Due,
            LastCompletedAt = status.LastCompletedAt,
            NextDueAt = status.NextDueAt,
        };
    }
}
