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
            title: the line's own words without the due date or the words that introduce it ("på fredag", "til onsdag", "i morgen", "inden 1. november", "tomorrow"). When the line lists steps, the title is only what they add up to - "Lav en præsentation", "Flyt kontoret" - and the steps go in subTasks.
            subTasks: the steps the line itself lists, in its order and words ("først udkast så finpudsning" gives "Udkast", "Finpudsning"). An empty list when it lists none; never add a step it does not name.
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
            ["subTasks"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("title", "requester", "deadline", "subTasks"),
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
    /// Picks none or more of the user's own tags for a task. The schema's <c>enum</c> is what keeps it
    /// to the user's tags - a model told "from the list only" is asked, a schema is enforced - and
    /// "none is fine" is said in so many words, because a suggestion nobody needs costs a click.
    /// </summary>
    public static string SuggestTags(string title, string? note, IReadOnlyList<string> tags) => $"""
        Go through the tags one at a time and pick each one that fits this to-do task - what the task does or is about falls under what the tag names. Several can fit, and none is a fine answer.
        Tags: {string.Join(", ", tags)}
        Task: {title}
        Note: {(string.IsNullOrWhiteSpace(note) ? "(none)" : note.Length <= 600 ? note : note[..600])}
        """;

    public static JsonObject SuggestTagsSchema(IReadOnlyList<string> tags) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["tags"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray([.. tags.Select(t => (JsonNode)t)]),
                },
            },
        },
        ["required"] = new JsonArray("tags"),
    };

    /// <summary>
    /// Turns retro notes into actions, all in one call: on the user's CPU every call costs seconds,
    /// so one per row would multiply the wait by the number of rows. The notes are numbered, and the
    /// answer refers back by number, because a note's text is not unique enough to match on. English,
    /// because retro boards always are (the user, 2026-10-22).
    /// </summary>
    public static string RetroActions(IReadOnlyList<string> notes)
    {
        var numbered = string.Join("\n", notes.Select((note, i) => $"{i + 1}. {note}"));

        return $"""
            These notes come from a team retrospective board. Rewrite each as one short action someone can do, starting with a verb. Keep what the note is about - its problem, not only the next step - and as many of its words as you can. A note that is already an action stays close to as it is.
            owner: the first name of the person who should do it, only if the note names one. Otherwise null. Never invent a name. A team, group or role - "the QA team", "ops", "nobody" - is never an owner: use null.
            Answer once per note, with its number.
            {WriteIn(AiLanguage.English)}
            Notes:
            {numbered}
            """;
    }

    public static JsonObject RetroActionsSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["actions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["note"] = new JsonObject { ["type"] = "integer" },
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["owner"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                    },
                    ["required"] = new JsonArray("note", "title", "owner"),
                },
            },
        },
        ["required"] = new JsonArray("actions"),
    };

    /// <summary>
    /// The facts are gathered by the host and the model only phrases them, so it cannot invent a
    /// task: the prompt says so, and the eval checks every point against the facts it was given.
    /// "Created" is said in so many words for the Someday tasks, because it is the only date there
    /// is - TaskItem has no UpdatedAt and the app has no revision log.
    /// </summary>
    public static string ReviewOverview(ReviewFacts facts)
    {
        static string List(IEnumerable<string> lines) =>
            string.Join("\n", lines.Select(l => $"- {l}")) is { Length: > 0 } text ? text : "- (none)";

        return $"""
            Write 1 to 5 short points for a weekly review of a personal to-do list, one sentence each, plain text without markdown. Leave out a topic the facts say nothing about rather than saying so. Say what got done, which waiting tasks have waited longest and whether to chase them, and which Someday tasks have sat for months and could be dropped or scheduled. Use only the facts below and name tasks by their titles; never mention a task that is not listed.
            {WriteIn(AiLanguage.Danish)}
            Completed in the last 7 days ({facts.CompletedCount}):
            {List(facts.Completed)}
            Waiting, longest first:
            {List(facts.Waiting.Select(w => $"{w.Title} - waiting on {w.WaitingOn ?? "nobody named"} for {w.Days} days"))}
            Overdue: {facts.OverdueCount}
            Someday tasks created more than {ReviewFacts.StaleDays} days ago (the creation date is the only date known):
            {List(facts.StaleSomeday.Select(s => $"{s.Title} - created {s.Days} days ago"))}
            """;
    }

    public static JsonObject ReviewOverviewSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["points"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["maxItems"] = AiSuggestions.MaxReviewPoints,
            },
        },
        ["required"] = new JsonArray("points"),
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
