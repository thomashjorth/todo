namespace Todo.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }
}
