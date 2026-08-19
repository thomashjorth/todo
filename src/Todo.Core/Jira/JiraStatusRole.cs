namespace Todo.Core.Jira;

/// <summary>
/// What a Jira status means to this user right now. Three roles, not two: Duty and Actionable both
/// import as Open, but only Duty is labelled on screen, and only Waiting pays for a changelog call.
/// </summary>
public enum JiraStatusRole
{
    Actionable,
    Duty,
    Waiting,
}
