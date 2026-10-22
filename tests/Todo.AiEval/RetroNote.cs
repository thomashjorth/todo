namespace Todo.AiEval;

/// <summary>
/// One note. <see cref="Owner"/> is what the guess should be - <see cref="Cases.Any"/> where the note
/// is ambiguous, null where nobody should be guessed. <see cref="BoardOwner"/> is the board's own
/// Action Owner, which a guess may never replace.
/// </summary>
public sealed record RetroNote(string Text, string? Owner, string[] TitleContains, string? BoardOwner = null);
