namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.SplitSchema"/> asks the model for.</summary>
public sealed record SplitAnswer(string?[]? SubTasks);
