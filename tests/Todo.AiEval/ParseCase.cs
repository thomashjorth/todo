namespace Todo.AiEval;

/// <summary>
/// One typed line and what a good suggestion holds. <see cref="Cases.Any"/> in a field means the
/// line is ambiguous there and either answer is fine; null means the field must be empty.
/// <see cref="SubTasks"/> are words each step must hold, in order; none means the line lists no steps
/// and any step the model gives was invented.
/// </summary>
public sealed record ParseCase(
    string Line,
    string? Requester,
    string? Deadline,
    string[] TitleContains,
    string[] TitleOmits,
    string[]? SubTasks = null);
