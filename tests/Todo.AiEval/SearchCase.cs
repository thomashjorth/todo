namespace Todo.AiEval;

/// <summary>
/// A query against <see cref="Cases.SearchTasks"/>. <see cref="Expected"/> must be the best hit, or
/// null when nothing should clear the threshold. A <see cref="KnownGap"/> is a case the model is
/// measured to get wrong - world knowledge rather than meaning - and it is reported without failing,
/// so the day a model gets it right shows up.
/// </summary>
public sealed record SearchCase(string Query, string? Expected, bool KnownGap = false);
