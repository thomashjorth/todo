namespace Todo.Helper;

public sealed record ConnectionsStatus(
    string? ServerUrl,
    bool HasKey,
    string Tunnel,
    string? Refusal,
    SourceStatus Jira,
    SourceStatus Ado,
    string OllamaUrl);
