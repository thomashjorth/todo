namespace Todo.Core.Errors;

/// <summary>
/// The <c>code</c> the API puts on every 400 so the frontend can translate it.
/// </summary>
public static class ErrorCodes
{
    // A code is an identity, not a description: once shipped it must never be renamed,
    // or a frontend on an older translation file silently loses the message.

    /// <summary>
    /// The password does not match. Also what an empty password gets: there is no field to point at,
    /// and telling the two apart would only help somebody guessing.
    /// </summary>
    public const string AuthWrongPassword = "auth.wrongPassword";

    /// <summary>Ten attempts a minute from one address; the eleventh waits for the window to turn.</summary>
    public const string AuthTooManyAttempts = "auth.tooManyAttempts";

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
    /// Both an error code and the value of <c>excluded</c> on a preview row, so the frontend
    /// translates it with the same function it uses for <c>ApiError.code</c> - see
    /// <see cref="JiraExcludedWaiting"/>.
    ///
    /// Reconciliation asked about a key that is still assigned to you and still unresolved, so
    /// nothing has changed and there is nothing to suggest - it simply did not come back in the
    /// fetch. Shown rather than hidden, the same choice <see cref="JiraExcludedWaiting"/> makes: a
    /// row that vanished would leave "the app said nothing" meaning both "nothing changed" and "I
    /// could not work it out", and that is the difference between a feature you trust and one you
    /// check by hand afterwards.
    /// </summary>
    public const string JiraOutsideFilter = "jira.outsideFilter";

    /// <summary>
    /// Both an error code and the value of <c>excluded</c> on a preview row, like its sibling
    /// <see cref="JiraOutsideFilter"/>.
    ///
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
    /// Not a row's code but the whole preview's: reconciliation asked about more than one key and
    /// Jira recognised none of them, so the rows those keys would have produced are left out and this
    /// stands in their place.
    ///
    /// It exists because the two readings of that answer are not equally likely. Every key gone at
    /// once is what a lost project permission looks like — Jira Data Center hides an unbrowsable issue
    /// behind <c>404 Issue Does Not Exist</c> — while a whole working list deleted in one go is rare.
    /// So the app names the likelier cause once instead of printing
    /// <see cref="JiraMissingInSource"/> against every task the user still has open, which reads as
    /// an invitation to clear them out by hand.
    ///
    /// <b>More than one key, because with one asked all and one are the same answer</b> and a single
    /// deleted issue must still be reported. The false negative that leaves is accepted and written
    /// down in the design's section 5: really delete both of your only two reconciled issues and
    /// neither is reported.
    /// </summary>
    public const string JiraNoneRecognized = "jira.noneRecognized";

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
    /// The counterpart of <see cref="JiraNoneRecognized"/>, which carries the whole reasoning. Azure
    /// DevOps reaches the same state by a different route: a token that may no longer read the
    /// project, where the batch read is asked with <c>errorPolicy=omit</c> and every id is therefore
    /// omitted rather than refused. <b>That route is unmeasured</b> — nobody has put an unauthorised
    /// token to the instance — which is an argument for the guard rather than against it.
    /// </summary>
    public const string AdoNoneRecognized = "ado.noneRecognized";

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

    /// <summary>
    /// Ollama did not answer, or does not have the model. The user's move is to start Ollama, which
    /// is why this is told apart from <see cref="AiUnreadableAnswer"/>.
    /// </summary>
    public const string AiUnavailable = "ai.unavailable";

    /// <summary>The model answered, but not in the requested shape. The user's move is to try again.</summary>
    public const string AiUnreadableAnswer = "ai.unreadableAnswer";

    public const string AiTextRequired = "ai.textRequired";

    /// <summary>A summary was asked for a task whose note is empty, so there is nothing to summarise.</summary>
    public const string AiNoteRequired = "ai.noteRequired";

    public const string TagNameRequired = "tags.nameRequired";

    public const string TagNameTooLong = "tags.nameTooLong";

    /// <summary>A tag of that name exists already, compared without regard to case.</summary>
    public const string TagDuplicate = "tags.duplicate";

    /// <summary>A task was given a tag id that does not exist - deleted meanwhile, most likely.</summary>
    public const string TagUnknown = "tags.unknown";

    /// <summary>A colour outside the palette's 21 slots (TagColors.Count).</summary>
    public const string TagColorInvalid = "tags.colorInvalid";
}
