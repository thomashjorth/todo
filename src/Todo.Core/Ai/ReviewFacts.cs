namespace Todo.Core.Ai;

/// <summary>
/// What the weekly review overview is written from, gathered by the host so the model only phrases
/// it. <see cref="StaleSomeday"/> is read off the creation date, the only one a Someday task has.
/// </summary>
public sealed record ReviewFacts(
    int CompletedCount,
    IReadOnlyList<string> Completed,
    IReadOnlyList<WaitingFact> Waiting,
    int OverdueCount,
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
