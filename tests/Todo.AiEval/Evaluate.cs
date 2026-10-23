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

        // Scored after AiSuggestions.ListedSteps, like the retro owner: the rule against a lone step
        // is code because no prompt held it, and the code's answer is what the user sees.
        var steps = AiSuggestions.ListedSteps(answer.SubTasks, AiSuggestions.Title(answer.Title, c.Line)).ToList();
        var expected = c.SubTasks ?? [];

        if (steps.Count != expected.Length)
        {
            failures.Add($"{steps.Count} subtasks ({string.Join(" / ", steps)}), expected {expected.Length}");
        }
        else
        {
            failures.AddRange(
                from i in Enumerable.Range(0, expected.Length)
                where !steps[i].Contains(expected[i], StringComparison.OrdinalIgnoreCase)
                select $"subtask {i + 1} \"{steps[i]}\" lacks \"{expected[i]}\"");
        }

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

    /// <summary>
    /// The raw actions are checked note by note, before AiSuggestions drops what it cannot place -
    /// a note the model skipped is a failure here, even though the app would only show it without a
    /// suggestion.
    /// </summary>
    public static async Task<(string, List<string>)> RetroAsync(OllamaClient client, RetroCase c)
    {
        var answer = await client.CompleteAsync<RetroActionsAnswer>(
            AiPrompts.RetroActions([.. c.Notes.Select(n => n.Text)]), AiPrompts.RetroActionsSchema());
        var actions = (answer.Actions ?? []).OfType<RetroActionAnswer>().ToList();
        var failures = new List<string>();

        for (var i = 0; i < c.Notes.Length; i++)
        {
            var note = c.Notes[i];
            var action = actions.FirstOrDefault(a => a.Note == i + 1);

            if (action is null)
            {
                failures.Add($"note {i + 1} got no action");
                continue;
            }

            var title = action.Title ?? string.Empty;
            var firstWord = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();

            failures.AddRange(
                from word in note.TitleContains
                where !title.Contains(word, StringComparison.OrdinalIgnoreCase)
                select $"note {i + 1}: title lacks \"{word}\"");

            if (firstWord is "we" or "i" or "the" or "nobody" or "deploys")
            {
                failures.Add($"note {i + 1}: \"{title}\" is not an action");
            }

            if (Languages.Guess(title) is { } language && language != Language.English)
            {
                failures.Add($"note {i + 1}: written in {language}");
            }

            // The owner is scored after AiSuggestions' check, unlike the rest: the prompt could not
            // make the model stop naming teams, so that rule is code, and the code's answer is what the
            // user sees. The board owner is passed as the app passes it.
            var owner = AiSuggestions.RetroActions([action with { Note = 1 }], [(note.Text, note.BoardOwner)])[0].Owner;

            if (note.Owner != Cases.Any && !string.Equals(owner, note.Owner, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"note {i + 1}: owner {owner ?? "null"}, expected {note.Owner ?? "null"}");
            }
        }

        var shown = string.Join(" | ", actions.OrderBy(a => a.Note).Select(a => $"{a.Note}. {a.Title} ({a.Owner ?? "-"})"));

        return (shown, failures);
    }

    public static async Task<(string, List<string>)> ReviewAsync(OllamaClient client, ReviewCase c)
    {
        var answer = await client.CompleteAsync<ReviewOverviewAnswer>(
            AiPrompts.ReviewOverview(c.Facts), AiPrompts.ReviewOverviewSchema());
        var points = (answer.Points ?? []).Select(p => p?.Trim() ?? string.Empty).ToList();
        var all = string.Join(" ", points);
        var failures = new List<string>();

        if (points.Count is < 1 or > AiSuggestions.MaxReviewPoints)
        {
            failures.Add($"{points.Count} points, expected 1 to {AiSuggestions.MaxReviewPoints}");
        }

        // The user's own finding: the first overview spoke as "Jeg har gennemført...". The points
        // speak to the user, never as the user.
        if (points.Any(p => p.StartsWith("Jeg ", StringComparison.Ordinal)))
        {
            failures.Add("speaks in the first person");
        }

        // The screen shows the points as plain text, so markdown would reach the user as asterisks.
        if (all.Contains("**", StringComparison.Ordinal) || points.Any(p => p.StartsWith('#')))
        {
            failures.Add("uses markdown");
        }

        failures.AddRange(
            from word in c.Mentions
            where !all.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"does not mention \"{word}\"");

        failures.AddRange(
            from title in c.Absent
            where all.Contains(title, StringComparison.OrdinalIgnoreCase)
            select $"invents \"{title}\"");

        if (Languages.Guess(all) is { } language && language != Language.Danish)
        {
            failures.Add($"written in {language}");
        }

        return (string.Join(" | ", points), failures);
    }

    public static async Task<(string, List<string>)> TagsAsync(OllamaClient client, TagCase c)
    {
        var answer = await client.CompleteAsync<TagSuggestionAnswer>(
            AiPrompts.SuggestTags(c.Title, null, Cases.TagNames), AiPrompts.SuggestTagsSchema(Cases.TagNames));
        var picked = (answer.Tags ?? []).OfType<string>().ToList();
        var failures = new List<string>();

        failures.AddRange(c.Required.Where(t => !picked.Contains(t)).Select(t => $"missing {t}"));
        failures.AddRange(c.Forbidden.Where(picked.Contains).Select(t => $"picked {t}"));

        if (c.KnownGap && failures.Count > 0)
        {
            Console.WriteLine($"     known gap: {string.Join("; ", failures)}");
            failures.Clear();
        }

        return (string.Join(", ", picked), failures);
    }

    /// <summary>The tasks are embedded as the app embeds them, and the query with the app's own prefix.</summary>
    public static async Task<(string, List<string>)> SearchAsync(OllamaClient client, SearchCase c)
    {
        var vectors = await client.EmbedAsync(Cases.SearchTasks);
        var query = (await client.EmbedAsync([AiScreenEndpoints.QueryPrefix + c.Query]))[0];
        var ranked = AiSimilarity.Rank(query, vectors.Select((v, i) => ((long)i, v)));
        var best = ranked.Count > 0 ? Cases.SearchTasks[ranked[0]] : null;
        var score = vectors.Max(v => AiSimilarity.Cosine(query, v));
        var failures = new List<string>();

        if (best != c.Expected)
        {
            var message = $"best {best ?? "nothing"}, expected {c.Expected ?? "nothing"}";

            // A known gap is printed, never counted: it is the model's limit, measured and written down.
            Console.WriteLine($"     {(c.KnownGap ? "known gap" : "MISS")}: {message}");

            if (!c.KnownGap)
            {
                failures.Add(message);
            }
        }

        return ($"{best ?? "nothing"} (top score {score:0.00}, {ranked.Count} shown)", failures);
    }

    public static async Task<(string, List<string>)> DuplicateAsync(OllamaClient client, DuplicateCase c)
    {
        var vectors = await client.EmbedAsync([c.Row, c.Existing]);
        var score = AiSimilarity.Cosine(vectors[0], vectors[1]);
        var flagged = AiSimilarity.ClosestDuplicate(vectors[0], [(1, vectors[1])]) is not null;
        List<string> failures = flagged == c.Same
            ? []
            : [$"{(flagged ? "flagged" : "missed")} at {score:0.00} against {AiSimilarity.DuplicateThreshold}"];

        return ($"{score:0.00}", failures);
    }
}
