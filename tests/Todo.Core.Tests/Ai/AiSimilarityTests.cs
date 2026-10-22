using Todo.Core.Ai;

namespace Todo.Core.Tests.Ai;

public class AiSimilarityTests
{
    [Fact]
    public void The_same_direction_is_one_and_a_right_angle_is_zero()
    {
        Assert.Equal(1, AiSimilarity.Cosine([1, 2, 3], [2, 4, 6]), 6);
        Assert.Equal(0, AiSimilarity.Cosine([1, 0], [0, 1]), 6);
    }

    /// <summary>A vector of zeros or of another length is not similar to anything, rather than NaN.</summary>
    [Fact]
    public void A_vector_that_cannot_be_compared_scores_zero()
    {
        Assert.Equal(0, AiSimilarity.Cosine([0, 0], [1, 1]));
        Assert.Equal(0, AiSimilarity.Cosine([1, 2], [1, 2, 3]));
        Assert.Equal(0, AiSimilarity.Cosine([], []));
    }

    /// <summary>
    /// The candidates are given worst first, so a ranking that kept the input order would fail.
    /// 0.39 and 0.41 straddle the threshold the measurement set.
    /// </summary>
    [Fact]
    public void Search_keeps_what_clears_the_threshold_best_first()
    {
        float[] query = [1, 0];

        var ranked = AiSimilarity.Rank(query, [(1, Toward(0.39)), (2, Toward(0.41)), (3, Toward(0.9))]);

        Assert.Equal([3L, 2L], ranked);
    }

    [Fact]
    public void Search_returns_at_most_twenty()
    {
        var ranked = AiSimilarity.Rank([1, 0], Enumerable.Range(1, 30).Select(i => ((long)i, Toward(0.9))));

        Assert.Equal(AiSimilarity.MaxSearchHits, ranked.Count);
    }

    [Fact]
    public void A_duplicate_is_the_closest_candidate_above_the_threshold()
    {
        var closest = AiSimilarity.ClosestDuplicate([1, 0], [(1, Toward(0.7)), (2, Toward(0.95)), (3, Toward(0.65))]);

        Assert.Equal(2L, closest);
    }

    [Fact]
    public void Nothing_above_the_duplicate_threshold_is_no_duplicate()
    {
        Assert.Null(AiSimilarity.ClosestDuplicate([1, 0], [(1, Toward(0.61)), (2, Toward(0.3))]));
    }

    [Fact]
    public void A_task_is_embedded_as_its_title_and_the_start_of_its_note()
    {
        Assert.Equal("Køb mælk", AiSimilarity.TextOf("Køb mælk", "  "));
        Assert.Equal("Køb mælk\nLetmælk", AiSimilarity.TextOf("Køb mælk", "Letmælk"));
        Assert.Equal("T\n" + new string('x', 600), AiSimilarity.TextOf("T", new string('x', 900)));
    }

    /// <summary>A unit vector whose cosine with [1, 0] is exactly <paramref name="cosine"/>.</summary>
    private static float[] Toward(double cosine) => [(float)cosine, (float)Math.Sqrt(1 - cosine * cosine)];
}
