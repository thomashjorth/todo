namespace Todo.Core.Ai;

/// <summary>The shape <see cref="AiPrompts.ParseTaskSchema"/> asks the model for. The deadline is text: the model writes it, and <see cref="AiSuggestions.Deadline"/> decides whether it is a date.</summary>
/// <remarks>
/// <see cref="SubTasks"/> came from the user's own test 2026-10-22: "Lav en præsentation, først udkast
/// så finpudsning" was suggested whole as a title, steps and all.
/// </remarks>
public sealed record ParsedTaskAnswer(string? Title, string? Requester, string? Deadline, string?[]? SubTasks = null);
