namespace Todo.Core.Jira;

/// <summary>
/// What a Jira status means to this user right now. Four roles, not two: Duty and Actionable both
/// import as Open, but only Duty is labelled on screen, and Waiting and Done each pay for a changelog
/// call - Waiting to date the wait, Done to date the closure.
/// </summary>
public enum JiraStatusRole
{
    Actionable,
    Duty,
    Waiting,

    /// <summary>
    /// The issue is finished. Never imported as a new task, and offered as a closure when the key is
    /// already known - see <see cref="JiraStatusRoles.For"/> for why it outranks the other three.
    /// </summary>
    Done,
}
