namespace Todo.Core.Ado;

/// <summary>
/// What an absent settings row reads as, and the bound the endpoint validates against. One spelling,
/// because the reader and the writer have to agree: AdoSettingsReader turns the absence of a row into
/// these values, and SettingsEndpoints stores a row only when the request asks for something else. Two
/// spellings would let a saved default become a stored row, and a stored row is what the two language
/// tests in SettingsEndpointsTests assert cannot appear.
/// </summary>
public static class AdoDefaults
{
    /// <summary>
    /// Azure DevOps has no due date field at all - measured 2026-08-20, see the plan's finding - so the
    /// app proposes one instead of importing one. Three days is the user's decision.
    ///
    /// This number is written down three times and that is deliberate, not duplication: here, as
    /// <c>default: 3</c> on SettingsRequest.adoDefaultDeadlineDays in the contract, and as the property
    /// initializer NSwag generates from it. The contract's copy is what makes an absent field on the
    /// wire bind to 3 rather than to 0, and 0 is a meaningful value here rather than a missing one.
    /// <c>An_absent_deadline_days_field_binds_to_the_default_rather_than_zero</c> ties the two together.
    /// </summary>
    public const int DeadlineDays = 3;

    /// <summary>
    /// Above this a number of days is a typo rather than an intention - 300 for 3 - so it is rejected
    /// rather than stored. Below zero is rejected too, which would mean "overdue on import".
    /// </summary>
    public const int DeadlineDaysMax = 365;

    /// <summary>
    /// The types worth importing, from the user's decision B. Two of the twelve work items measured on
    /// the instance were a Test Plan and a Test Suite - test artefacts rather than work somebody
    /// solves, 17% noise.
    ///
    /// The list is a requirement, not an optional filter: an empty list is rejected on PUT, and an
    /// absent row reads as this. The plan first said an empty list meant every type; that is the same
    /// trap as the empty Jira project key, where the absence of a limit was read as a neutral default,
    /// and the storage cannot carry the distinction anyway - an empty list is stored as no row, so the
    /// reader cannot tell never-configured from deliberately-emptied.
    /// </summary>
    public static readonly IReadOnlyList<string> WorkItemTypes = ["Bug", "User Story", "Task"];
}
