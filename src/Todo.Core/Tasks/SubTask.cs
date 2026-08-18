namespace Todo.Core.Tasks;

public class SubTask
{
    public long Id { get; set; }

    public long TaskItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}
