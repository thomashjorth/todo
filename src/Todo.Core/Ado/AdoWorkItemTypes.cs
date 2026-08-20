namespace Todo.Core.Ado;

/// <summary>
/// Which work item types an import takes, which is decision B. Its own type for the same reason
/// AdoDeadline and AdoStateRoles are: the answer is needed in two shapes that cannot share a code
/// path. AdoTaskSource turns it into a WIQL <c>IN (...)</c> clause before anything goes out, and the
/// import endpoint asks it about one row's type after the client sent that type back - because the
/// import never calls Azure DevOps and therefore cannot let the query do the filtering.
/// </summary>
public static class AdoWorkItemTypes
{
    /// <summary>
    /// The list as it is actually used: trimmed, with blanks dropped. Both callers need exactly this,
    /// and they need it to be the same list. Blanks are dropped rather than kept because a blank in a
    /// WIQL literal becomes <c>IN (' ')</c>, which is valid and matches nothing - a silent failure -
    /// and because no work item's type is blank either, so keeping one could only ever hide a row.
    ///
    /// It can still come out empty, even though nothing in the app can store an empty list:
    /// AdoSettingsReader reads an absent row as AdoDefaults.WorkItemTypes and SettingsEndpoints
    /// refuses an all-blank list, but a hand-edited row holding nothing but blanks survives both. That
    /// is why both callers refuse on empty rather than reading it as "every type" - slice 11's lesson
    /// applies literally, that the absence of a limit is not a neutral default, and the two test
    /// artefacts among the twelve measured work items are what it keeps out.
    /// </summary>
    public static IReadOnlyList<string> Effective(AdoSettings settings) =>
    [
        .. settings.WorkItemTypes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
    ];

    /// <summary>
    /// Whether one row's work item type is a type the user asked to import. Ordinal for the reason
    /// AdoStateRoles is ordinal: the names come from the instance, and <c>Task</c> and <c>task</c>
    /// would be two types there.
    ///
    /// Trims the incoming name, which the state comparison deliberately does not. The difference is
    /// where the value has been: a state name arrives from Azure DevOps in the same request that is
    /// being mapped, while a type name on an import row has been out to the client and back, and a
    /// client is not something this side gets to trust about whitespace.
    ///
    /// Blank is not allowed, and nothing is allowed once the list came out empty. The import refuses a
    /// row without a type before it asks, so a false answer here means the user filtered that type
    /// out.
    /// </summary>
    public static bool Allows(string? workItemType, AdoSettings settings) =>
        workItemType is { } name
        && !string.IsNullOrWhiteSpace(name)
        && Effective(settings).Contains(name.Trim(), StringComparer.Ordinal);
}
