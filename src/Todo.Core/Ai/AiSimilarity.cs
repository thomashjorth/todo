namespace Todo.Core.Ai;

/// <summary>
/// How close two texts are, read from their EmbeddingGemma vectors, and where "close" begins. Both
/// thresholds were measured on the user's machine 2026-10-22 - see section 6 of the remaining-features
/// design for the tables - and both are eval cases, so a different model shows up as a failed case
/// rather than as a list that quietly got worse.
/// </summary>
public static class AiSimilarity
{
    /// <summary>
    /// The best hit has to reach this for anything to be shown. Measured on the user's own 44 tasks
    /// (the remaining-features design, section 7): the right task came first at 0.33-0.61, and two of
    /// three questions about nothing in the list stayed below it.
    /// </summary>
    public const double SearchFloor = 0.30;

    /// <summary>A runner-up is shown only this close to the best; further behind it is noise.</summary>
    public const double SearchMargin = 0.08;

    /// <summary>
    /// Every pair that was the same task cleared it (0.63-0.74, Danish against English included).
    /// Two that were not did too - test against production server at 0.86 - which is why the screen
    /// says "Ligner" and never blocks anything.
    /// </summary>
    public const double DuplicateThreshold = 0.62;

    /// <summary>
    /// Three, because the right task was among the best three in 16 of 17 measured questions and first
    /// in 14: past that, a list stops being an answer.
    /// </summary>
    public const int MaxSearchHits = 3;

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / Math.Sqrt(normA * normB);
    }

    /// <summary>
    /// The best three ids, best first, when the best reaches <see cref="SearchFloor"/>, and each
    /// within <see cref="SearchMargin"/> of it. A relative rule rather than one absolute threshold:
    /// the old 0.40 found the right task first in 5 of 17 measured questions, this one in 14.
    /// </summary>
    public static IReadOnlyList<long> Rank(ReadOnlySpan<float> query, IEnumerable<(long Id, float[] Vector)> candidates)
    {
        var scored = new List<(long Id, double Score)>();

        foreach (var (id, vector) in candidates)
        {
            scored.Add((id, Cosine(query, vector)));
        }

        var ranked = scored.OrderByDescending(s => s.Score).ToList();

        if (ranked.Count == 0 || ranked[0].Score < SearchFloor)
        {
            return [];
        }

        var best = ranked[0].Score;

        return [.. ranked.Take(MaxSearchHits).Where(s => s.Score >= best - SearchMargin).Select(s => s.Id)];
    }

    /// <summary>The closest candidate at or above the duplicate threshold, or nothing.</summary>
    public static long? ClosestDuplicate(ReadOnlySpan<float> row, IEnumerable<(long Id, float[] Vector)> candidates)
    {
        long? best = null;
        var bestScore = DuplicateThreshold;

        foreach (var (id, vector) in candidates)
        {
            var score = Cosine(row, vector);

            if (score >= bestScore)
            {
                best = id;
                bestScore = score;
            }
        }

        return best;
    }
}
