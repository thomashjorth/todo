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
    /// Done without standing in any list, and that is not a convenience. <b>The Azure DevOps picker can
    /// never offer the name:</b> FetchStatusNamesAsync builds on AdoTaskSource.BaseQuery, which excludes
    /// this very state, and the frontend's <c>stateOptions()</c> only puts back the names that are
    /// <em>already</em> chosen. That is a chicken-and-egg - it cannot be picked until it has been picked
    /// - so a setting here would be one the user was never offered, the same objection that keeps a duty
    /// branch out of this class. Note what is <em>not</em> claimed: <c>PUT /api/settings</c> validates no
    /// state name, so a list can be made to hold this one; no path the user has puts it there.
    ///
    /// Without this name, reconciling against the source reads a closed work item as
    /// <see cref="AdoStateRole.Actionable"/>, meaning "still open", which is the exact opposite of the
    /// truth and is the most ordinary outcome there is. The app asserts it in one place already:
    /// BaseQuery excludes precisely this name, on the measured note that it is the one name every work
    /// item type on this instance shares. The rule hereby moves up to the place where the role is
    /// decided, rather than staying implied by a WHERE clause.
    ///
    /// Public so that BaseQuery is built from it rather than spelling it a second time: two spellings of
    /// one fact are invisible to the compiler, the same class of trap as a route template's
    /// <c>:guid</c> against its parameter type. What that does and does not buy is written out on
    /// BaseQuery, which is where somebody editing the query is standing when they need it.
    ///
    /// <b>The rule is unobservable today, and that is the trap for whoever reconciles next.</b> No path
    /// the app drives itself can produce this state, because everything is built on BaseQuery. Reuse
    /// BaseQuery for a reconciling query, or hydrate without asking for System.State, and this branch
    /// never fires - with every test still green, because a rule that is never reached fails nothing.
    /// FetchStatusNamesAsync sits right beside it as a worked example of exactly that reuse.
    /// </summary>
    public const string ClosedState = "Closed";

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
    /// nobody could answer - the same note JiraStatusRoles carries. Symmetry is <em>not</em> the reason
    /// on the built-in side, where the left operand is a literal we wrote: that comparison rests on the
    /// same untrimmed System.State the list side does, so a source that ever padded the field would
    /// defeat both halves at once rather than one of them.
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
        // way, because waiting is the older rule. The built-in name is answered on this branch for that
        // same reason, and it was measured rather than reasoned: move the clause below the waiting
        // branch and every test stays green except the one that names Closed as waiting. What is
        // load-bearing is the branch, not the operand order inside the || - both operands are pure, so
        // swapping them is measurably invisible to every test in the suite. Why the name is built in at
        // all is on ClosedState.
        if (string.Equals(name, ClosedState, StringComparison.Ordinal)
            || settings.DoneStates.Contains(name, StringComparer.Ordinal))
        {
            return AdoStateRole.Done;
        }

        return settings.WaitingStates.Contains(name, StringComparer.Ordinal)
            ? AdoStateRole.Waiting
            : AdoStateRole.Actionable;
    }
}
