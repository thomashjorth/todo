using Todo.Core.Time;

namespace Todo.Core.Tests.Time;

public sealed class FixedClock(DateOnly today) : IClock
{
    public DateTime UtcNow { get; } = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateOnly Today { get; } = today;
}
