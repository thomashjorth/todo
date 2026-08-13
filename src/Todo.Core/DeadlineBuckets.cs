namespace Todo.Core;

public enum DeadlineBucket
{
    Overdue,
    Today,
    ThisWeek,
    Later,
    NoDeadline,
}

public static class DeadlineBuckets
{
    public static DeadlineBucket For(DateOnly? deadline, DateOnly today)
    {
        if (deadline is not { } due)
        {
            return DeadlineBucket.NoDeadline;
        }

        if (due < today)
        {
            return DeadlineBucket.Overdue;
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
