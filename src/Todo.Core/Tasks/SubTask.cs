namespace Todo.Core.Tasks;

public class SubTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}
