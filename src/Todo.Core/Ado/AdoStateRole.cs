namespace Todo.Core.Ado;

/// <summary>
/// What one Azure DevOps state means to this user right now.
///
/// An enum where slice 12 had a bool, and the class it belongs to predicted the change: <i>"It answers
/// a bool rather than a two-valued enum. Jira's enum earns itself on three roles; two values would be
/// a bool with extra steps."</i> A third role arrived, so the enum earns itself here too - and it has
/// to, because a state can sit in two of the user's lists at once and something must say which wins.
///
/// No Duty member, and the absence is the same finding <see cref="AdoStateRoles"/> records: Azure
/// DevOps has no second-level rotation and no setting for one, so the member would be unreachable.
/// </summary>
public enum AdoStateRole
{
    Actionable,
    Waiting,

    /// <summary>
    /// The work item is finished. Never imported as a new task, and offered as a closure when the key
    /// is already known - see <see cref="AdoStateRoles.For"/> for why it outranks the other two.
    /// </summary>
    Done,
}
