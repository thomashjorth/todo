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
}
