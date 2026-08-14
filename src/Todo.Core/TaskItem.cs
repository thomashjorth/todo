namespace Todo.Core;

public enum TodoStatus
{
    Open,
    InProgress,
    Done,
}

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceId { get; set; } = "manual";

    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateOnly? Deadline { get; set; }

    public string? Requester { get; set; }

    public string? ExternalKey { get; set; }

    public TodoStatus Status { get; set; } = TodoStatus.Open;

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<SubTask> SubTasks { get; set; } = [];
}
