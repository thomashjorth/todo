using Todo.Core.Tasks;

namespace Todo.Core.Tests.Tasks;

public class DeadlineBucketsTests
{
    // 2026-08-12 is a Wednesday, so the week ends Sunday 2026-08-16.
    private static readonly DateOnly Wednesday = new(2026, 8, 12);
    private static readonly DateOnly Tuesday = new(2026, 8, 11);
    private static readonly DateOnly Thursday = new(2026, 8, 13);

    [Fact]
    public void No_deadline_is_its_own_bucket()
        => Assert.Equal(DeadlineBucket.NoDeadline, DeadlineBuckets.For(null, null, Wednesday));

    [Fact]
    public void Yesterday_is_overdue()
        => Assert.Equal(DeadlineBucket.Overdue, DeadlineBuckets.For(Tuesday, null, Wednesday));

    [Fact]
    public void Today_is_today()
        => Assert.Equal(DeadlineBucket.Today, DeadlineBuckets.For(Wednesday, null, Wednesday));

    [Fact]
    public void Tomorrow_is_this_week()
        => Assert.Equal(DeadlineBucket.ThisWeek, DeadlineBuckets.For(Thursday, null, Wednesday));

    [Fact]
    public void The_coming_sunday_is_still_this_week()
        => Assert.Equal(
            DeadlineBucket.ThisWeek, DeadlineBuckets.For(new(2026, 8, 16), null, Wednesday));

    [Fact]
    public void The_following_monday_is_later()
        => Assert.Equal(
            DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), null, Wednesday));

    [Fact]
    public void On_a_sunday_tomorrow_belongs_to_the_next_week()
    {
        var sunday = new DateOnly(2026, 8, 16);
        Assert.Equal(DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), null, sunday));
    }

    [Fact]
    public void A_start_date_tomorrow_is_deferred()
        => Assert.Equal(DeadlineBucket.Deferred, DeadlineBuckets.For(null, Thursday, Wednesday));

    /// <summary>The boundary: the day a task starts, it has started. Not `>=`.</summary>
    [Fact]
    public void A_start_date_today_has_started_and_is_not_deferred()
        => Assert.Equal(
            DeadlineBucket.NoDeadline, DeadlineBuckets.For(null, Wednesday, Wednesday));

    [Fact]
    public void A_start_date_yesterday_has_started_and_is_not_deferred()
        => Assert.Equal(DeadlineBucket.NoDeadline, DeadlineBuckets.For(null, Tuesday, Wednesday));

    /// <summary>
    /// The precedence rule: hiding a commitment already missed is worse than showing something
    /// earlier than planned, so an overdue deadline outranks a start date in the future.
    /// </summary>
    [Fact]
    public void An_overdue_deadline_beats_a_start_date_in_the_future()
        => Assert.Equal(DeadlineBucket.Overdue, DeadlineBuckets.For(Tuesday, Thursday, Wednesday));

    [Fact]
    public void A_deadline_that_has_not_passed_yet_loses_to_a_start_date_in_the_future()
        => Assert.Equal(DeadlineBucket.Deferred, DeadlineBuckets.For(Thursday, Thursday, Wednesday));
}
