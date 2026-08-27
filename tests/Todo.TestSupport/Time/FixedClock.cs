using Todo.Core.Time;

namespace Todo.TestSupport.Time;

/// <summary>
/// The time of day is optional so the callers that only care about the date keep compiling. Midnight
/// is the old behaviour, and it is a meaningful reading rather than a neutral one for anything asking
/// about <see cref="LocalNow"/>: before 15:00 on the chosen weekday, the review's boundary is still
/// last week's.
/// </summary>
public sealed class FixedClock(DateOnly today, TimeOnly? at = null) : IClock
{
    public DateTime UtcNow { get; } = today.ToDateTime(at ?? TimeOnly.MinValue, DateTimeKind.Utc);

    public DateTime LocalNow { get; } = today.ToDateTime(at ?? TimeOnly.MinValue, DateTimeKind.Local);

    public DateOnly Today { get; } = today;
}
