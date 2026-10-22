namespace Todo.AiEval;

/// <summary>An import row against an existing task, and whether a reader would call them the same task.</summary>
public sealed record DuplicateCase(string Row, string Existing, bool Same);
