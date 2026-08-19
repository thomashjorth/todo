namespace Todo.Core.Jira;

public static class JiraStatusRoles
{
    /// <summary>
    /// Branch order is load-bearing, exactly as in DeadlineBuckets.For. Duty wins: the same status
    /// means "waiting for the pool" when you are not it, and "waiting for you" when you are — so the
    /// switch decides, not the status. Reversing these two hides the work you hold the duty for.
    ///
    /// Ordinal, because both sides of the comparison come from the same instance in the same
    /// spelling: the lists were picked from GET /api/jira/statuses, and the names come back on the
    /// issues. A case-insensitive match would fold two statuses Jira keeps apart into one.
    ///
    /// Deliberately no trimming or blank filtering here. Those live in JqlFor, where they protect
    /// the query; the names compared here are the ones Jira itself sent back, so a second trim would
    /// be dead code and a question nobody could answer.
    /// </summary>
    public static JiraStatusRole For(string statusName, JiraSettings settings)
    {
        if (settings.OnDuty
            && settings.DutyStatuses.Contains(statusName, StringComparer.Ordinal))
        {
            return JiraStatusRole.Duty;
        }

        return settings.WaitingStatuses.Contains(statusName, StringComparer.Ordinal)
            ? JiraStatusRole.Waiting
            : JiraStatusRole.Actionable;
    }
}
