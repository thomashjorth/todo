namespace Todo.Core.Tags;

/// <summary>
/// A label the user defines and puts on tasks. Its own table rather than a JSON setting like the
/// delegates, because a task refers to it - see the tags design's section 1.
/// </summary>
public class Tag
{
    public const int NameMaxLength = 50;

    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
