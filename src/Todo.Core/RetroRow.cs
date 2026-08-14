namespace Todo.Core;

public sealed record RetroRow(
    string Title,
    string? Owner,
    string? Author,
    string Zone,
    DateOnly? DueDate,
    DateTime? Created,
    string DedupKey);
