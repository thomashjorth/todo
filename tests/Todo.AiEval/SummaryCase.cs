namespace Todo.AiEval;

/// <summary>
/// A note and what its summary must and must not say. The omissions are what the prompt asks to
/// leave out - code, greetings, signatures, reproduction steps, history and markup - spelled as the
/// words that would give each one away.
/// </summary>
public sealed record SummaryCase(
    string Title,
    string Note,
    string Source,
    Language Language,
    string[] Mentions,
    string[] Omits);
