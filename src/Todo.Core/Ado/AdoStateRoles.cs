namespace Todo.Core.Ado;

/// <summary>
/// Whether one Azure DevOps state means the work item is waiting on somebody else. The counterpart of
/// JiraStatusRoles, extracted for the same measured reason rather than by analogy: the decision is
/// taken twice - once while previewing, once while importing the rows the client sent back - and slice
/// 11 measured that two call sites are two places a rule can be forgotten, and that only one of them
/// had a test.
///
/// There is deliberately <b>no duty branch</b>, and the absence is a finding rather than an omission.
/// Jira's rule asks about the second-level rotation <em>first</em>, because one status means "waiting
/// for the pool" when you are not the pool and "waiting for you" when you are. Azure DevOps has no
/// counterpart here and no setting for one: AdoSettings carries WaitingStates and IncludeWaiting and
/// nothing else, so a duty branch would be inventing a switch the user was never offered - and it
/// would be unreachable, which is worse than absent. If one ever arrives, the branch order over in
/// JiraStatusRoles is load-bearing and says why.
///
/// It answered a bool while there were two roles, on the note that Jira's enum earns itself on three.
/// A third arrived — done — so this is <see cref="AdoStateRole"/> now, and the condition was met
/// rather than worked around: a state can stand in the waiting list and the done list at once, and a
/// bool per question could not say which of them wins.
/// </summary>
public static class AdoStateRoles
{
    /// <summary>
    /// Ordinal, and that is the whole of what can go wrong here. Both sides come from the same
    /// instance in the same spelling - the user picks the list from GET /api/ado/states, and the names
    /// come back on the work items - and this instance really does spell one idea two ways: measured
    /// 2026-08-20, a Test Suite says <c>In Progress</c> where a Bug says <c>Active</c>. A
    /// case-insensitive match would fold two states Azure DevOps keeps apart into one where nobody
    /// could see it happen, which is why the write path refuses to dedupe them case-insensitively
    /// either - see OrdinalNameList in SettingsEndpoints.
    ///
    /// A null or blank state is not waiting. It cannot arrive from the source, which asks for
    /// System.State on every batch read, and the import refuses a row without one - so this is the
    /// answer for a caller that has neither, and it is the same safe reading SettingList.Read gives a
    /// corrupt list: nothing is treated as waiting.
    ///
    /// No trimming. The names compared here are the ones Azure DevOps itself sent back and the ones
    /// SettingsEndpoints trimmed on the way in, so a second trim would be dead code and a question
    /// nobody could answer - the same note JiraStatusRoles carries.
    /// </summary>
    public static AdoStateRole For(string? state, AdoSettings settings)
    {
        if (state is not { } name || string.IsNullOrWhiteSpace(name))
        {
            return AdoStateRole.Actionable;
        }

        // Done first, and the order is load-bearing exactly as JiraStatusRoles.For's is. A state can
        // stand in both lists, and a finished work item is not waiting for anybody — reverse these
        // and a closed item keeps standing as waiting, which both hides the closure offer and leaves
        // it labelled as work somebody still owes you. Guessing without the reason falls the other
        // way, because waiting is the older rule.
        if (settings.DoneStates.Contains(name, StringComparer.Ordinal))
        {
            return AdoStateRole.Done;
        }

        return settings.WaitingStates.Contains(name, StringComparer.Ordinal)
            ? AdoStateRole.Waiting
            : AdoStateRole.Actionable;
    }
}
