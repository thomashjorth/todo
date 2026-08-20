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
}
