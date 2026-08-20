namespace Todo.Core.Sources;

/// <summary>
/// One item as an external system handed it over. The last two members were added in slice 12, when
/// the second implementation arrived and the record turned out to be Jira-shaped in exactly two
/// places - see the notes on each.
/// </summary>
public sealed record ExternalTask(
    string Key,
    string Title,
    string? Note,
    // What the source proposes, never what the app must use: the design document's section 4 says a
    // deadline is owned locally and an import only suggests one. Jira fills this from its own due
    // date; Azure DevOps has no such field at all and fills it from AdoDeadline instead.
    DateOnly? Deadline,
    string? Requester,
    string StatusName,
    // The item's own type - a Jira issue type, an Azure DevOps work item type - or null for a source
    // that does not report one. Jira answers null: slice 11 never had a reason to read the issue
    // type, and inventing one here would be worse than saying nothing.
    //
    // Azure DevOps needs it on the row rather than only in the query, for two reasons measured
    // 2026-08-20: state names differ per type on that instance (Test Suite says In Progress where a
    // Bug says Active), so a state shown without its type is ambiguous; and the import has to apply
    // the type filter again rather than trust the client, so the type has to survive the round trip.
    string? ItemType,
    // When the item last changed state, if the source already knows. This is the half of the
    // ITaskSource shape that slice 12 changed: Jira DC 10.3.24 does not return
    // statuscategorychangedate, so its source answers null here and the caller has to pay for
    // ITaskSource.FetchStatusChangedAtAsync per row - while Azure DevOps returns
    // Microsoft.VSTS.Common.StateChangeDate in the same response and simply fills it in.
    //
    // A caller wanting a waiting date reads this first and only calls the method when it is null.
    // Neither a no-op method nor a round trip Azure DevOps does not need: the cheap answer rides on
    // the row, and the expensive one stays available for the source that has no other way.
    //
    // UTC, like every timestamp in this app: SQLite cannot sort a DateTimeOffset, so one must never
    // reach an entity, and the offset is honoured at the boundary before it is dropped.
    DateTime? StatusChangedAt);
