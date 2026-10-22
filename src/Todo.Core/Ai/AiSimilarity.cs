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
    /// Every concept match in the measurement cleared it (0.43-0.62) and no noise did (0.27-0.36).
    /// </summary>
    public const double SearchThreshold = 0.40;

    /// <summary>
    /// Every pair that was the same task cleared it (0.63-0.74, Danish against English included).
    /// Two that were not did too - test against production server at 0.86 - which is why the screen
    /// says "Ligner" and never blocks anything.
    /// </summary>
    public const double DuplicateThreshold = 0.62;

    /// <summary>How many search hits come back at most: past that, a list stops being an answer.</summary>
    public const int MaxSearchHits = 20;

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

    /// <summary>The ids whose vectors clear the search threshold against the query, best first.</summary>
    public static IReadOnlyList<long> Rank(ReadOnlySpan<float> query, IEnumerable<(long Id, float[] Vector)> candidates)
    {
        var scored = new List<(long Id, double Score)>();

        foreach (var (id, vector) in candidates)
        {
            var score = Cosine(query, vector);

            if (score >= SearchThreshold)
            {
                scored.Add((id, score));
            }
        }

        return scored.OrderByDescending(s => s.Score).Take(MaxSearchHits).Select(s => s.Id).ToList();
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

    /// <summary>
    /// What a task is embedded as. The note is cut, because a long Jira description would drown the
    /// title in it - and the title is what a search is usually for.
    /// </summary>
    public static string TextOf(string title, string? note) =>
        string.IsNullOrWhiteSpace(note) ? title : $"{title}\n{(note.Length <= 600 ? note : note[..600])}";
}
