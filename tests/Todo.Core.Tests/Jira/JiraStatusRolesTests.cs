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
        string[]? waiting = null,
        string[]? duty = null,
        bool onDuty = false,
        string[]? done = null) =>
        new(
            BaseUrl: "https://jira.example.invalid",
            ProjectKey: "SAAS",
            Token: "a-token",
            WaitingStatuses: waiting ?? [],
            IncludeWaiting: false,
            DutyStatuses: duty ?? [],
            OnDuty: onDuty,
            DoneStatuses: done ?? []);

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

    [Fact]
    public void A_status_in_the_done_list_is_done()
    {
        var role = JiraStatusRoles.For("Løst", With(done: ["Løst"]));

        Assert.Equal(JiraStatusRole.Done, role);
    }

    /// <summary>
    /// Done outranks both of the older rules, and this pair is the only thing that can catch the
    /// branch being moved: every outcome here is a legal role, so a reversal compiles and reads fine.
    /// It would simply leave a finished issue standing as waiting — or, with the rotation on, as the
    /// pool's — which hides the closure offer behind a label saying somebody still owes you the work.
    /// The duty half is the sharper of the two, because duty is itself a rule that wins.
    /// </summary>
    [Fact]
    public void Done_outranks_waiting_when_a_status_is_in_both_lists()
    {
        var role = JiraStatusRoles.For("Løst", With(waiting: ["Løst"], done: ["Løst"]));

        Assert.Equal(JiraStatusRole.Done, role);
    }

    [Fact]
    public void Done_outranks_duty_even_while_the_rotation_is_on()
    {
        var role = JiraStatusRoles.For(Pool, With(duty: [Pool], onDuty: true, done: [Pool]));

        Assert.Equal(JiraStatusRole.Done, role);
    }

    /// <summary>
    /// An empty done list is a valid setting rather than a missing one — it means no suggestions.
    /// </summary>
    [Fact]
    public void An_empty_done_list_makes_nothing_done()
    {
        Assert.Equal(JiraStatusRole.Actionable, JiraStatusRoles.For("Løst", With()));
    }

    /// <summary>Ordinal on this list too, for the reason the two above it are.</summary>
    [Fact]
    public void A_done_status_that_differs_only_in_case_is_not_done()
    {
        var role = JiraStatusRoles.For("Løst", With(done: ["løst"]));

        Assert.Equal(JiraStatusRole.Actionable, role);
    }
}
