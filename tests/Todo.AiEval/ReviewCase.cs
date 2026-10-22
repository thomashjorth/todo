using Todo.Core.Ai;

namespace Todo.AiEval;

/// <summary>
/// Review facts and what the points must say. <see cref="Absent"/> are plausible task titles the
/// facts do not hold: a model that writes one has invented a task.
/// </summary>
public sealed record ReviewCase(string Name, ReviewFacts Facts, string[] Mentions, string[] Absent);
