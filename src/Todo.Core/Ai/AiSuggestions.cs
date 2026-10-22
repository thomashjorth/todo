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

    public const int MaxReviewPoints = 5;

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

    /// <summary>Trimmed, blanks gone, at most five - the schema asks for that, the prompt is a request.</summary>
    public static IReadOnlyList<string> ReviewPoints(IEnumerable<string?>? suggested) =>
        (suggested ?? []).Select(Clean).OfType<string>().Take(MaxReviewPoints).ToList();

    /// <summary>
    /// The model's actions matched back to the notes by number. A number out of range, a repeated
    /// one or an empty title is dropped rather than guessed at: that note simply gets no suggestion.
    /// The owner is only kept for a note whose own <c>Action Owner</c> is empty - a guess never
    /// overrides what the board says.
    /// </summary>
    public static IReadOnlyDictionary<int, (string Title, string? Owner)> RetroActions(
        IEnumerable<RetroActionAnswer?>? suggested, IReadOnlyList<string?> boardOwners)
    {
        var result = new Dictionary<int, (string Title, string? Owner)>();

        foreach (var action in suggested ?? [])
        {
            var index = (action?.Note ?? 0) - 1;

            if (action is null || index < 0 || index >= boardOwners.Count || result.ContainsKey(index)
                || Clean(action.Title) is not { } title)
            {
                continue;
            }

            var owner = string.IsNullOrWhiteSpace(boardOwners[index]) ? Clean(action.Owner) : null;
            result[index] = (Cut(title), owner);
        }

        return result;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text) => text.Length <= TitleMaxLength ? text : text[..TitleMaxLength].TrimEnd();
}
