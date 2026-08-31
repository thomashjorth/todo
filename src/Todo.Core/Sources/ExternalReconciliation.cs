namespace Todo.Core.Sources;

/// <summary>
/// What a source says about the keys the app has already imported.
///
/// Two sets rather than a flag per row, and that is the whole reason ownership never becomes a name
/// comparison: both sources can answer "is it still mine" in their own vocabulary — <c>@Me</c> and
/// <c>currentUser()</c> — so the decision is the server's. Jira's identity is a display name and
/// nothing else, since <c>MyselfBody</c> binds no other field, and a comparison on that would break
/// the day somebody corrects a surname.
///
/// The reading is therefore: a key in <see cref="Found"/> but <em>not</em> in
/// <see cref="StillMine"/> has been assigned to somebody else. A key in neither is one the source
/// does not know — deleted, moved, or outside what the token is allowed to read.
///
/// <see cref="Found"/> reuses <see cref="ExternalTask"/> whole, so a reconciled row is built by the
/// same code as an ordinary preview row and there is no second way into the contract's row type.
/// Two things about it are the contract's rather than each source's, because reusing a source's own
/// mapping quietly gets both wrong:
///
/// <list type="bullet">
/// <item><b><see cref="ExternalTask.Deadline"/> is null on every reconciled item.</b> A reconciled
/// row is never imported as new work, so a suggested deadline is noise on a row whose tick box means
/// "close this one". Azure DevOps has no due date to carry and passes null anyway; Jira's mapping
/// fills the field from <c>duedate</c> in the ordinary path, so it has to be cleared rather than
/// inherited — otherwise one source's reconciled rows carry a deadline and the other's do not, and
/// nothing says which was intended.</item>
/// <item><b>No order is promised.</b> Azure DevOps answers in the batch read's order, which is the
/// server's and not the caller's, while a per-key source naturally answers in the order it was
/// asked. A caller must look items up by <see cref="ExternalTask.Key"/> rather than pair the two
/// lists up positionally.</item>
/// </list>
/// </summary>
public sealed record ExternalReconciliation(
    IReadOnlyList<ExternalTask> Found,
    IReadOnlySet<string> StillMine);
