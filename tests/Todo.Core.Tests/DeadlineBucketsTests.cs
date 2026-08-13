using Todo.Core;

namespace Todo.Core.Tests;

public class DeadlineBucketsTests
{
    // 2026-08-12 is a Wednesday, so the week ends Sunday 2026-08-16.
    private static readonly DateOnly Wednesday = new(2026, 8, 12);

    [Fact]
    public void No_deadline_is_its_own_bucket()
        => Assert.Equal(DeadlineBucket.NoDeadline, DeadlineBuckets.For(null, Wednesday));

    [Fact]
    public void Yesterday_is_overdue()
        => Assert.Equal(DeadlineBucket.Overdue, DeadlineBuckets.For(new(2026, 8, 11), Wednesday));

    [Fact]
    public void Today_is_today()
        => Assert.Equal(DeadlineBucket.Today, DeadlineBuckets.For(Wednesday, Wednesday));

    [Fact]
    public void Tomorrow_is_this_week()
        => Assert.Equal(DeadlineBucket.ThisWeek, DeadlineBuckets.For(new(2026, 8, 13), Wednesday));

    [Fact]
    public void The_coming_sunday_is_still_this_week()
        => Assert.Equal(DeadlineBucket.ThisWeek, DeadlineBuckets.For(new(2026, 8, 16), Wednesday));

    [Fact]
    public void The_following_monday_is_later()
        => Assert.Equal(DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), Wednesday));

    [Fact]
    public void On_a_sunday_tomorrow_belongs_to_the_next_week()
    {
        var sunday = new DateOnly(2026, 8, 16);
        Assert.Equal(DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), sunday));
    }
}
