namespace Todo.Core.Sources;

/// <summary>
/// One external system that can hand over the items assigned to you. Jira implements it in slice
/// 11; ADO follows in slice 12, and that is when it shows whether the shape holds. Deliberately
/// not an IMentionSource — Jira has no mentions to fetch, and forcing it to throw would be worse
/// than two interfaces (design document, section 6).
/// </summary>
public interface ITaskSource
{
    /// <summary>What lands in <c>TaskItem.SourceId</c>.</summary>
    string SourceId { get; }

    /// <summary>Who the stored credential belongs to. This is what "Test connection" answers.</summary>
    Task<SourceIdentity> TestAsync(CancellationToken ct = default);

    /// <summary>The status names the configured project uses, so the user can pick from them.</summary>
    Task<IReadOnlyList<string>> FetchStatusNamesAsync(CancellationToken ct = default);

    Task<ExternalTaskPage> FetchAssignedAsync(CancellationToken ct = default);

    /// <summary>
    /// When the item last changed status, for a source that cannot say so on the row itself. A
    /// separate call on purpose: Jira DC 10.3.24 does not return statuscategorychangedate (measured
    /// 2026-08-18), so it comes from the changelog, and only the rows that need it should pay for it.
    ///
    /// Slice 12 measured that Azure DevOps does not need it - Microsoft.VSTS.Common.StateChangeDate
    /// arrives with the work item - so <see cref="ExternalTask.StatusChangedAt"/> was added and this
    /// became the fallback rather than the only way. Read the row's field first and call this only
    /// when it is null. The method stays on the interface because the cheaper path is an ability, not
    /// a requirement: a source that has no such field must still be able to answer, and Jira is one.
    /// It is not a no-op for Azure DevOps either - it reads the single work item - so a caller that
    /// asks gets a real answer rather than a silent null.
    /// </summary>
    Task<DateTime?> FetchStatusChangedAtAsync(string externalKey, CancellationToken ct = default);

    /// <summary>
    /// What the source says about keys the app already holds. Called only with the keys that are open
    /// locally and were <em>missing</em> from <see cref="FetchAssignedAsync"/>'s answer, because the
    /// rest already have a row — so the set is bounded by the user's working list rather than by the
    /// archive, and is typically a handful.
    ///
    /// It exists because both sources' fetch filters on assignment. Hand an item to somebody else and
    /// it drops straight out of the answer, leaving the app unable to tell "nothing happened" from
    /// "it is not mine any more". Absence cannot be used as that signal: change the project key or
    /// the type filter and every task looks gone at once, on precisely the day the user touched a
    /// setting.
    ///
    /// A key the source does not know must be answered as an <em>absence from both sets</em> rather
    /// than as an exception. The deleted item is one of the outcomes this method exists to report, so
    /// throwing would fail the whole preview on the very case it was written for.
    ///
    /// That puts an obligation on the caller, because absence is not self-describing: it reads as
    /// "gone from the source", and the app's answer to gone is to offer to close the task. So pass
    /// only keys this source itself wrote — filter on <see cref="SourceId"/> — and pass them in the
    /// spelling it wrote them in. A key an implementation cannot interpret is absent for that reason
    /// instead, and the caller cannot tell the two apart; Azure DevOps reads its keys as numbers, so
    /// <c>0016901</c> comes back as <c>16901</c> and the caller's own spelling is in neither set.
    /// Either way a live item is reported as deleted.
    /// </summary>
    Task<ExternalReconciliation> ReconcileAsync(
        IReadOnlyList<string> keys, CancellationToken ct = default);
}
