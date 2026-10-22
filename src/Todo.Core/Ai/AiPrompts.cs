using System.Text;
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
    /// and got five of five right. It costs about two seconds of prompt reading per call.
    /// </summary>
    public static string ParseTask(string text, DateOnly today)
    {
        var calendar = new StringBuilder();

        for (var i = 1; i <= CalendarDays; i++)
        {
            var day = today.AddDays(i);
            calendar.Append("- ").Append(Weekday(day)).Append(' ').AppendLine(Iso(day));
        }

        return $"""
            You turn one line that a person typed into their to-do list into a task.
            Today is {Weekday(today)} {Iso(today)}. The next {CalendarDays} days are:
            {calendar.ToString().TrimEnd()}

            Return:
            - title: the task, in the same language and close to the same words as the line, without the date.
            - requester: the person the task is for or about, if the line names one. Otherwise null.
            - deadline: the date the task is due, as YYYY-MM-DD, if the line names one. Otherwise null. A weekday on its own means the first such day in the list above; "næste <weekday>" means the one in the week after this one.

            The line:
            {text}
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
    public static string Split(string title, string? note, IEnumerable<string> existing)
    {
        var known = string.Join("; ", existing);

        return $"""
            Break this task from a to-do list into 3 to {AiSuggestions.MaxSubTasks} concrete subtasks.
            Each subtask is a short imperative phrase in the same language as the task. Do not repeat a subtask that already exists.

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

    public static string Summarize(string title, string note) => $"""
        Summarise this issue in at most two short sentences, in the same language as the description.
        Say what is wrong or wanted and what has to be done. Leave out greetings, signatures, reproduction steps and history. Plain text, no markdown.

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

    private static string Weekday(DateOnly day) => Weekdays[(int)day.DayOfWeek];

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
