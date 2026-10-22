namespace Todo.Core.Ai;

/// <summary>
/// What the weekly review overview is written from, gathered by the host so the model only phrases
/// it. <see cref="StaleSomeday"/> is read off the creation date, the only one a Someday task has.
/// </summary>
/// <remarks>
/// Nothing about what was completed, on purpose: the user's verdict on the first overview
/// (2026-10-22) was that the recap of done tasks "gengiver forkert og det giver ingen værdi" - the
/// model ran four titles together into one wrong sentence, in the first person - while the points
/// about what to act on were the useful ones. So the facts are only what can be acted on.
/// </remarks>
public sealed record ReviewFacts(
    IReadOnlyList<AgeFact> Overdue,
    IReadOnlyList<WaitingFact> Waiting,
    IReadOnlyList<AgeFact> StaleSomeday)
{
    /// <summary>
    /// "Sat for months": three of them. A choice rather than a measurement, made 2026-10-22 for review.
    /// </summary>
    public const int StaleDays = 90;

    /// <summary>
    /// How many of each list reach the prompt. Prompt reading is most of a call's time on the user's
    /// CPU, and the longest-waiting few are enough to say which to chase.
    /// </summary>
    public const int MaxPerList = 8;
}
