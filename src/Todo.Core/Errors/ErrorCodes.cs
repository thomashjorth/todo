namespace Todo.Core.Errors;

/// <summary>
/// The <c>code</c> the API puts on every 400 so the frontend can translate it.
/// </summary>
public static class ErrorCodes
{
    // A code is an identity, not a description: once shipped it must never be renamed,
    // or a frontend on an older translation file silently loses the message.

    public const string TaskTitleRequired = "task.titleRequired";
    public const string TaskTitleTooLong = "task.titleTooLong";

    public const string SubTaskTitleRequired = "subTask.titleRequired";
    public const string SubTaskTitleTooLong = "subTask.titleTooLong";

    public const string RetroEmptyExport = "retro.emptyExport";
    public const string RetroMissingContentColumn = "retro.missingContentColumn";
    public const string RetroRowKeyRequired = "retro.rowKeyRequired";
    public const string RetroRowTitleRequired = "retro.rowTitleRequired";
    public const string RetroRowTitleTooLong = "retro.rowTitleTooLong";
    public const string RetroDuplicateAlias = "retro.duplicateAlias";

    public const string SettingsUnknownLanguage = "settings.unknownLanguage";
    public const string SettingsEmptyToken = "settings.emptyToken";
    public const string SettingsDuplicateDelegate = "settings.duplicateDelegate";

    public const string JiraNotConfigured = "jira.notConfigured";
    public const string JiraProjectKeyRequired = "jira.projectKeyRequired";
    public const string JiraRefused = "jira.refused";
    public const string JiraUnreachable = "jira.unreachable";
    public const string JiraRowKeyRequired = "jira.rowKeyRequired";
    public const string JiraRowTitleRequired = "jira.rowTitleRequired";
    public const string JiraRowTitleTooLong = "jira.rowTitleTooLong";
    public const string JiraRowStatusRequired = "jira.rowStatusRequired";
    public const string JiraStatusNameInvalid = "jira.statusNameInvalid";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend
    /// translates it with the same function it uses for <c>ApiError.code</c>.
    /// </summary>
    public const string JiraExcludedWaiting = "jira.excludedWaiting";

    /// <summary>
    /// The issue stands in a status the user calls finished and has never been imported, so importing
    /// it would create a task that is already over. Shown rather than hidden, the same choice the
    /// waiting rows make: a hidden row would look like one Jira had lost.
    /// </summary>
    public const string JiraExcludedDone = "jira.excludedDone";

    /// <summary>
    /// The work item type filter is a requirement rather than an optional filter, so an empty list is
    /// refused instead of being read as "every type" - the same rule as
    /// <see cref="JiraProjectKeyRequired"/>, and for the same reason: the absence of a limit is not a
    /// neutral default. The message must read whole without the value, because api-error-message.ts
    /// translates without params.
    /// </summary>
    public const string AdoWorkItemTypesRequired = "ado.workItemTypesRequired";

    /// <summary>
    /// A number of days below 0 or above <see cref="Ado.AdoDefaults.DeadlineDaysMax"/>. Negative would
    /// mean "overdue the moment it is imported", and 300 is a plausible typo for 3.
    /// </summary>
    public const string AdoDefaultDeadlineDaysInvalid = "ado.defaultDeadlineDaysInvalid";

    /// <summary>
    /// The collection URL or the token is missing, which is what <c>AdoSettings.IsConfigured</c> asks.
    /// </summary>
    public const string AdoNotConfigured = "ado.notConfigured";

    /// <summary>
    /// The project is blank. Deliberately its own refusal rather than part of
    /// <see cref="AdoNotConfigured"/>, even though Azure DevOps puts the project in the path: slice 11
    /// measured that folding a missing project into "not configured" tells the user the whole thing is
    /// unset when one field is blank. Its counterpart is <see cref="JiraProjectKeyRequired"/>.
    /// </summary>
    public const string AdoProjectRequired = "ado.projectRequired";

    public const string AdoRefused = "ado.refused";
    public const string AdoUnreachable = "ado.unreachable";

    public const string AdoRowKeyRequired = "ado.rowKeyRequired";
    public const string AdoRowTitleRequired = "ado.rowTitleRequired";
    public const string AdoRowTitleTooLong = "ado.rowTitleTooLong";

    /// <summary>
    /// A row without its Azure DevOps state name. The state is what waiting-ness is derived from, and
    /// the counterpart of <see cref="JiraRowStatusRequired"/> for the same measured reason: an absent
    /// string is null and can be refused, while an absent bool would arrive as <c>false</c>, a legal
    /// value nothing could refuse.
    /// </summary>
    public const string AdoRowStateRequired = "ado.rowStateRequired";

    /// <summary>
    /// A row without its work item type. Jira has no counterpart, because Jira's import has no filter
    /// to re-apply: the type is what decision B's filter is applied to on the way in, so a row without
    /// one would be silently skipped as "not a type you asked for" - which looks like a lost row rather
    /// than a refusal.
    /// </summary>
    public const string AdoRowWorkItemTypeRequired = "ado.rowWorkItemTypeRequired";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend translates
    /// it with the same function it uses for <c>ApiError.code</c> - see
    /// <see cref="JiraExcludedWaiting"/>.
    /// </summary>
    public const string AdoExcludedWaiting = "ado.excludedWaiting";

    /// <summary>
    /// The counterpart of <see cref="JiraExcludedDone"/>: a finished work item that was never
    /// imported, kept out rather than brought in as a fresh open task.
    /// </summary>
    public const string AdoExcludedDone = "ado.excludedDone";

    /// <summary>
    /// A work item type name carrying a quotation mark or a backslash. Those two characters are what
    /// WIQL's string literals turn on, and a type name goes into one - the same blocklist, and the
    /// same reason for refusing rather than escaping, as <see cref="JiraStatusNameInvalid"/>.
    /// </summary>
    public const string AdoWorkItemTypeInvalid = "ado.workItemTypeInvalid";

    public const string SystemUnsupportedScheme = "system.unsupportedScheme";
}
