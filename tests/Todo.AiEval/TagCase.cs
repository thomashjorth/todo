namespace Todo.AiEval;

/// <summary>
/// A task against <see cref="Cases.TagNames"/>: the tags a good answer must hold, and those it must
/// not. A tag in neither list is the model's call. A <see cref="KnownGap"/> is reported without
/// failing, like a search gap: measured wrong, written down, and visible the day a model gets it right.
/// </summary>
public sealed record TagCase(string Title, string[] Required, string[] Forbidden, bool KnownGap = false);
