using Todo.Core.Jira;

namespace Todo.Core.Tests.Jira;

/// <summary>
/// The delivery's core decision, tested where it needs no host. `Afventer general` is the status an
/// issue sits in while it waits for the shared 2nd level pool — so when the user <em>is</em> the
/// pool, the issue is waiting for them and is actionable, not parked. Both lists name it, and the
/// switch is what decides.
/// </summary>
public class JiraStatusRolesTests
{
    private const string Pool = "Afventer general";

    private static JiraSettings With(
        string[]? waiting = null, string[]? duty = null, bool onDuty = false) =>
        new(
            BaseUrl: "https://jira.example.invalid",
            ProjectKey: "SAAS",
            Token: "a-token",
            WaitingStatuses: waiting ?? [],
            IncludeWaiting: false,
            DutyStatuses: duty ?? [],
            OnDuty: onDuty);

    /// <summary>
    /// The overlap is the main case, not an edge case, and this pair — same fixture, opposite switch
    /// — is the whole proof. Reversing the two branches in <c>For</c> fells exactly this test.
    /// </summary>
    [Fact]
    public void A_status_in_both_lists_is_duty_while_on_duty()
    {
        var role = JiraStatusRoles.For(Pool, With(waiting: [Pool], duty: [Pool], onDuty: true));

        Assert.Equal(JiraStatusRole.Duty, role);
    }

    [Fact]
    public void The_same_status_is_waiting_while_off_duty()
    {
        var role = JiraStatusRoles.For(Pool, With(waiting: [Pool], duty: [Pool], onDuty: false));

        Assert.Equal(JiraStatusRole.Waiting, role);
    }

    [Fact]
    public void A_status_only_in_the_duty_list_is_duty_while_on_duty()
    {
        var role = JiraStatusRoles.For(Pool, With(duty: [Pool], onDuty: true));

        Assert.Equal(JiraStatusRole.Duty, role);
    }

    /// <summary>
    /// The duty switch says nothing about a status the rotation does not cover. `Venter på support`
    /// waits for somebody outside the team whether or not this week is the user's.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_status_only_in_the_waiting_list_is_waiting_either_way(bool onDuty)
    {
        var role = JiraStatusRoles.For(
            "Venter på support",
            With(waiting: ["Venter på support"], duty: [Pool], onDuty: onDuty));

        Assert.Equal(JiraStatusRole.Waiting, role);
    }

    [Fact]
    public void A_status_in_neither_list_is_actionable()
    {
        var role = JiraStatusRoles.For("I gang", With(waiting: [Pool], duty: [Pool], onDuty: true));

        Assert.Equal(JiraStatusRole.Actionable, role);
    }

    /// <summary>
    /// Ordinal, and that is a choice rather than an accident: the names come from the instance in the
    /// same spelling both ways. Slice 11 measured that the same choice on the waiting list was
    /// unguarded until a test spelled it out, so the duty list gets one from the start — "Afventer
    /// Kunden" and "Afventer kunden" <em>can</em> be two statuses in Jira, and folding them together
    /// would happen where nobody could see it.
    /// </summary>
    [Fact]
    public void A_duty_status_that_differs_only_in_case_is_not_duty()
    {
        var role = JiraStatusRoles.For(Pool, With(duty: ["afventer general"], onDuty: true));

        Assert.Equal(JiraStatusRole.Actionable, role);
    }
}
