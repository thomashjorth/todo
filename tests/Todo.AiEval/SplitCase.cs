namespace Todo.AiEval;

/// <summary>
/// A task to split. <see cref="Source"/> is the task's source id, because the source decides the
/// answer's language for an imported task (AiEndpoints.LanguageOf), and <see cref="Language"/> is what
/// a good answer is written in.
/// </summary>
public sealed record SplitCase(string Title, string? Note, string[] Existing, string Source, Language Language);
