namespace Todo.Core.Ado;

/// <summary>
/// The deadline an Azure DevOps import proposes. Azure DevOps has no due date field at all -
/// measured 2026-08-20, the field list on a Bug has no Microsoft.VSTS.Scheduling.DueDate - so the
/// app invents one from the clock and the user's setting. That is decision A in the slice's plan.
///
/// Its own type because the rule is taken twice and the two callers cannot share a code path: the
/// preview derives it while reading the work items, and the import derives it again from rows the
/// client sent, which deliberately carry no deadline. Same reason JiraStatusRoles was extracted -
/// slice 11 measured that two call sites are two places a rule can be forgotten, and that only one
/// of them had a test.
/// </summary>
public static class AdoDeadline
{
    /// <summary>
    /// Zero means no deadline rather than "today", and that is the whole of what can go wrong here.
    /// The setting is a non-nullable int so the frontend gets no extra @if branch, which makes 0 the
    /// readable "turned off" for a number of days - so it has to answer null, or turning the deadline
    /// off would file every imported work item as due immediately.
    ///
    /// A negative number cannot arrive: the endpoint rejects it and AdoSettingsReader reads an
    /// out-of-range row as the default. It answers null anyway rather than a date in the past,
    /// because "overdue on import" is the one outcome nobody could have asked for.
    ///
    /// Today comes from IClock, so the answer belongs to the server rather than to whichever machine
    /// clicked - the same reason WaitingSince is set server-side. The consequence to know: previewing
    /// today and importing tomorrow gives tomorrow's arithmetic, and that is right, because the date
    /// is relative to the import.
    /// </summary>
    public static DateOnly? For(DateOnly today, int defaultDeadlineDays) =>
        defaultDeadlineDays <= 0 ? null : today.AddDays(defaultDeadlineDays);
}
