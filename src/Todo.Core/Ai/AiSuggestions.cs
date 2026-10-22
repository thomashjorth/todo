using System.Globalization;

namespace Todo.Core.Ai;

/// <summary>
/// What is kept of a model's answer before the user sees it. The JSON schema decides the shape, but
/// not the content: a title can still come back empty, a date in another format, a subtask twice.
/// Every rule here is a repair rather than a refusal, because the rest of a suggestion is still worth
/// having when one field is not.
/// </summary>
public static class AiSuggestions
{
    /// <summary>The task endpoints' own limit, so an accepted suggestion can always be saved.</summary>
    public const int TitleMaxLength = 500;

    public const int MaxSubTasks = 6;

    /// <summary>The typed line is the fallback: it is what the user would have saved with Enter.</summary>
    public static string Title(string? suggested, string typed) => Cut(Clean(suggested) ?? typed.Trim());

    public static string? Requester(string? suggested) => Clean(suggested);

    /// <summary>
    /// The date the model wrote, or nothing. Exactly <c>yyyy-MM-dd</c>, because that is what the
    /// prompt asks for and what the deadline field takes; anything else is read as no deadline rather
    /// than guessed at.
    /// </summary>
    public static DateOnly? Deadline(string? suggested) =>
        DateOnly.TryParseExact(
            suggested?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>
    /// Trimmed, blanks and duplicates gone, nothing the task already has, at most six. Compared
    /// without regard to case, because "Ring til IT" and "ring til it" are one subtask to a reader.
    /// </summary>
    public static IReadOnlyList<string> SubTasks(IEnumerable<string?>? suggested, IEnumerable<string> existing)
    {
        var seen = new HashSet<string>(existing.Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);

        return (suggested ?? [])
            .Select(Clean)
            .OfType<string>()
            .Select(Cut)
            .Where(seen.Add)
            .Take(MaxSubTasks)
            .ToList();
    }

    public static string? Summary(string? suggested) => Clean(suggested);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text) => text.Length <= TitleMaxLength ? text : text[..TitleMaxLength].TrimEnd();
}
