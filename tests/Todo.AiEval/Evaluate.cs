using System.Text.Json;
using Todo.Core.Ai;
using Todo.Host.Ai;
using Todo.Host.Endpoints;

namespace Todo.AiEval;

/// <summary>
/// Asks one case and lists what is wrong with the answer. Each check reads the RAW answer before
/// AiSuggestions repairs it, because the eval measures the prompt; the repairs are measured by
/// AiSuggestionsTests and would otherwise hide a prompt that has got worse.
/// </summary>
public static class Evaluate
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<(string, List<string>)> ParseAsync(OllamaClient client, ParseCase c)
    {
        var answer = await client.CompleteAsync<ParsedTaskAnswer>(
            AiPrompts.ParseTask(c.Line, Cases.Today), AiPrompts.ParseTaskSchema());
        var failures = new List<string>();
        var title = answer.Title ?? string.Empty;

        if (c.Deadline != Cases.Any && answer.Deadline != c.Deadline)
        {
            failures.Add($"deadline {answer.Deadline ?? "null"}, expected {c.Deadline ?? "null"}");
        }

        if (c.Requester != Cases.Any && !string.Equals(answer.Requester, c.Requester, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"requester {answer.Requester ?? "null"}, expected {c.Requester ?? "null"}");
        }

        failures.AddRange(
            from word in c.TitleContains
            where !title.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"title lacks \"{word}\"");

        failures.AddRange(
            from word in c.TitleOmits
            where title.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"title keeps \"{word}\"");

        // The line's own language is the expectation, read the same way as the answer's.
        if (Languages.Guess(title) is { } language && language != (Languages.Guess(c.Line) ?? language))
        {
            failures.Add($"title written in {language}, the line in {Languages.Guess(c.Line)}");
        }

        return (JsonSerializer.Serialize(answer, Json), failures);
    }

    public static async Task<(string, List<string>)> SplitAsync(OllamaClient client, SplitCase c)
    {
        var answer = await client.CompleteAsync<SplitAnswer>(
            AiPrompts.Split(c.Title, c.Note, c.Existing, AiEndpoints.LanguageOf(c.Source, $"{c.Title} {c.Note}")), AiPrompts.SplitSchema());
        var items = (answer.SubTasks ?? []).Select(s => s?.Trim() ?? string.Empty).ToList();
        var failures = new List<string>();

        if (items.Count is < 3 or > AiSuggestions.MaxSubTasks)
        {
            failures.Add($"{items.Count} subtasks, expected 3 to {AiSuggestions.MaxSubTasks}");
        }

        failures.AddRange(
            from item in items
            where c.Existing.Any(e => string.Equals(e, item, StringComparison.OrdinalIgnoreCase))
            select $"repeats the existing \"{item}\"");

        failures.AddRange(
            from item in items
            where item.Length == 0 || item.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12
            select $"not a short phrase: \"{item}\"");

        if (items.Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
        {
            failures.Add("the same subtask twice");
        }

        if (Languages.Guess(string.Join(". ", items)) is { } language && language != c.Language)
        {
            failures.Add($"written in {language}, expected {c.Language}");
        }

        return (JsonSerializer.Serialize(items, Json), failures);
    }

    public static async Task<(string, List<string>)> SummaryAsync(OllamaClient client, SummaryCase c)
    {
        var answer = await client.CompleteAsync<SummaryAnswer>(
            AiPrompts.Summarize(c.Title, c.Note, AiEndpoints.LanguageOf(c.Source, $"{c.Title} {c.Note}")), AiPrompts.SummarySchema());
        var summary = answer.Summary?.Trim() ?? string.Empty;
        var failures = new List<string>();

        var sentences = summary.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Count(s => s.Trim().Length > 3);

        // "10.000" and "4.12" split into two "sentences" at their dot, so the count reads numbers
        // apart only when the fragment around them is long enough to be words.
        if (sentences > 2 && summary.Length > 280)
        {
            failures.Add($"{sentences} sentences and {summary.Length} characters, expected at most two");
        }

        if (summary.Contains('\n'))
        {
            failures.Add("more than one line");
        }

        failures.AddRange(
            from word in c.Mentions
            where !summary.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"does not mention \"{word}\"");

        failures.AddRange(
            from word in c.Omits
            where summary.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"keeps \"{word}\"");

        if (Languages.Guess(summary) is { } language && language != c.Language)
        {
            failures.Add($"written in {language}, expected {c.Language}");
        }

        return (summary, failures);
    }
}
