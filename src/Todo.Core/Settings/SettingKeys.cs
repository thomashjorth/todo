namespace Todo.Core.Settings;

/// <summary>
/// The stored keys, so a typo is a compiler error rather than a setting that never saves.
/// </summary>
public static class SettingKeys
{
    public const string Language = "language";

    public const string Delegates = "delegates";

    public const string JiraBaseUrl = "jira.baseUrl";
    public const string JiraProjectKey = "jira.projectKey";
    public const string JiraToken = "jira.token";
    public const string JiraWaitingStatuses = "jira.waitingStatuses";
    public const string JiraIncludeWaiting = "jira.includeWaiting";
    public const string JiraDutyStatuses = "jira.dutyStatuses";
    public const string JiraOnDuty = "jira.onDuty";

    // AdoSettingsReader selects on the "ado." prefix, so a key that does not start with it is read
    // as absent rather than as a compiler error.
    public const string AdoBaseUrl = "ado.baseUrl";
    public const string AdoProject = "ado.project";
    public const string AdoToken = "ado.token";
    public const string AdoWaitingStates = "ado.waitingStates";
    public const string AdoIncludeWaiting = "ado.includeWaiting";
    public const string AdoWorkItemTypes = "ado.workItemTypes";
    public const string AdoDefaultDeadlineDays = "ado.defaultDeadlineDays";
}
