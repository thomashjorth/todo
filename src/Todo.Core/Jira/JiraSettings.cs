namespace Todo.Core.Jira;

/// <summary>
/// What the app needs to talk to one Jira. The token is in here because the caller is server-side
/// only; it must never be put on a contract type. See the design document's section 3 on why it is
/// stored in cleartext.
/// </summary>
public sealed record JiraSettings(
    string? BaseUrl,
    string? ProjectKey,
    string? Token,
    IReadOnlyList<string> WaitingStatuses,
    bool IncludeWaiting)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token);

    /// <summary>Where the source system shows an item, computed rather than stored.</summary>
    public string? BrowseUrl(string externalKey) =>
        string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(externalKey)
            ? null
            : $"{BaseUrl!.TrimEnd('/')}/browse/{externalKey}";
}
