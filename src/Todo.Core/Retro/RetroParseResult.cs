namespace Todo.Core.Retro;

public sealed record RetroParseResult(IReadOnlyList<RetroRow> Rows, int SkippedRatingCards);
