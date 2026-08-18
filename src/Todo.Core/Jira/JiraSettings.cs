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
    /// <summary>
    /// The base URL as something that can actually be called, or null. One spelling shared with
    /// <see cref="IsConfigured"/>, so a source that has checked the flag cannot then find the URL
    /// unusable — the two would otherwise drift apart in different assemblies.
    /// </summary>
    public Uri? BaseUri =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    /// <summary>
    /// Whether there is enough here to reach out. Stricter than non-blank on purpose: slice 11 is
    /// the first caller that acts on the answer, and a name that promises the app can talk to this
    /// Jira must not be true of a string that cannot be called. Measured — <c>https:/jira</c> with
    /// one slash passes a blank check and fails <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>,
    /// so before this it read as configured and would have become a 500 on the first request. The
    /// scheme is checked too, because <c>javascript:alert(1)</c> and <c>file:///c:/temp</c> are both
    /// absolute URIs.
    /// </summary>
    public bool IsConfigured => BaseUri is not null && !string.IsNullOrWhiteSpace(Token);

    /// <summary>
    /// Where the source system shows an item, computed rather than stored. The trailing slash is
    /// trimmed here as well as on the way in through PUT /api/settings, and that is two layers on
    /// purpose: the endpoint owns what gets <em>stored</em>, so the settings page echoes one
    /// canonical form, while this owns what gets <em>emitted</em> and is the only one whose absence
    /// the user would see, as <c>//browse/SAAS-1</c>. This is a public method on a public record and
    /// cannot see whether its caller came through that endpoint.
    /// </summary>
    public string? BrowseUrl(string externalKey) =>
        string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(externalKey)
            ? null
            : $"{BaseUrl!.TrimEnd('/')}/browse/{externalKey}";
}
