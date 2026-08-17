namespace Todo.Core.Tasks;

public static class DeadlineBuckets
{
    /// <summary>
    /// The bucket a task belongs in today. A start date in the future defers the task, but the
    /// order of these branches is load-bearing: a task can have both a future start date and a
    /// deadline that has already passed — it was deferred, and time ran out on it anyway. The two
    /// statements contradict each other, so the question is which mistake is worse. Hiding a
    /// commitment that has already been missed is worse than showing something earlier than
    /// planned, so <see cref="DeadlineBucket.Overdue"/> wins over
    /// <see cref="DeadlineBucket.Deferred"/>. Do not reorder.
    /// </summary>
    public static DeadlineBucket For(DateOnly? deadline, DateOnly? deferUntil, DateOnly today)
    {
        if (deadline is { } overdue && overdue < today)
        {
            return DeadlineBucket.Overdue;
        }

        // Strictly after today: on the day a task starts, it has started.
        if (deferUntil is { } start && start > today)
        {
            return DeadlineBucket.Deferred;
        }

        if (deadline is not { } due)
        {
            return DeadlineBucket.NoDeadline;
        }

        if (due == today)
        {
            return DeadlineBucket.Today;
        }

        return due <= EndOfWeek(today) ? DeadlineBucket.ThisWeek : DeadlineBucket.Later;
    }

    // Weeks run Monday to Sunday, so on a Sunday the week ends today.
    private static DateOnly EndOfWeek(DateOnly today)
        => today.AddDays(((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7);
}
