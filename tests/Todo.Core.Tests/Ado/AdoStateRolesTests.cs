using Todo.Core.Ado;

namespace Todo.Core.Tests.Ado;

/// <summary>
/// A pure function of a state name and two lists, so it belongs here for the reason JiraStatusRolesTests
/// does - and it is the one thing in task 4 that can be wrong without anybody seeing it. A row that
/// should have been waiting simply arrives Open, lands in a deadline section, and looks like work.
/// </summary>
public class AdoStateRolesTests
{
    private static AdoSettings With(
        string[] waitingStates, bool includeWaiting = false, string[]? doneStates = null) =>
        new(
            BaseUrl: "https://ado.example.invalid/Some%20Collection",
            Project: "Saas",
            Token: "a-token",
            WaitingStates: waitingStates,
            IncludeWaiting: includeWaiting,
            DoneStates: doneStates ?? [],
            WorkItemTypes: AdoDefaults.WorkItemTypes,
            DefaultDeadlineDays: AdoDefaults.DeadlineDays);

    [Fact]
    public void A_state_in_the_list_is_waiting()
    {
        Assert.Equal(AdoStateRole.Waiting, AdoStateRoles.For("Blocked", With(["Blocked", "PO Review"])));
    }

    [Fact]
    public void A_state_outside_the_list_is_not_waiting()
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("Active", With(["Blocked", "PO Review"])));
    }

    /// <summary>
    /// Ordinal, and the reason is measured rather than stylistic: this instance spells one idea two
    /// ways - a Test Suite says "In Progress" where a Bug says "Active" - so near-duplicate state names
    /// are the normal case here, and OrdinalIgnoreCase would fold two states Azure DevOps keeps apart
    /// into one where nobody could see it happen. The write path refuses the same fold, which is why
    /// the settings lists do not go through SettingList.Write.
    /// </summary>
    [Theory]
    [InlineData("blocked")]
    [InlineData("BLOCKED")]
    public void A_state_that_differs_only_in_case_is_not_the_waiting_state(string listed)
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("Blocked", With([listed])));
    }

    /// <summary>
    /// The safe reading of an empty list, and it has to be this way round: read as "everything waits"
    /// an unconfigured app would park every imported work item in "Venter på", where nothing is worked
    /// on. Same direction SettingList.Read reads a corrupt value.
    /// </summary>
    [Fact]
    public void An_empty_list_makes_nothing_waiting()
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("Blocked", With([])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_state_that_is_not_there_is_not_waiting(string? state)
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For(state, With(["Blocked", ""])));
    }

    /// <summary>
    /// The pair does two different things, and this is the assertion that keeps them apart. The list is
    /// a <em>mapping</em> - which states mean waiting - while IncludeWaiting is a <em>switch</em> saying
    /// whether those rows may come along anyway. Folding the switch into this rule would make a waiting
    /// row arrive Open the moment the user turned waiting off, which is the opposite of what they asked
    /// for: they asked not to see it, not to be handed it as something to do.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_switch_does_not_decide_what_waiting_means(bool includeWaiting)
    {
        Assert.Equal(
            AdoStateRole.Waiting, AdoStateRoles.For("Blocked", With(["Blocked"], includeWaiting)));
    }

    [Fact]
    public void A_state_in_the_done_list_is_done()
    {
        Assert.Equal(AdoStateRole.Done, AdoStateRoles.For("Resolved", With([], doneStates: ["Resolved"])));
    }

    /// <summary>
    /// The overlap is the whole reason this answers an enum rather than two bools, and this test is the
    /// only thing that can catch the branches being swapped: both outcomes are legal roles, so a
    /// reversal compiles, reads fine, and simply keeps a finished work item standing as waiting - which
    /// hides the closure offer and labels the row as work somebody still owes you.
    /// </summary>
    [Fact]
    public void Done_outranks_waiting_when_a_state_is_in_both_lists()
    {
        Assert.Equal(
            AdoStateRole.Done,
            AdoStateRoles.For("Resolved", With(["Resolved"], doneStates: ["Resolved"])));
    }

    /// <summary>
    /// An empty done list is a valid setting rather than a missing one - it means no suggestions - so
    /// it must not fall the way an empty WorkItemTypes does, where absence restores a default.
    /// </summary>
    [Fact]
    public void An_empty_done_list_makes_nothing_done()
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("Resolved", With([])));
    }

    /// <summary>Ordinal on this side too, for the reason the waiting list is.</summary>
    [Theory]
    [InlineData("resolved")]
    [InlineData("RESOLVED")]
    public void A_done_state_that_differs_only_in_case_is_not_done(string listed)
    {
        Assert.Equal(AdoStateRole.Actionable, AdoStateRoles.For("Resolved", With([], doneStates: [listed])));
    }
}
