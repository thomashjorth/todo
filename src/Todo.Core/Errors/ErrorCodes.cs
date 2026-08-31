namespace Todo.Core.Errors;

/// <summary>
/// The <c>code</c> the API puts on every 400 so the frontend can translate it.
/// </summary>
public static class ErrorCodes
{
    // A code is an identity, not a description: once shipped it must never be renamed,
    // or a frontend on an older translation file silently loses the message.

    public const string TaskTitleRequired = "task.titleRequired";
    public const string TaskTitleTooLong = "task.titleTooLong";

    public const string SubTaskTitleRequired = "subTask.titleRequired";
    public const string SubTaskTitleTooLong = "subTask.titleTooLong";

    public const string RetroEmptyExport = "retro.emptyExport";
    public const string RetroMissingContentColumn = "retro.missingContentColumn";
    public const string RetroRowKeyRequired = "retro.rowKeyRequired";
    public const string RetroRowTitleRequired = "retro.rowTitleRequired";
    public const string RetroRowTitleTooLong = "retro.rowTitleTooLong";
    public const string RetroDuplicateAlias = "retro.duplicateAlias";

    public const string SettingsUnknownLanguage = "settings.unknownLanguage";
    public const string SettingsEmptyToken = "settings.emptyToken";
    public const string SettingsDuplicateDelegate = "settings.duplicateDelegate";

    public const string JiraNotConfigured = "jira.notConfigured";
    public const string JiraProjectKeyRequired = "jira.projectKeyRequired";
    public const string JiraRefused = "jira.refused";
    public const string JiraUnreachable = "jira.unreachable";
    public const string JiraRowKeyRequired = "jira.rowKeyRequired";
    public const string JiraRowTitleRequired = "jira.rowTitleRequired";
    public const string JiraRowTitleTooLong = "jira.rowTitleTooLong";
    public const string JiraRowStatusRequired = "jira.rowStatusRequired";
    public const string JiraStatusNameInvalid = "jira.statusNameInvalid";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend
    /// translates it with the same function it uses for <c>ApiError.code</c>.
    /// </summary>
    public const string JiraExcludedWaiting = "jira.excludedWaiting";

    /// <summary>
    /// The issue stands in a status the user calls finished and has never been imported, so importing
    /// it would create a task that is already over. Shown rather than hidden, the same choice the
    /// waiting rows make: a hidden row would look like one Jira had lost.
    /// </summary>
    public const string JiraExcludedDone = "jira.excludedDone";

    /// <summary>
    /// Reconciliation asked about a key that is still assigned to you and still unresolved, so
    /// nothing has changed and there is nothing to suggest - it simply did not come back in the
    /// fetch. Shown rather than hidden, the same choice <see cref="JiraExcludedWaiting"/> makes: a
    /// row that vanished would leave "the app said nothing" meaning both "nothing changed" and "I
    /// could not work it out", and that is the difference between a feature you trust and one you
    /// check by hand afterwards.
    /// </summary>
    public const string JiraOutsideFilter = "jira.outsideFilter";

    /// <summary>
    /// Reconciliation asked about a key and Jira answered with neither the issue nor an ownership -
    /// deleted, moved, or outside what the token may read. Shown rather than acted on, because the
    /// absence cannot say which of those it was: Jira Data Center is documented to hide an issue you
    /// may not browse behind <c>404 Issue Does Not Exist</c> rather than a 403, so a lost permission
    /// and a deleted issue would arrive as the same answer. <b>That behaviour is unmeasured against
    /// this user's instance</b> - see the note above <c>JiraTaskSource.ReconcileAsync</c> - which is
    /// an argument for reporting the absence rather than against it.
    /// </summary>
    public const string JiraMissingInSource = "jira.missingInSource";

    /// <summary>
    /// The work item type filter is a requirement rather than an optional filter, so an empty list is
    /// refused instead of being read as "every type" - the same rule as
    /// <see cref="JiraProjectKeyRequired"/>, and for the same reason: the absence of a limit is not a
    /// neutral default. The message must read whole without the value, because api-error-message.ts
    /// translates without params.
    /// </summary>
    public const string AdoWorkItemTypesRequired = "ado.workItemTypesRequired";

