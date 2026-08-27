namespace Todo.Core.Review;

/// <summary>
/// What an absent settings row reads as. One spelling, because the reader and the writer have to
/// agree: ReviewSettingsReader turns the absence of a row into these values, and SettingsEndpoints
/// stores a row only when the request asks for something else. Two spellings would let a saved
/// default become a stored row, and a stored row is what the two language tests in
/// SettingsEndpointsTests assert cannot appear.
/// </summary>
public static class ReviewDefaults
{
    /// <summary>
    /// Friday, which is Allen's own recommendation: the week is still open enough to act on whatever
    /// the review turns up. The user's decision 2026-08-27, and it is a setting they can move.
    ///
    /// Written down twice on purpose, here and as <c>default: 5</c> on SettingsRequest.reviewWeekday
    /// in the contract. The contract's copy is what makes an absent field bind to Friday - and the
    /// trap is sharper than the one on adoDefaultDeadlineDays, because <c>0</c> here is
    /// <em>Sunday</em>, a real weekday rather than a missing value. Without the default, a save that
    /// never mentioned the review would move the habit to Sunday.
    /// </summary>
    public const DayOfWeek Weekday = DayOfWeek.Friday;

    /// <summary>Mid-afternoon, so the reminder does not arrive before the day has happened.</summary>
    public static readonly TimeOnly TimeOfDay = new(15, 0);
}
