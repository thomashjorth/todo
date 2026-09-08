using System.Text.RegularExpressions;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The repository's comments were Danish in the oldest code and English in the newest, and nothing
/// enforced either. This is that rule, written down once: comments are English, and the Danish
/// string literals asserting on the app's Danish surface are not comments and are not touched.
///
/// <b>Why this scores words instead of looking for a letter.</b> Three files carry English comments
/// whose subject is Danish collation - <c>JiraTaskSource</c> and the two task-source test classes
/// explain that ae, oe and aa sort after z - so they must spell the letters out. A character test
/// flags all three and would have "fixed" prose that was already correct. Prose is decided by its
/// function words, so the function words are what get counted.
///
/// <b>Why there is a backlog.</b> Twenty-five files were Danish when this was written. Translating
/// them in one commit would be unreviewable, and committing a red test would break
/// <c>Check.cmd</c> for everyone in between. So the list starts full and shrinks: a file on it may
/// be Danish, a file off it may not. <see cref="Every_backlogged_file_is_still_Danish"/> is what
/// stops the list from rotting - remove a file from the backlog in the same commit that translates
/// it, or the suite says so.
/// </summary>
public class SourceLanguageTests
{
    /// <summary>
    /// Files still to translate. Delete an entry in the same commit that translates it. When this
    /// is empty, delete it and <see cref="Every_backlogged_file_is_still_Danish"/> with it.
    /// </summary>
    private static readonly HashSet<string> Backlog = new(StringComparer.Ordinal)
    {
        "src/Todo.Web/src/app/ado/ado-import.html",
        "src/Todo.Web/src/app/ado/ado-import.ts",
        "src/Todo.Web/src/app/app.html",
        "src/Todo.Web/src/app/jira/jira-import.html",
        "src/Todo.Web/src/app/jira/jira-import.ts",
        "src/Todo.Web/src/app/retro/retro-import.ts",
        "src/Todo.Web/src/app/review/review.html",
        "src/Todo.Web/src/app/settings/settings.html",
        "src/Todo.Web/src/app/shortcuts/shortcut-key.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut-key.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut-store.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut.spec.ts",
        "src/Todo.Web/src/app/shortcuts/shortcut.ts",
        "src/Todo.Web/src/app/tasks/task-detail.html",
        "src/Todo.Web/src/app/tasks/task-detail.spec.ts",
        "src/Todo.Web/src/app/tasks/task-detail.ts",
        "src/Todo.Web/src/app/tasks/task-list.html",
        "src/Todo.Web/src/app/tasks/task-list.spec.ts",
        "src/Todo.Web/src/app/tasks/task-list.ts",
        "src/Todo.Web/src/app/tasks/task-row.html",
        "src/Todo.Web/src/app/tasks/task-row.ts",
        "src/Todo.Web/src/app/tasks/task-store.spec.ts",
        "src/Todo.Web/src/index.html",
        "tests/Todo.Api.Tests/LongIdMigrationTests.cs",
        "tests/Todo.E2E/TaskListScreen.cs",
    };

    /// <summary>
    /// Generated code is not ours to write in either language, and the EF migrations are scaffolded.
    /// </summary>
    private static readonly string[] Generated =
    [
        "src/Todo.Web/src/app/api/todo-client.ts",
        "src/Todo.Contracts/Generated/Contracts.g.cs",
    ];

    private static readonly string[] DanishWords =
    [
        "ikke", "fordi", "derfor", "skal", "kun", "hver", "denne", "dette", "disse", "bliver",
        "blev", "altid", "aldrig", "mens", "hvis", "hvad", "hvor", "samme", "andre", "noget",
        "nogen", "uden", "inden", "siden", "mellem", "gennem", "sammen", "selv", "stadig",
        "ellers", "betyder", "frem", "netop", "og", "den", "det", "som", "der", "men", "til",
        "med", "er", "har", "kan", "vi", "man", "så", "også", "altså", "når", "står", "går",
        "ligger", "findes", "sætter", "læser", "skriver", "hedder", "både", "for",
    ];

    private static readonly string[] EnglishWords =
    [
        "the", "and", "is", "are", "this", "that", "these", "those", "because", "only", "must",
        "should", "when", "each", "with", "without", "have", "has", "can", "could", "would",
        "will", "was", "were", "their", "there", "here", "they", "them", "from", "into", "over",
        "under", "about", "after", "before", "while", "which", "what", "where", "why", "how",
        "does", "did", "not", "but", "for", "its", "an", "of", "to", "be", "on", "at", "as",
        "by", "so", "if", "then", "than",
    ];

    private static readonly Regex Danish = WordsIn(DanishWords);
    private static readonly Regex English = WordsIn(EnglishWords);
    private static readonly Regex DanishLetter = new("[æøåÆØÅ]", RegexOptions.Compiled);

