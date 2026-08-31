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
/// </summary>
public sealed record ExternalReconciliation(
    IReadOnlyList<ExternalTask> Found,
    IReadOnlySet<string> StillMine);
