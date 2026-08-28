using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.Core.Time;
using Todo.TestSupport.Time;

namespace Todo.Api.Tests;

public class ReviewEndpointsTests : ApiTest
{
    /// <summary>
    /// A Wednesday at nine, so the most recent Friday 15:00 is five days back and the clock is well
    /// clear of the boundary in both directions. Fixed rather than real, so the assertion on the
    /// stamp below can be exact.
    /// </summary>
    private static readonly DateOnly Wednesday = new(2026, 8, 26);

    private static readonly TimeOnly Nine = new(9, 0);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(new FixedClock(Wednesday, Nine));

    [Fact]
    public async Task A_fresh_database_owes_a_review()
    {
        var status = await GetAsync();

        Assert.True(status.Due);
        Assert.Null(status.LastCompletedAt);
    }

    [Fact]
    public async Task Completing_the_review_stamps_the_clock_and_clears_the_debt()
    {
        var after = await CompleteAsync();

        Assert.False(after.Due);
        Assert.Equal(Wednesday.ToDateTime(Nine, DateTimeKind.Utc), after.LastCompletedAt);

        // Answered by the completion itself, so the client needs no second call - and read back, so
        // the assertion is not only about what one handler returned.
        var read = await GetAsync();
        Assert.False(read.Due);
        Assert.Equal(after.LastCompletedAt, read.LastCompletedAt);
    }

    /// <summary>
    /// The guard on the endpoint separation, and it is the whole reason the timestamp is not a field
    /// on SettingsRequest. PUT /api/settings is a full replacement that reads an absent field as
    /// clear, so a field there would be wiped by a save that had nothing to do with the review -
    /// exactly the trap each token has a route of its own to avoid.
    /// </summary>
    [Fact]
    public async Task Completing_the_review_survives_a_later_settings_save()
    {
        var completed = await CompleteAsync();

        var save = await Client.PutAsJsonAsync("/api/settings", new { language = "en" });
        save.EnsureSuccessStatusCode();

        var after = await GetAsync();

        Assert.False(after.Due);
        Assert.Equal(completed.LastCompletedAt, after.LastCompletedAt);
    }

    /// <summary>
    /// That the weekday setting is actually read by the review route - the wiring claim, which is
    /// what belongs at this layer; the arithmetic itself is covered seven ways in
    /// ReviewScheduleTests. Moving the chosen day moves the boundary, so the same stored completion
    /// pays for one week and not for another.
    ///
    /// The completion is seeded rather than posted, and that is not a shortcut: POST stamps the
    /// clock, which is <em>now</em>, and now is later than every boundary the schedule can produce -
    /// so after a real completion no weekday could ever read as owed, and the assertion could not
    /// fail. Measured before it was written that way.
    /// </summary>
    [Fact]
    public async Task Moving_the_weekday_moves_the_boundary()
    {
        // Tuesday morning, between Tuesday 15:00 (not yet reached that week) and Friday 15:00.
        await SeedCompletionAsync(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));

        // Friday: the boundary is the 21st at 15:00, which the Tuesday review is after. Paid.
        await SaveWeekdayAsync(5);
        Assert.False((await GetAsync()).Due);

        // Tuesday: the boundary is the 25th at 15:00, five hours after the review. Owed.
        await SaveWeekdayAsync(2);
        Assert.True((await GetAsync()).Due);
    }

    /// <summary>
    /// The drift test compares only paths and methods, so a field serialised under the wrong name is
    /// invisible to it.
    /// </summary>
    [Fact]
    public async Task Wire_format_uses_the_names_the_contract_declares()
    {
        var body = await (await Client.GetAsync("/api/review")).Content.ReadAsStringAsync();

        Assert.Contains("\"due\":true", body);
        Assert.Contains("\"lastCompletedAt\":null", body);
        Assert.Contains("\"nextDueAt\":", body);
    }

    private async Task<ReviewStatusResponse> GetAsync()
    {
        var status = await Client.GetFromJsonAsync<ReviewStatusResponse>("/api/review");

        Assert.NotNull(status);
        return status;
    }

    private async Task<ReviewStatusResponse> CompleteAsync()
    {
        var response = await Client.PostAsync("/api/review/complete", null);

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ReviewStatusResponse>();
        Assert.NotNull(status);
        return status;
    }

    private async Task SaveWeekdayAsync(int weekday)
    {
        var response = await Client.PutAsJsonAsync("/api/settings", new { reviewWeekday = weekday });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Written straight into the table, because no route can put a completion in the past - see the
    /// caller for why that matters. Round-trip format, the one ReviewSettingsReader parses.
    /// </summary>
    private async Task SeedCompletionAsync(DateTime at)
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        db.Settings.Add(new Setting
        {
            Key = SettingKeys.ReviewLastCompletedAt,
            Value = at.ToString("O", CultureInfo.InvariantCulture),
        });

        await db.SaveChangesAsync();
    }
}
