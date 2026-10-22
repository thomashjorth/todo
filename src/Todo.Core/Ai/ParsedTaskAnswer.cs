namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.ParseTaskSchema"/> asks the model for. The deadline is text: the model writes it, and <see cref="AiSuggestions.Deadline"/> decides whether it is a date.</summary>
public sealed record ParsedTaskAnswer(string? Title, string? Requester, string? Deadline);
