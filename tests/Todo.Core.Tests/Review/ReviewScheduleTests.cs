using Todo.Core.Review;
using Todo.Core.Time;

namespace Todo.Core.Tests.Review;

/// <summary>
/// A pure function of four values, so it belongs here rather than behind an endpoint. It is also the
/// one place where a wrong answer would be nearly invisible: the marker would simply be there, or
/// simply not be, and neither looks like a bug until a week has gone by.
/// </summary>
public class ReviewScheduleTests
{
    private static readonly TimeOnly ThreePm = new(15, 0);

    /// <summary>
    /// Local and UTC deliberately differ by two hours, and the difference is written rather than
    /// converted: <c>SpecifyKind</c> rather than <c>ToUniversalTime</c>, so the gap is the same on a
    /// machine in Copenhagen and on one in UTC. Derived through TimeZoneInfo.Local it would be zero
    /// in CI, and then the guard below could not fail there - the exact shape of an assertion that
    /// cannot fail. It is also why <see cref="IClock.LocalNow"/> is its own member.
    ///
    /// Two hours, and 15:30 as the reading, so the pair straddles the chosen 15:00: an implementation
    /// reading UtcNow lands on the other side of the boundary rather than merely a little off it.
    /// </summary>
    private sealed class OffsetClock : IClock
    {
        public OffsetClock(DateTime localNow)
        {
            LocalNow = DateTime.SpecifyKind(localNow, DateTimeKind.Local);
            UtcNow = DateTime.SpecifyKind(localNow.AddHours(-2), DateTimeKind.Utc);
            Today = DateOnly.FromDateTime(localNow);
        }

        public DateTime UtcNow { get; }

        public DateTime LocalNow { get; }

        public DateOnly Today { get; }
    }

    /// <summary>
    /// The expected values go through the same local-to-UTC conversion the implementation does, so
    /// the assertion says "this local moment" rather than hardcoding an offset. Both sides move
    /// together on a machine in another zone; none of the fixture dates is near a DST switch.
    /// </summary>
    private static DateTime Utc(int year, int month, int day, int hour, int minute)
        => DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Local)
            .ToUniversalTime();

    [Fact]
    public void Never_reviewed_is_due_at_once()
    {
        // A Wednesday, so the most recent Friday 15:00 is five days back.
        var clock = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0));

        var status = ReviewSchedule.For(DayOfWeek.Friday, ThreePm, lastCompletedAt: null, clock);

        Assert.True(status.Due);
        Assert.Null(status.LastCompletedAt);
    }

    [Fact]
    public void Not_due_before_the_chosen_time_on_the_chosen_day()
    {
        // Friday 14:59: this week's moment has not arrived, so last week's is the boundary - and the
        // review taken an hour after it satisfies the period that is running.
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 14, 59, 0));

        Assert.False(
            ReviewSchedule.For(DayOfWeek.Friday, ThreePm, Utc(2026, 8, 21, 16, 0), clock).Due);
    }

    [Fact]
    public void Due_once_the_chosen_time_has_passed()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 15, 0, 0));

        Assert.True(
            ReviewSchedule.For(DayOfWeek.Friday, ThreePm, Utc(2026, 8, 21, 16, 0), clock).Due);
    }

    /// <summary>
    /// The cadence decision from the design's section 2, and the guess falls the other way: a "seven
    /// days since the last one" rule would let the habit slide a day forward every week until the
    /// weekday the user chose stopped meaning anything. Both halves are needed - the first says a
    /// voluntary review counts, the second says it does not buy a week.
    /// </summary>
    [Fact]
    public void A_review_taken_mid_week_satisfies_this_period_but_not_the_next()
    {
        var tuesday = Utc(2026, 8, 25, 10, 0);

        // Wednesday: the Tuesday review is inside the period that is running, so nothing is owed.
        var midWeek = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0));
        Assert.False(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, tuesday, midWeek).Due);

        // Friday 15:01, three days later: the boundary has moved past Tuesday, so it is owed again.
        var friday = new OffsetClock(new DateTime(2026, 8, 28, 15, 1, 0));
        Assert.True(ReviewSchedule.For(DayOfWeek.Friday, ThreePm, tuesday, friday).Due);
    }

    /// <summary>
    /// The boundary is built from local time, because "Friday 15:00" is a statement about the user's
    /// afternoon while the stored timestamp is UTC. OffsetClock puts UTC at 13:30 against a local
    /// 15:30, so an implementation reading UtcNow decides this week's moment has not arrived and
    /// names the wrong one as next. Due is false either way, which is why the assertion with teeth
    /// here is the one on NextDueAt.
    /// </summary>
    [Fact]
    public void The_boundary_is_local_time_rather_than_UTC()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 28, 15, 30, 0));

        // Reviewed twenty minutes after this week's moment, so the next one is a full week out.
        var status = ReviewSchedule.For(
            DayOfWeek.Friday, ThreePm, Utc(2026, 8, 28, 15, 20), clock);

        Assert.False(status.Due);
        Assert.Equal(Utc(2026, 9, 4, 15, 0), status.NextDueAt);
    }

    [Fact]
    public void Next_due_is_the_coming_occurrence_rather_than_the_past_one()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 26, 9, 0, 0));

        var status = ReviewSchedule.For(DayOfWeek.Friday, ThreePm, lastCompletedAt: null, clock);

        Assert.Equal(Utc(2026, 8, 28, 15, 0), status.NextDueAt);
    }

    /// <summary>
    /// Sunday is 0, and 0 is a real weekday rather than an absent value - the whole reason the
    /// contract has to declare default: 5 on the field.
    /// </summary>
    [Fact]
    public void The_chosen_day_can_be_Sunday()
    {
        var clock = new OffsetClock(new DateTime(2026, 8, 30, 15, 1, 0));

        Assert.True(
            ReviewSchedule.For(DayOfWeek.Sunday, ThreePm, lastCompletedAt: null, clock).Due);
    }
}