    [Fact]
    public async Task No_comment_outside_the_backlog_is_written_in_Danish()
    {
        var offenders = new List<string>();

        foreach (var file in await SourceFilesAsync())
        {
            if (Backlog.Contains(file))
            {
                continue;
            }

            offenders.AddRange(DanishCommentsIn(file)
                .Select(found => $"{file}:{found.Line}  {found.Text}"));
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} comment block(s) are written in Danish. Comments are English in "
                + "this repository; the Danish string literals that assert on the app's Danish "
                + "surface are not comments and are fine. If a comment is English but discusses "
                + "Danish letters, put the letters in `backticks`, \"quotes\" or <c>tags</c>:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Without this the backlog rots: a file translated but left on the list would go on excusing
    /// itself, and the guard would quietly cover less than the list claims.
    /// </summary>
    [Fact]
    public async Task Every_backlogged_file_is_still_Danish()
    {
        var tracked = (await SourceFilesAsync()).ToHashSet(StringComparer.Ordinal);

        var stale = Backlog
            .Select(file => !tracked.Contains(file)
                ? $"{file} is on the backlog but git does not track it"
                : DanishCommentsIn(file).Count == 0
                    ? $"{file} is on the backlog but holds no Danish comment any more"
                    : null)
            .OfType<string>()
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"{stale.Count} backlog entr(ies) are stale. Remove a file from Backlog in the same "
                + "commit that translates it, so the list keeps meaning what it says:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, stale));
    }

    private sealed record Comment(int Line, string Text);

    private static List<Comment> DanishCommentsIn(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, relativePath));

        return CommentsIn(relativePath, text).Where(IsDanish).ToList();
    }

    /// <summary>
    /// Consecutive <c>//</c> lines are one block. A single line is too short to score: "// so far"
    /// is two English words and no Danish ones, and a Danish line can be just as thin.
    /// </summary>
    private static List<Comment> CommentsIn(string path, string text)
    {
        var found = new List<Comment>();

        if (path.EndsWith(".html", StringComparison.Ordinal))
        {
            foreach (Match match in Regex.Matches(text, "<!--(.*?)-->", RegexOptions.Singleline))
            {
                found.Add(new Comment(LineOf(text, match.Index), match.Groups[1].Value));
            }

            return found;
        }

        var code = StripStringLiterals(path, text);
        var lines = code.Split('\n');
        var run = new List<string>();
        var start = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], "(?<!:)//(.*)");

            if (match.Success)
            {
                if (run.Count == 0)
                {
                    start = i + 1;
                }

                run.Add(match.Groups[1].Value);
            }
            else if (run.Count > 0)
            {
                found.Add(new Comment(start, string.Join(' ', run)));
                run.Clear();
            }
        }

        if (run.Count > 0)
        {
            found.Add(new Comment(start, string.Join(' ', run)));
        }

        foreach (Match match in Regex.Matches(code, @"/\*(.*?)\*/", RegexOptions.Singleline))
        {
            found.Add(new Comment(LineOf(code, match.Index), match.Groups[1].Value));
        }

        return found;
    }

    /// <summary>
    /// String literals hold the app's Danish surface - <c>"Opgavestiller: Mette Kirkegaard"</c> and
    /// its kin - and scoring them would make the guard forbid the one thing that has to stay.
    /// </summary>
    private static string StripStringLiterals(string path, string text)
    {
        if (path.EndsWith(".cs", StringComparison.Ordinal))
        {
            text = Regex.Replace(text, "@\"(?:[^\"]|\"\")*\"", "\"\"");

            return Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        }

        text = Regex.Replace(text, "`(?:\\\\.|[^`\\\\])*`", "``");
        text = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");

        return Regex.Replace(text, "'(?:\\\\.|[^'\\\\])*'", "''");
    }

    private static bool IsDanish(Comment comment)
    {
        var body = Unquoted(comment.Text);
        var danish = Danish.Matches(body).Count;
        var english = English.Matches(body).Count;

        if (danish == 0 && !DanishLetter.IsMatch(body))
        {
            return false;
        }

        return danish > english;
    }

    /// <summary>
    /// Quoted text inside a comment is the app's own wording being cited, not prose to judge.
    /// </summary>
    private static string Unquoted(string comment)
    {
        comment = Regex.Replace(comment, "<c>.*?</c>", string.Empty, RegexOptions.Singleline);
        comment = Regex.Replace(comment, "`[^`]*`", string.Empty);

        return Regex.Replace(comment, "\"[^\"]*\"", string.Empty);
    }

    private static int LineOf(string text, int index) =>
        text.Take(index).Count(character => character == '\n') + 1;

    private static Regex WordsIn(string[] words) =>
        new(
            @"\b(" + string.Join('|', words.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task<List<string>> SourceFilesAsync()
    {
        var listing = await ExternalCommand.RunAsync(
            "git", RepoPaths.Root, ["ls-files", "*.cs", "*.ts", "*.html"]);

        Assert.True(
            listing.ExitCode == 0,
            $"git ls-files exited with {listing.ExitCode}. It said:"
                + Environment.NewLine
                + listing.Combined);

        var files = listing.Lines
            .Where(path => path.Length > 0)
            .Where(path => !Generated.Contains(path, StringComparer.Ordinal))
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .ToList();

        // A parsing or working-directory problem answers nothing and would pass both assertions.
        Assert.True(
            files.Count > 100,
            $"git ls-files reported only {files.Count} source file(s); this repo has hundreds.");

        return files;
    }
}
