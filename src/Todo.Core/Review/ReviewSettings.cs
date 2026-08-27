namespace Todo.Core.Review;

/// <summary>
/// The three stored values the review needs, already defaulted. Its own record rather than fields on
/// a shared settings type, following AdoSettings: the reader is the one place an absent row turns
/// into a value, and a caller that got the raw rows would have to know that rule too.
/// </summary>
/// <param name="Weekday">The day of the week the review is suggested on.</param>
/// <param name="TimeOfDay">The local time of day it is suggested at.</param>
/// <param name="LastCompletedAt">
/// When the review was last finished, UTC, or null if it never has been.
/// </param>
public sealed record ReviewSettings(
    DayOfWeek Weekday, TimeOnly TimeOfDay, DateTime? LastCompletedAt);
