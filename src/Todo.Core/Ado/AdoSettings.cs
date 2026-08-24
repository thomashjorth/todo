namespace Todo.Core.Ado;

/// <summary>
/// What the app needs to talk to one Azure DevOps collection. The token is in here because the caller
/// is server-side only; it must never be put on a contract type. See the design document's section 3
/// on why it is stored in cleartext.
///
/// Its own record rather than a generalisation of JiraSettings, and that is the slice's actual
/// question: whether ITaskSource's abstraction was Jira-shaped. The answer has to come out of building
/// the second one, not out of assuming a shared shape beforehand. Where they do converge, a common
/// abstraction is a tidy-up afterwards with two examples to justify it - and the two already differ:
/// Jira has two status lists and two switches, ADO has one list, one switch, a type filter and a
/// deadline the app invents because the server has none.
/// </summary>
public sealed record AdoSettings(
    string? BaseUrl,
    string? Project,
    string? Token,
    IReadOnlyList<string> WaitingStates,
    bool IncludeWaiting,
    // The states that mean the work item is finished. No switch beside it, unlike the waiting pair,
    // and that is the difference between the two lists rather than an omission: waiting-ness decides
    // whether a row is imported at all, so it needs an opt-in, while doneness only ever offers - an
    // already-imported row gets a suggestion, a new one is kept out. An empty list is valid and means
    // no suggestions, which is the opposite of WorkItemTypes above; the near trap is copying the
    // wrong one of the two precedents.
    IReadOnlyList<string> DoneStates,
    // Never empty. The reader turns both an absent row and an unreadable one into AdoDefaults, because
    // an empty type filter would either import test artefacts or import nothing, and neither is what
    // emptying a list means.
    IReadOnlyList<string> WorkItemTypes,
    // Not nullable, and 0 is a value rather than an absence: it means "no deadline". The default of
    // AdoDefaults.DeadlineDays comes from the read layer - the absence of a row - and not from
    // deserialisation, which cannot tell an absent int from a deliberate 0.
    int DefaultDeadlineDays)
{
    /// <summary>
    /// The collection URL as something that can actually be called, or null. One spelling shared with
    /// <see cref="IsConfigured"/>, so a source that has checked the flag cannot then find the URL
    /// unusable - the same reason JiraSettings has it.
    /// </summary>
    public Uri? BaseUri =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    /// <summary>
    /// Whether there is enough here to reach out. Stricter than non-blank for the reason JiraSettings
    /// measured: <c>https:/ado</c> with one slash passes a blank check and fails
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>, so it would read as configured and become
    /// a 500 on the first request. The scheme is checked too, because <c>file:///c:/temp</c> is an
    /// absolute URI as well.
    ///
    /// The project is deliberately <em>not</em> part of this, even though ADO puts it in the path:
    /// slice 11 measured that a missing project must be its own refusal with its own error code, so the
    /// user is told which field is missing rather than being told the whole thing is unconfigured. That
    /// check belongs to the task that makes the call.
    /// </summary>
    public bool IsConfigured => BaseUri is not null && !string.IsNullOrWhiteSpace(Token);

    /// <summary>
    /// Where Azure DevOps shows one work item, computed rather than stored. Built by the app because
    /// the URLs the server hands back address the project by GUID and are not humanly navigable -
    /// measured 2026-08-20, see the design document's section 10.
    ///
    /// What the shape rests on, said out loud because half of it is measured and half is not. The
    /// <c>{collection}/{project}/</c> prefix is measured: the user's own project URL is
    /// <c>.../Edora%20Software/Saas/_queries</c>, so a project-scoped page hangs off exactly that.
    /// The <c>_workitems/edit/{id}</c> tail is Azure DevOps' documented work item route and was
    /// <em>not</em> measured against the instance - clicking the link once settles it, and a wrong
    /// tail shows up as a page that does not open rather than as a wrong task.
    ///
    /// The two halves are escaped differently on purpose, and getting that backwards is the trap. The
    /// base URL is a <em>URL</em> the user pasted, so it already carries <c>%20</c> for the space in
    /// the collection name and must be left alone; the project is a <em>name</em> the user typed, so
    /// it has to be escaped here. Escaping the base URL would give <c>%2520</c>, and not escaping the
    /// project would break on any project name with a space in it.
    ///
    /// The trailing slash is trimmed here as well as on the way in through PUT /api/settings, and
    /// that is two layers on purpose - the same split JiraSettings.BrowseUrl documents: the endpoint
    /// owns what gets stored, this owns what gets emitted, and this is the only one whose absence a
    /// user would see.
    /// </summary>
    public string? BrowseUrl(string externalKey) =>
        string.IsNullOrWhiteSpace(BaseUrl)
        || string.IsNullOrWhiteSpace(Project)
        || string.IsNullOrWhiteSpace(externalKey)
            ? null
            : $"{BaseUrl!.TrimEnd('/')}/{Uri.EscapeDataString(Project!.Trim())}"
                + $"/_workitems/edit/{Uri.EscapeDataString(externalKey.Trim())}";
}
