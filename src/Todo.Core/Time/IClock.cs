namespace Todo.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Now, in the machine's own zone. Its own member rather than a conversion of
    /// <see cref="UtcNow"/>: a caller that derived local time through <c>TimeZoneInfo.Local</c> would
    /// behave differently depending on where it ran, so a test of it would pass or fail by machine -
    /// and a guard that fails for the wrong reason is worse than no guard. <see cref="Today"/> has
    /// been local since it was written; this adds the time of day it cannot carry.
    /// </summary>
    DateTime LocalNow { get; }

    DateOnly Today { get; }
}
