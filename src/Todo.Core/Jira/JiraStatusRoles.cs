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
    ///
    /// And deliberately no built-in status name, where AdoStateRoles answers Done for the literal
    /// "Closed" whatever the lists say. The asymmetry is a finding rather than an oversight: that name
    /// is unpickable over there, because the query the picker is built on excludes it. GET
    /// /api/jira/statuses answers with every status in the project, so a status meaning finished can
    /// always be chosen here - and a built-in name would be a rule the user could neither see nor undo.
    /// </summary>
    public static JiraStatusRole For(string statusName, JiraSettings settings)
    {
        // Done first, and this branch is as load-bearing as the duty one below. A status can sit in
        // two lists, and a finished issue is not waiting for anybody — reverse these and a resolved
        // issue keeps standing as "waiting for the pool", which both hides the closure offer behind
        // the duty branch and leaves it labelled as work somebody still owes you. No switch guards
        // it, unlike duty: an empty done list already means nothing matches.
        if (settings.DoneStatuses.Contains(statusName, StringComparer.Ordinal))
        {
            return JiraStatusRole.Done;
        }

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
