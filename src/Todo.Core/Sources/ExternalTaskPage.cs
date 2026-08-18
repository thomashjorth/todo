namespace Todo.Core.Sources;

/// <summary>
/// The items plus what the source said the total was, so a page that got truncated is visible
/// rather than looking like the whole answer.
/// </summary>
public sealed record ExternalTaskPage(IReadOnlyList<ExternalTask> Items, int Total);
