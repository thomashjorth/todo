namespace Todo.Core.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => DateTime.Now;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