    /// <summary>
    /// A number of days below 0 or above <see cref="Ado.AdoDefaults.DeadlineDaysMax"/>. Negative would
    /// mean "overdue the moment it is imported", and 300 is a plausible typo for 3.
    /// </summary>
    public const string AdoDefaultDeadlineDaysInvalid = "ado.defaultDeadlineDaysInvalid";

    /// <summary>
    /// The collection URL or the token is missing, which is what <c>AdoSettings.IsConfigured</c> asks.
    /// </summary>
    public const string AdoNotConfigured = "ado.notConfigured";

    /// <summary>
    /// The project is blank. Deliberately its own refusal rather than part of
    /// <see cref="AdoNotConfigured"/>, even though Azure DevOps puts the project in the path: slice 11
    /// measured that folding a missing project into "not configured" tells the user the whole thing is
    /// unset when one field is blank. Its counterpart is <see cref="JiraProjectKeyRequired"/>.
    /// </summary>
    public const string AdoProjectRequired = "ado.projectRequired";

    public const string AdoRefused = "ado.refused";
    public const string AdoUnreachable = "ado.unreachable";

    public const string AdoRowKeyRequired = "ado.rowKeyRequired";
    public const string AdoRowTitleRequired = "ado.rowTitleRequired";
    public const string AdoRowTitleTooLong = "ado.rowTitleTooLong";

    /// <summary>
    /// A row without its Azure DevOps state name. The state is what waiting-ness is derived from, and
    /// the counterpart of <see cref="JiraRowStatusRequired"/> for the same measured reason: an absent
    /// string is null and can be refused, while an absent bool would arrive as <c>false</c>, a legal
    /// value nothing could refuse.
    /// </summary>
    public const string AdoRowStateRequired = "ado.rowStateRequired";

    /// <summary>
    /// A row without its work item type. Jira has no counterpart, because Jira's import has no filter
    /// to re-apply: the type is what decision B's filter is applied to on the way in, so a row without
    /// one would be silently skipped as "not a type you asked for" - which looks like a lost row rather
    /// than a refusal.
    /// </summary>
    public const string AdoRowWorkItemTypeRequired = "ado.rowWorkItemTypeRequired";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend translates
    /// it with the same function it uses for <c>ApiError.code</c> - see
    /// <see cref="JiraExcludedWaiting"/>.
    /// </summary>
    public const string AdoExcludedWaiting = "ado.excludedWaiting";

    /// <summary>
    /// The counterpart of <see cref="JiraExcludedDone"/>: a finished work item that was never
    /// imported, kept out rather than brought in as a fresh open task.
    /// </summary>
    public const string AdoExcludedDone = "ado.excludedDone";

    /// <summary>
    /// The counterpart of <see cref="JiraOutsideFilter"/>: a work item that is still assigned to you
    /// and still open, which the fetch nonetheless left out - the type filter and the state clause
    /// both narrow it.
    /// </summary>
    public const string AdoOutsideFilter = "ado.outsideFilter";

    /// <summary>
    /// The counterpart of <see cref="JiraMissingInSource"/>: a work item the batch read did not
    /// return. What a token that may not read the project answers instead is <b>unmeasured</b>, and
    /// that is the reason the absence is reported rather than acted on - an answer nobody has seen
    /// is not one to build a suggestion to close tasks upon.
    /// </summary>
    public const string AdoMissingInSource = "ado.missingInSource";

    /// <summary>
    /// A work item type name carrying a quotation mark or a backslash. Those two characters are what
    /// WIQL's string literals turn on, and a type name goes into one - the same blocklist, and the
    /// same reason for refusing rather than escaping, as <see cref="JiraStatusNameInvalid"/>.
    /// </summary>
    public const string AdoWorkItemTypeInvalid = "ado.workItemTypeInvalid";

    /// <summary>
    /// A weekday outside 0-6. Refused rather than clamped, so a typo is visible instead of quietly
    /// becoming Sunday - and 0 is <em>not</em> out of range: it is Sunday.
    /// </summary>
    public const string ReviewWeekdayInvalid = "review.weekdayInvalid";

    /// <summary>
    /// A time of day that will not parse as HH:mm. Its message must read whole without the value:
    /// api-error-message.ts calls translate(key) with no params, so a {{value}} placeholder would
    /// reach the user unrendered.
    /// </summary>
    public const string ReviewTimeOfDayInvalid = "review.timeOfDayInvalid";

    public const string SystemUnsupportedScheme = "system.unsupportedScheme";
}
