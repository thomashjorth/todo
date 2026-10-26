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
    public const string JiraWaitingStatuses = "jira.waitingStatuses";
    public const string JiraIncludeWaiting = "jira.includeWaiting";
    public const string JiraDutyStatuses = "jira.dutyStatuses";

    /// <summary>
    /// The statuses that mean the issue is finished. Its own list rather than a shared one with
    /// <see cref="AdoDoneStates"/>: the two systems have different vocabularies, and one list holding
    /// both would be unreadable. An empty list is valid and means no suggestions.
    /// </summary>
    public const string JiraDoneStatuses = "jira.doneStatuses";
    public const string JiraOnDuty = "jira.onDuty";

    // AdoSettingsReader selects on the "ado." prefix, so a key that does not start with it is read
    // as absent rather than as a compiler error.
    public const string AdoBaseUrl = "ado.baseUrl";
    public const string AdoProject = "ado.project";
    public const string AdoWaitingStates = "ado.waitingStates";

    /// <summary>The states that mean the work item is finished. See <see cref="JiraDoneStatuses"/>.</summary>
    public const string AdoDoneStates = "ado.doneStates";
    public const string AdoIncludeWaiting = "ado.includeWaiting";
    public const string AdoWorkItemTypes = "ado.workItemTypes";
    public const string AdoDefaultDeadlineDays = "ado.defaultDeadlineDays";

    // ReviewSettingsReader selects on the "review." prefix, so a key that does not start with it is
    // read as absent rather than as a compiler error.
    public const string ReviewWeekday = "review.weekday";
    public const string ReviewTimeOfDay = "review.timeOfDay";

    /// <summary>
    /// When the weekly review was last finished, UTC. Not a user choice like its two neighbours, and
    /// therefore deliberately not written by <c>PUT /api/settings</c>: that route is a full
    /// replacement which reads an absent field as clear, so a field there would be wiped by any
    /// unrelated save. It is written by <c>POST /api/review/complete</c> and nowhere else.
    ///
    /// It lives in this table rather than in the design's SyncState because SyncState does not exist
    /// yet (slice 14), and Setting already carries the tokens, which are not choices either. A
    /// hand-written migration for one key/value row was not worth the price.
    /// </summary>
    public const string ReviewLastCompletedAt = "review.lastCompletedAt";
}
