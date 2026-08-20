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
}
