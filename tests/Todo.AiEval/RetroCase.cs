namespace Todo.AiEval;

/// <summary>
/// A retro board's notes, asked in one call as the app asks them, and what each action should hold.
/// </summary>
public sealed record RetroCase(string Name, RetroNote[] Notes);
