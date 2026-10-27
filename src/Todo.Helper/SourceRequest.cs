namespace Todo.Helper;

public sealed record SourceRequest(string? BaseUrl, string? Token, bool ClearToken = false);
