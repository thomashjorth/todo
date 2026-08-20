using Todo.Core.Ado;

namespace Todo.Core.Tests.Ado;

/// <summary>
/// Decision B's list, as the two callers actually use it. The normalisation was measured through
/// AdoTaskSource in task 3 - blanks dropped, names trimmed, an empty result refused - and moved here in
/// task 4 when the import turned out to need the same list to ask about one row's type. These are the
/// assertions on the rule itself, so the query text and the row filter cannot drift apart.
/// </summary>
public class AdoWorkItemTypesTests
{
    private static AdoSettings With(params string[] workItemTypes) =>
        new(
            BaseUrl: "https://ado.example.invalid/Some%20Collection",
            Project: "Saas",
            Token: "a-token",
            WaitingStates: [],
            IncludeWaiting: false,
            WorkItemTypes: workItemTypes,
            DefaultDeadlineDays: AdoDefaults.DeadlineDays);

    [Fact]
    public void The_effective_list_is_the_configured_one()
    {
        Assert.Equal(
            new[] { "Bug", "User Story", "Task" },
            AdoWorkItemTypes.Effective(With("Bug", "User Story", "Task")));
    }

    /// <summary>
    /// A blank would become <c>IN (' ')</c> in the WIQL, which is valid and matches nothing, and on the
    /// row side it could only ever hide something. Dropped in one place so both callers agree.
    /// </summary>
    [Fact]
    public void Blanks_are_dropped_and_the_rest_trimmed()
    {
        Assert.Equal(
            new[] { "Bug", "Task" }, AdoWorkItemTypes.Effective(With("  Bug ", "   ", "Task", "")));
    }

    /// <summary>
    /// A list of nothing but blanks is empty, not a list of one. Nothing in the app can store it -
    /// SettingsEndpoints refuses it and an absent row reads as the three defaults - but a hand-edited
    /// row survives both, and both callers refuse on emptiness rather than reading it as every type.
    /// </summary>
    [Fact]
    public void A_list_of_nothing_but_blanks_is_empty()
    {
        Assert.Empty(AdoWorkItemTypes.Effective(With("  ", "")));
    }

    [Fact]
    public void A_configured_type_is_allowed()
    {
        Assert.True(AdoWorkItemTypes.Allows("User Story", With("Bug", "User Story", "Task")));
    }

    /// <summary>
    /// The two test artefacts among the twelve measured work items are what this keeps out - 17% noise
    /// on the instance the day it was measured.
    /// </summary>
    [Fact]
    public void A_type_outside_the_list_is_not_allowed()
    {
        Assert.False(AdoWorkItemTypes.Allows("Test Suite", With("Bug", "User Story", "Task")));
    }

    /// <summary>
    /// Ordinal for the same reason AdoStateRoles is: these are names the instance chose, and folding
    /// case here would let a row through under a type the user did not pick.
    /// </summary>
    [Fact]
    public void A_type_that_differs_only_in_case_is_not_allowed()
    {
        Assert.False(AdoWorkItemTypes.Allows("bug", With("Bug")));
    }

    /// <summary>
    /// Trimmed on the way in, which the state comparison deliberately is not: a type name on an import
    /// row has been out to a client and back, and whitespace from out there is not something this side
    /// gets to trust.
    /// </summary>
    [Fact]
    public void A_type_name_is_trimmed_before_it_is_looked_up()
    {
        Assert.True(AdoWorkItemTypes.Allows("  Bug  ", With("Bug")));
    }

    /// <summary>
    /// Empty is nothing rather than everything - slice 11's lesson, that the absence of a limit is not
    /// a neutral default. Read the other way an import would take the test artefacts the filter exists
    /// to keep out.
    /// </summary>
    [Fact]
    public void Nothing_is_allowed_when_the_list_came_out_empty()
    {
        Assert.False(AdoWorkItemTypes.Allows("Bug", With("   ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_type_that_is_not_there_is_not_allowed(string? workItemType)
    {
        Assert.False(AdoWorkItemTypes.Allows(workItemType, With("Bug", "")));
    }
}
