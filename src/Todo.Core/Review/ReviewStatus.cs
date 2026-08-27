namespace Todo.Core.Review;

/// <summary>
/// Whether the weekly review is owed, and the two timestamps that say why.
/// </summary>
/// <param name="Due">
/// Whether a review is owed right now. Computed rather than stored - see <see cref="ReviewSchedule"/>.
/// </param>
/// <param name="LastCompletedAt">
/// When the review was last finished, in UTC, or null if it never has been. Null reads as owed rather
/// than as "not configured": a fresh database says so on day one, and one press clears it.
/// </param>
/// <param name="NextDueAt">
/// The next scheduled moment, in UTC. Carried so the client does not have to do the arithmetic, and
/// so a test can assert about the boundary rather than only about which side of it we are on.
/// </param>
public sealed record ReviewStatus(bool Due, DateTime? LastCompletedAt, DateTime NextDueAt);
