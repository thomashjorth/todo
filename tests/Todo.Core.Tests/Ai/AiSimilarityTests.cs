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
    /// The rule measured on the user's own tasks (the remaining-features design, section 7): the best
    /// three, the best at least 0.30, each within 0.08 of the best. The candidates are given worst
    /// first, so a ranking that kept the input order would fail.
    /// </summary>
    [Fact]
    public void Search_returns_the_best_three_best_first()
    {
        var ranked = AiSimilarity.Rank(
            [1, 0], [(1, Toward(0.44)), (2, Toward(0.45)), (3, Toward(0.43)), (4, Toward(0.5))]);

        Assert.Equal([4L, 2L, 1L], ranked);
    }

    /// <summary>"Renew seq license" was the right answer at 0.37, which the old 0.40 threw away.</summary>
    [Fact]
    public void A_best_answer_above_the_floor_is_shown_even_when_it_is_weak()
    {
        Assert.Equal([1L], AiSimilarity.Rank([1, 0], [(1, Toward(0.31)), (2, Toward(0.1))]));
    }

    [Fact]
    public void Nothing_at_all_is_shown_when_the_best_is_below_the_floor()
    {
        Assert.Empty(AiSimilarity.Rank([1, 0], [(1, Toward(0.29)), (2, Toward(0.28))]));
    }

    /// <summary>0.43 is within 0.08 of 0.50 and 0.41 is not: a runner-up far behind is noise.</summary>
    [Fact]
    public void A_runner_up_far_behind_the_best_is_left_out()
    {
        Assert.Equal([1L, 2L], AiSimilarity.Rank([1, 0], [(1, Toward(0.5)), (2, Toward(0.43)), (3, Toward(0.41))]));
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

    /// <summary>A unit vector whose cosine with [1, 0] is exactly <paramref name="cosine"/>.</summary>
    private static float[] Toward(double cosine) => [(float)cosine, (float)Math.Sqrt(1 - cosine * cosine)];
}
