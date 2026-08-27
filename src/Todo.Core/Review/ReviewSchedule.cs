using Todo.Core.Time;

namespace Todo.Core.Review;

public static class ReviewSchedule
{
    /// <summary>
    /// Whether the weekly review is owed, computed rather than fired. Nothing runs at midnight and
    /// there is no timer - the same rule as <c>DeadlineBuckets.For</c>, and for the same reason: the
    /// app has no tray and no background sync, so a moment that "fires" on Friday at 15:00 hits
    /// nothing when the window is shut. This survives the app having been closed for three weeks.
    ///
    /// The cadence is a fixed weekday rather than seven days since the last one, and the guess falls
    /// the other way. A review taken on Tuesday satisfies the period that is running, so the marker
    /// goes; when Friday 15:00 arrives the boundary has moved past Tuesday and it is owed again. A
    /// seven-day rule would let the habit slide a day forward every week until the weekday the user
    /// chose stopped meaning anything.
    ///
    /// Local time throughout, converted to UTC only to compare: "Friday 15:00" is a statement about
    /// the user's afternoon, while <paramref name="lastCompletedAt"/> is UTC by the repo's
    /// convention. One frame, not two - and that is why <see cref="IClock.LocalNow"/> exists.
    /// </summary>
    public static ReviewStatus For(
        DayOfWeek weekday, TimeOnly timeOfDay, DateTime? lastCompletedAt, IClock clock)
    {
        var now = clock.LocalNow;
        var todayAt = now.Date + timeOfDay.ToTimeSpan();

        // How many days back the chosen weekday is. On the day itself it is today when the time has
        // passed and a full week back when it has not, so the boundary is never in the future - which
        // is what lets a null lastCompletedAt read as owed without a branch of its own.
        var daysBack = ((int)now.DayOfWeek - (int)weekday + 7) % 7;
        var due = daysBack == 0 && now < todayAt
            ? todayAt.AddDays(-7)
            : todayAt.AddDays(-daysBack);

        return new ReviewStatus(
            Due: lastCompletedAt is null || lastCompletedAt < AsUtc(due),
            LastCompletedAt: lastCompletedAt,
            NextDueAt: AsUtc(due.AddDays(7)));
    }

    /// <summary>
    /// The arithmetic above runs on <see cref="DateTimeKind.Unspecified"/> values, because adding a
    /// TimeSpan to a Date loses the kind. Stamping it Local before converting is what makes the
    /// conversion mean "this moment in the user's own zone".
    /// </summary>
    private static DateTime AsUtc(DateTime local)
        => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
}
