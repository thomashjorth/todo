using Todo.Core.Tasks;
using Todo.Core.Time;

// Builders write straight to the database, so they never meet the API's validation: arrange
// state with them, but perform the action a test verifies through the endpoint.
namespace Todo.TestSupport.Builders;

public sealed class TaskItemBuilder
{
    private const int DaysInAWeek = 7;

    private readonly IClock _clock;
    private readonly List<SubTask> _subTasks = [];

    private string _title = "En opgave";
    private string _sourceId = "manual";
    private string? _note;
    private string? _requester;
    private string? _externalKey;
    private DateOnly? _deadline;
    private TodoStatus _status = TodoStatus.Open;

    public TaskItemBuilder()
        : this(new SystemClock())
    {
    }

    public TaskItemBuilder(IClock clock) => _clock = clock;

    public TaskItemBuilder Titled(string title)
    {
        _title = title;
        return this;
    }

    public TaskItemBuilder WithNote(string note)
    {
        _note = note;
        return this;
    }

    public TaskItemBuilder DueOn(DateOnly deadline)
    {
        _deadline = deadline;
        return this;
    }

    public TaskItemBuilder DueToday() => DueOn(_clock.Today);

    public TaskItemBuilder Overdue() => DueOn(_clock.Today.AddDays(-1));

    /// <summary>The last day the app still counts as this week, which is Sunday.</summary>
    public TaskItemBuilder DueThisWeek()
    {
        var today = _clock.Today;

        // Asks DeadlineBuckets rather than restating where the week ends, so the two cannot drift.
        var lastDay = Enumerable.Range(1, DaysInAWeek)
            .Select(today.AddDays)
            .Cast<DateOnly?>()
            .LastOrDefault(date => DeadlineBuckets.For(date, today) == DeadlineBucket.ThisWeek);

        if (lastDay is not { } date)
        {
            throw new InvalidOperationException(
                $"The week ends today ({today:yyyy-MM-dd} is a {today.DayOfWeek}), so no later date "
                + "falls in it. Use DueToday(), or DueOn() with a clock the test controls.");
        }

        return DueOn(date);
    }

    public TaskItemBuilder WithoutDeadline()
    {
        _deadline = null;
        return this;
    }

    public TaskItemBuilder RequestedBy(string requester)
    {
        _requester = requester;
        return this;
    }

    public TaskItemBuilder Done()
    {
        _status = TodoStatus.Done;
        return this;
    }

    public TaskItemBuilder InProgress()
    {
        _status = TodoStatus.InProgress;
        return this;
    }

    public TaskItemBuilder WithSubTask(string title, bool isDone = false)
    {
        _subTasks.Add(new SubTask { Title = title, IsDone = isDone, SortOrder = _subTasks.Count });
        return this;
    }

    /// <summary>Makes the task look imported, so re-importing the same row is a duplicate.</summary>
    public TaskItemBuilder FromRetro(string externalKey)
    {
        _sourceId = "retro";
        _externalKey = externalKey;
        return this;
    }

    public TaskItem Build() => new()
    {
        SourceId = _sourceId,
        Title = _title,
        Note = _note,
        Deadline = _deadline,
        Requester = _requester,
        ExternalKey = _externalKey,
        Status = _status,
        CompletedAt = _status == TodoStatus.Done ? _clock.UtcNow : null,
        CreatedAt = _clock.UtcNow,
        SubTasks = [.. _subTasks],
    };
}
