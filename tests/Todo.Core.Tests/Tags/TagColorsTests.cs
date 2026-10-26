using Todo.Core.Tags;

namespace Todo.Core.Tests.Tags;

public class TagColorsTests
{
    [Fact]
    public void The_first_tag_is_red()
    {
        Assert.Equal(0, TagColors.Next([]));
    }

    /// <summary>The user's "frigjort": the slot of a deleted tag is the first free one again.</summary>
    [Fact]
    public void A_freed_slot_is_taken_before_the_next_one()
    {
        Assert.Equal(3, TagColors.Next([0, 1, 2, 4, 5]));
    }

    [Fact]
    public void The_shades_follow_the_seven_colours_of_the_rainbow()
    {
        Assert.Equal(7, TagColors.Next([0, 1, 2, 3, 4, 5, 6]));
    }

    /// <summary>
    /// Past 21 the least used colour, the earliest on a tie. Slot 0 is used twice and is given first,
    /// so a rule that took the first slot regardless of its count would answer 0.
    /// </summary>
    [Fact]
    public void Past_every_slot_the_least_used_colour_is_repeated()
    {
        int[] everySlotTwiceButThree = [.. Enumerable.Range(0, TagColors.Count).SelectMany(s => s == 3 ? new[] { s } : new[] { s, s })];

        Assert.Equal(3, TagColors.Next(everySlotTwiceButThree));
    }
}
