namespace Todo.Core.Sources;

public sealed record ExternalTask(
    string Key,
    string Title,
    string? Note,
    DateOnly? Deadline,
    string? Requester,
    string StatusName);
