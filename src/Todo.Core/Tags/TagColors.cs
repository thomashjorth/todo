namespace Todo.Core.Tags;

/// <summary>
/// Which colour a new tag gets. The colours themselves live in the frontend's palette; here a colour
/// is its slot, 0-20: the rainbow's seven, then each again in two more shades (the tag-chips design).
/// </summary>
public static class TagColors
{
    public const int Count = 21;

    /// <summary>
    /// The first free slot, so a deleted tag's colour is the next one given. Past 21 tags, the least
    /// used, the earliest in the order on a tie.
    /// </summary>
    public static int Next(IEnumerable<int> used)
    {
        var counts = new int[Count];

        foreach (var slot in used)
        {
            if (slot is >= 0 and < Count)
            {
                counts[slot]++;
            }
        }

        return Array.IndexOf(counts, counts.Min());
    }
}
