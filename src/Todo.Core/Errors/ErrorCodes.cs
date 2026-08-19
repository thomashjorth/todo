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

    public const string JiraNotConfigured = "jira.notConfigured";
    public const string JiraProjectKeyRequired = "jira.projectKeyRequired";
    public const string JiraRefused = "jira.refused";
    public const string JiraUnreachable = "jira.unreachable";
    public const string JiraRowKeyRequired = "jira.rowKeyRequired";
    public const string JiraRowTitleRequired = "jira.rowTitleRequired";
    public const string JiraRowTitleTooLong = "jira.rowTitleTooLong";
    public const string JiraRowStatusRequired = "jira.rowStatusRequired";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend
    /// translates it with the same function it uses for <c>ApiError.code</c>.
    /// </summary>
    public const string JiraExcludedWaiting = "jira.excludedWaiting";

    public const string SystemUnsupportedScheme = "system.unsupportedScheme";
}
