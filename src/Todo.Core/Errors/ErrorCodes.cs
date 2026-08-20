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

    /// <summary>
    /// A work item type name carrying a quotation mark or a backslash. Those two characters are what
    /// WIQL's string literals turn on, and a type name goes into one - the same blocklist, and the
    /// same reason for refusing rather than escaping, as <see cref="JiraStatusNameInvalid"/>.
    /// </summary>
    public const string AdoWorkItemTypeInvalid = "ado.workItemTypeInvalid";

    public const string SystemUnsupportedScheme = "system.unsupportedScheme";
}
