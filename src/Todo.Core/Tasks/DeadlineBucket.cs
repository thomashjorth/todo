namespace Todo.Core.Tasks;

public enum DeadlineBucket
{
    Overdue,
    Today,
    ThisWeek,
    Later,
    NoDeadline,

    /// <summary>Has a start date in the future, so it is not actionable yet.</summary>
    Deferred,
}
