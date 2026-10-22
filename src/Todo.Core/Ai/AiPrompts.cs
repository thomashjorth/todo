using System.Text.Json.Nodes;

namespace Todo.Core.Ai;

/// <summary>
/// What the three AI features ask the model, and the JSON shape each answer must take. Measured
/// against gemma4:e4b on 2026-10-22 - see the three-features design's section 2.
/// </summary>
/// <remarks>
/// The prompts are English, because the model follows instructions best in English and the code is
/// English (ADR 10). Each one asks for the answer "in the same language as" the input instead, which
/// is what the measured runs did: a Danish description got a Danish summary and an English one an
/// English summary.
/// </remarks>
public static class AiPrompts
{
    /// <summary>
    /// How far ahead the calendar in the quick-entry prompt reaches. Two weeks, so "næste mandag"
    /// is in it from any day of this one.
    /// </summary>
    public const int CalendarDays = 14;

    /// <summary>
    /// Danish, in <see cref="DayOfWeek"/> order, written out rather than read from a culture so the
    /// prompt cannot change with the machine's ICU data. Danish because the line the user types is,
    /// and the model looks a word up rather than translating it first.
    /// </summary>
    private static readonly string[] Weekdays =
        ["søndag", "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag"];

    /// <summary>
    /// Turns one typed line into a task. The calendar is the point: given only today's date, the
    /// model put "næste mandag" on 2026-10-27, a Tuesday. With the days written out it looked them up
    /// and got five of five right.
    /// <para>
    /// Terse on purpose. Reading the prompt is most of a call's time on the user's CPU, and it cannot
    /// be cached between two lines: Ollama reused 424 of 429 tokens for an identical prompt and none
    /// when only the line changed (measured 2026-10-22). The terse wording kept the eval at 12 of 12
    /// and took the median from 5.6 s to 4.9 s.
    /// </para>
    /// </summary>
    public static string ParseTask(string text, DateOnly today)
    {
        var calendar = string.Join(
            ", ",
            Enumerable.Range(1, CalendarDays).Select(i => today.AddDays(i)).Select(d => $"{Weekday(d)} {Iso(d)}"));

        return $"""
            Make a to-do task from the line below. Today is {Weekday(today)} {Iso(today)}.
            Coming days: {calendar}.
            title: the line's own words without the due date or the words that introduce it ("på fredag", "til onsdag", "i morgen", "inden 1. november", "tomorrow").
            requester: the person the task is for or about, if named, else null.
            deadline: the due date as YYYY-MM-DD, if named, else null. A bare weekday is its first date above; "næste <weekday>" is the one a week after that.
            {WriteIn(AiLanguage.Of(text))}
            Line: {text}
            """;
    }

    public static JsonObject ParseTaskSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["requester"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["deadline"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        },
        ["required"] = new JsonArray("title", "requester", "deadline"),
    };

    /// <summary>
    /// The existing subtasks are in the prompt so the model does not suggest them again, and they are
    /// filtered out afterwards as well, because a prompt is a request and not a promise.
    /// </summary>
    /// <param name="language">
    /// <see cref="AiLanguage.Danish"/> or <see cref="AiLanguage.English"/>, chosen by the caller from
    /// the task's source - see <see cref="AiLanguage"/>.
    /// </param>
    public static string Split(string title, string? note, IEnumerable<string> existing, string language)
    {
        var known = string.Join("; ", existing);

        return $"""
            Break this task from a to-do list into 3 to {AiSuggestions.MaxSubTasks} concrete subtasks.
            Each subtask is a short imperative phrase. Do not repeat a subtask that already exists.
            {WriteIn(language)}

            Task: {title}
            Note: {(string.IsNullOrWhiteSpace(note) ? "(none)" : note)}
            Existing subtasks: {(known.Length == 0 ? "(none)" : known)}
            """;
    }

    public static JsonObject SplitSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["subTasks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 3,
                ["maxItems"] = AiSuggestions.MaxSubTasks,
            },
        },
        ["required"] = new JsonArray("subTasks"),
    };

    /// <summary>
    /// "Keep the concrete facts" came from the eval: without it a summary said "store datasæt" for
    /// "over 10.000 rækker". "Leave out code" came from it too: with the facts rule alone, a
    /// description holding a SQL statement got the statement copied into its summary.
    /// </summary>
    public static string Summarize(string title, string note, string language) => $"""
        Summarise this issue in at most two short sentences on one line.
        Say what is wrong or wanted and what has to be done. Keep the concrete facts - numbers, limits, system names and error names. Leave out code, greetings, signatures, reproduction steps and history. Plain text, no markdown.
        {WriteIn(language)}

        Title: {title}
        Description:
        {note}
        """;

    public static JsonObject SummarySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["summary"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("summary"),
    };

    /// <summary>
    /// The language is named rather than left to the model, and Norwegian is ruled out in so many
    /// words: the first eval run answered "Call the dentist" with the Norwegian "Ring tannlegen", gave
    /// a Danish task Norwegian subtasks, and summarised a Danish description in English because it
    /// held a block of SQL. See <see cref="AiLanguage"/> for what the model got wrong when asked to
    /// work the language out itself.
    /// </summary>
    private static string WriteIn(string language) => language == AiLanguage.Danish
        ? "Write in Danish - not Norwegian, not Swedish."
        : "Write in English.";

    private static string Weekday(DateOnly day) => Weekdays[(int)day.DayOfWeek];

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
