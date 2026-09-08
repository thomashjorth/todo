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
/// <b>The letter is a tie-breaker, not an absent signal.</b> It used to be neither. The classifier
/// consulted <c>DanishLetter</c> only when the Danish word count was zero, and a zero count can
/// never beat an English one, so the regex could not change a single verdict - it was dead code
/// dressed as a second opinion. It now decides ties: <c>danish >= english</c> when the letter is
/// there, <c>danish > english</c> when it is not. That closes the largest remaining false
/// negative, short Danish technical prose whose only function words are the ones both languages
/// share - "Raekkefoelgen afgoer resultatet." scores nothing on either list. It is safe for the
/// three collation comments for the reason the paragraph above gives: they are English prose about
/// a Danish letter, so their English count is the higher one and no tie arises.
///
/// The branch is worth exactly two blocks today, and they are named rather than counted because a
/// count here rots on the next translation: <c>task-detail.ts</c> line 144, which ties at one word
/// each because "At" is on the English list, and <c>jira-import.html</c> line 251, which scores
/// nothing on either side. Both are Danish, both were already on the backlog, and neither adds a
/// file. If you find the delivery plan claiming four, that number was measured while <c>for</c>
/// had been taken out of the Danish list but not yet out of the English one - which tips two more
/// blocks from a win into a tie without changing any verdict. Same 162 blocks in the same 27 files
/// either way; only the attribution moves.
///
/// <b>Why there is a backlog.</b> Twenty-seven files were Danish when this was written. Translating
/// them in one commit would be unreviewable, and committing a red test would break
/// <c>Check.cmd</c> for everyone in between. So the list starts full and shrinks: a file on it may
/// be Danish, a file off it may not. <see cref="Every_backlogged_file_is_still_Danish"/> is what
/// stops the list from rotting - remove a file from the backlog in the same commit that translates
/// it, or the suite says so.
/// </summary>
public class SourceLanguageTests
{
    /// <summary>
    /// Files still to translate. Delete an entry in the same commit that translates it.
    ///
    /// When this is empty, delete it and <see cref="Every_backlogged_file_is_still_Danish"/> with
    /// it - but leave the per-extension floors in
    /// <see cref="No_comment_outside_the_backlog_is_written_in_Danish"/> standing. They are what
    /// has to survive the list. Until then it is this list that forces the extractor to produce
    /// something: every entry on it has to hold Danish, so a walk that found nothing turns the
    /// second fact red. Once the list goes, only the floors are left asking whether anything was
    /// read at all, and without them the last translation would leave behind a single assertion
    /// that cannot fail - a pathspec matching nothing would offer nothing to score and the guard
    /// would report a clean repository.
    /// </summary>
    private static readonly string[] BacklogEntries =
    [
        "tests/Todo.Api.Tests/LongIdMigrationTests.cs",
        "tests/Todo.E2E/TaskListScreen.cs",
    ];

    private static readonly HashSet<string> Backlog = new(BacklogEntries, StringComparer.Ordinal);

    /// <summary>
    /// Generated code is not ours to write in either language, and the EF migrations are scaffolded.
    /// </summary>
    private static readonly string[] Generated =
    [
        "src/Todo.Web/src/app/api/todo-client.ts",
        "src/Todo.Contracts/Generated/Contracts.g.cs",
    ];

    /// <summary>
    /// Comment blocks the extractor has to find per extension before a green run means anything.
    /// <c>git ls-files</c> answering is not the same as its pathspecs matching, and a walk that
    /// reads nothing offends nobody. The floors sit well under the measured counts - 1469 blocks
    /// in the C#, 542 in the TypeScript and 86 in the templates on 2026-09-08, over 224 files - so
    /// ordinary editing cannot trip them while a pathspec that stops matching does. Seen to fail
    /// on the mistake it is for: <c>*.htmlx</c> in place of <c>*.html</c> leaves the templates at
    /// zero blocks and turns this red, while the file count stays at 212 and clears the
    /// hundred-file check below without a murmur. Raise them if the repository grows a lot; never
    /// lower one to make a red run green.
    /// </summary>
    private static readonly (string Extension, int Floor)[] Floors =
    [
        (".cs", 1000),
        (".ts", 300),
        (".html", 50),
    ];

    /// <summary>
    /// Function words only: content words drift across the two languages and would score for
    /// whichever list happened to name them.
    ///
    /// <c>for</c> appears in neither list although it is a real word in both, and it was the only
    /// word the two shared. Every occurrence added one to each side, which moves the difference
    /// the classifier reads by nothing while diluting the ratio it reads it from.
    /// </summary>
    private static readonly string[] DanishWords =
    [
        "ikke", "fordi", "derfor", "skal", "kun", "hver", "denne", "dette", "disse", "bliver",
        "blev", "altid", "aldrig", "mens", "hvis", "hvad", "hvor", "samme", "andre", "noget",
        "nogen", "uden", "inden", "siden", "mellem", "gennem", "sammen", "selv", "stadig",
        "ellers", "betyder", "frem", "netop", "og", "den", "det", "som", "der", "men", "til",
        "med", "er", "har", "kan", "vi", "man", "så", "også", "altså", "når", "står", "går",
        "ligger", "findes", "sætter", "læser", "skriver", "hedder", "både",
    ];

    private static readonly string[] EnglishWords =
    [
        "the", "and", "is", "are", "this", "that", "these", "those", "because", "only", "must",
        "should", "when", "each", "with", "without", "have", "has", "can", "could", "would",
        "will", "was", "were", "their", "there", "here", "they", "them", "from", "into", "over",
        "under", "about", "after", "before", "while", "which", "what", "where", "why", "how",
        "does", "did", "not", "but", "its", "an", "of", "to", "be", "on", "at", "as",
        "by", "so", "if", "then", "than",
    ];

    private static readonly Regex Danish = WordsIn(DanishWords);
    private static readonly Regex English = WordsIn(EnglishWords);
    private static readonly Regex DanishLetter = new("[æøåÆØÅ]", RegexOptions.Compiled);

    [Fact]
    public async Task No_comment_outside_the_backlog_is_written_in_Danish()
    {
        var offenders = new List<string>();
        var blocks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in (await SourceFilesAsync()).Scanned)
        {
            // Extracted for every file and scored only for the ones off the backlog: the floors
            // below have to go on proving the walk works after the backlog is gone.
            var comments = CommentsIn(file, ReadSource(file));
            var extension = Path.GetExtension(file);

            blocks[extension] = blocks.GetValueOrDefault(extension) + comments.Count;

            if (Backlog.Contains(file))
            {
                continue;
            }

            offenders.AddRange(comments
                .Where(IsDanish)
                .Select(found => $"{file}:{found.Line}  {found.Text}"));
        }

        foreach (var (extension, floor) in Floors)
        {
            var found = blocks.GetValueOrDefault(extension);

            Assert.True(
                found >= floor,
                $"The walk found only {found} comment block(s) in the {extension} files, under the "
                    + $"floor of {floor}. Read that as a listing or parsing fault rather than as a "
                    + "sparsely commented repository: check the pathspecs in SourceFilesAsync and "
                    + "the walk in CommentsIn before touching this number.");
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
        var listing = await SourceFilesAsync();
        var tracked = listing.Tracked.ToHashSet(StringComparer.Ordinal);
        var scanned = listing.Scanned.ToHashSet(StringComparer.Ordinal);

        // A HashSet collection initializer throws Add's result away, so a repeated path used to
        // compile in silence and quietly make the list shorter than it reads.
        var repeats = BacklogEntries
            .GroupBy(entry => entry, StringComparer.Ordinal)
            .Where(entries => entries.Count() > 1)
            .Select(entries => $"{entries.Key} (listed {entries.Count()} times)")
            .ToList();

        Assert.True(
            repeats.Count == 0,
            $"Backlog lists {BacklogEntries.Length} entries of which only {Backlog.Count} are "
                + "distinct, so it covers less than it appears to:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, repeats));

        var stale = BacklogEntries
            .Select(file => !scanned.Contains(file)
                ? tracked.Contains(file)
                    ? $"{file} is on the backlog but SourceFilesAsync filters it out as generated "
                        + "or scaffolded, so nothing here ever reads it"
                    : $"{file} is on the backlog but git does not track it"
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

    private static List<Comment> DanishCommentsIn(string relativePath) =>
        CommentsIn(relativePath, ReadSource(relativePath)).Where(IsDanish).ToList();

    /// <summary>
    /// The file git listed, read from disk. Named rather than thrown: a tracked file that is not
    /// on disk is a deleted or half-reverted working copy, and <c>File.ReadAllText</c> would
    /// answer that with a bare <c>FileNotFoundException</c> raised somewhere inside a LINQ chain -
    /// the same reason <see cref="FrontendStrictnessTests"/> names a missing <c>tsc.cmd</c> instead
    /// of letting a <c>Win32Exception</c> out.
    /// </summary>
    private static string ReadSource(string relativePath)
    {
        var file = Path.Combine(RepoPaths.Root, relativePath);

        Assert.True(
            File.Exists(file),
            $"git tracks {relativePath}, but there is no file at {file}. That is a deleted or "
                + "partly reverted working copy rather than a comment in the wrong language: "
                + $"restore it with `git checkout -- {relativePath}`, or commit the deletion so "
                + "git stops listing it.");

        return File.ReadAllText(file);
    }

    /// <summary>
    /// Every comment in one file, in source order, with the line it starts on.
    ///
    /// <b>One walk, not two passes.</b> This was a regex that blanked the string literals followed
    /// by a second regex that looked for comments in what was left, and the two desynchronised on
    /// the first apostrophe inside a comment. Strings and comments are mutually exclusive, so the
    /// <c>'</c> in <c>URL'en</c> opened a phantom literal that ran on to the next quote in real
    /// code and deleted every line in between - and it leaked the Danish fixture strings it had
    /// been written to hide into the comment bodies on the way. Measured before the fix: 46 of the
    /// 212 scanned C# and TypeScript files carried such a phantom, together swallowing 4,927 lines,
    /// and the main assertion passed over three unmistakably Danish blocks in two files that were
    /// not on the backlog. The rule that kills it is that a <c>'</c> or <c>"</c> literal cannot
    /// cross a newline in either language, so a run reaching one ends there. Correct line numbers
    /// come out of the same walk, which is the other thing two passes could not do: the old numbers
    /// were counted in the blanked text, where the phantoms had already removed lines.
    ///
    /// Consecutive <c>//</c> lines are one block; a block comment and an HTML comment stand alone.
    /// A single line is too short to score: "// so far" is two English words and no Danish ones,
    /// and a Danish line can be just as thin.
    ///
    /// <b>What this does not see.</b> A template's <c>script</c> and <c>style</c> blocks are not
    /// scanned, because the markup path only looks for HTML comments. No tracked template has one
    /// today; whoever first puts inline script in <c>index.html</c> should know its comments are
    /// unguarded. And the holes in a C# interpolated string are read as ordinary string content
    /// rather than as code, so a quote inside <c>{...}</c> can mis-pair - bounded to that one line
    /// by the newline rule, and no such line in the repository carries a comment.
    /// </summary>
    private static List<Comment> CommentsIn(string path, string text) =>
        path.EndsWith(".html", StringComparison.Ordinal)
            ? MarkupCommentsIn(text)
            : CodeCommentsIn(text, path.EndsWith(".cs", StringComparison.Ordinal));

    /// <summary>
    /// Templates keep a regex: an Angular template holds no string literal a comment could hide
    /// inside, so there is nothing here for two passes to disagree about. The cursor walks forward
    /// with the matches so the line number is still counted once rather than per match.
    /// </summary>
    private static List<Comment> MarkupCommentsIn(string text)
    {
        var found = new List<Comment>();
        var line = 1;
        var read = 0;

        foreach (Match match in Regex.Matches(text, "<!--(.*?)-->", RegexOptions.Singleline))
        {
            line += Newlines(text, read, match.Index);
            read = match.Index;

            found.Add(new Comment(line, match.Groups[1].Value));
        }

        return found;
    }

    /// <summary>
    /// The single left-to-right walk. A comment can only open at <c>//</c> or <c>/*</c>, so those
    /// are tested first and everything after them belongs to the comment - quotes included. Only
    /// when neither opens does a string literal get its chance, which is what keeps the two kinds
    /// mutually exclusive.
    /// </summary>
    private static List<Comment> CodeCommentsIn(string text, bool csharp)
    {
        var found = new List<Comment>();
        var run = new List<string>();
        var runStart = 0;
        var runEnd = 0;
        var line = 1;
        var index = 0;

        void CloseRun()
        {
            if (run.Count > 0)
            {
                found.Add(new Comment(runStart, string.Join(' ', run)));
                run.Clear();
            }
        }

        while (index < text.Length)
        {
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (text[index] == '/' && next == '/')
            {
                var body = index + 2;
                var stop = body;

                while (stop < text.Length && text[stop] != '\n')
                {
                    stop++;
                }

                if (run.Count > 0 && line != runEnd + 1)
                {
                    CloseRun();
                }

                if (run.Count == 0)
                {
                    runStart = line;
                }

                // TrimEnd('\r') because this is a CRLF working copy: joining the lines with a
                // space would otherwise leave bare carriage returns inside the failure message,
                // and a console renders one of those by overwriting the line it just printed.
                run.Add(text[body..stop].TrimEnd('\r'));
                runEnd = line;

                // Stops on the newline rather than past it, so the loop below counts the line.
                index = stop;

                continue;
            }

            if (text[index] == '/' && next == '*')
            {
                CloseRun();

                var body = index + 2;
                var close = text.IndexOf("*/", body, StringComparison.Ordinal);
                var stop = close < 0 ? text.Length : close;
                var past = close < 0 ? text.Length : close + 2;

                found.Add(new Comment(line, text[body..stop]));

                line += Newlines(text, index, past);
                index = past;

                continue;
            }

            var literal = EndOfLiteral(text, index, csharp);

            if (literal > index)
            {
                line += Newlines(text, index, literal);
                index = literal;

                continue;
            }

            if (text[index] == '\n')
            {
                line++;
            }

            index++;
        }

        CloseRun();

        return found;
    }

    /// <summary>
    /// The index just past the string literal starting at <paramref name="start"/>, or
    /// <paramref name="start"/> itself when no literal starts there.
    ///
    /// The forms allowed to cross a newline are the ones that really can: a TypeScript backtick
    /// template, and C#'s <c>@"..."</c> and <c>"""..."""</c>. Everything else - <c>'...'</c> and
    /// <c>"..."</c> in both languages - stops at the first newline it meets and is left
    /// unterminated. That is the whole fix: a stray apostrophe costs the rest of its own line
    /// rather than the next fifteen.
    /// </summary>
    private static int EndOfLiteral(string text, int start, bool csharp)
    {
        var open = start;
        var verbatim = false;

        if (csharp)
        {
            // @ makes a literal verbatim, $ only makes it interpolated, and both may prefix it.
            // An @ that is not followed by a quote is a verbatim identifier such as @class.
            while (open < text.Length && (text[open] == '@' || text[open] == '$'))
            {
                verbatim |= text[open] == '@';
                open++;
            }

            if (open > start && (open == text.Length || text[open] != '"'))
            {
                return start;
            }
        }

        if (open == text.Length)
        {
            return start;
        }

        var quote = text[open];

        // The backtick is TypeScript's alone; in C# it is not a delimiter at all.
        if (quote != '"' && quote != '\'' && (csharp || quote != '`'))
        {
            return start;
        }

        if (verbatim)
        {
            return EndOfVerbatim(text, open);
        }

        if (csharp && quote == '"')
        {
            var fence = open;

            while (fence < text.Length && text[fence] == '"')
            {
                fence++;
            }

            // Three or more quotes open a raw string, closed by a fence of the same length. Two
            // are an empty literal, which the escaped form below already ends in the right place.
            if (fence - open >= 3)
            {
                var delimiter = new string('"', fence - open);
                var close = text.IndexOf(delimiter, fence, StringComparison.Ordinal);

                return close < 0 ? text.Length : close + delimiter.Length;
            }
        }

        return EndOfEscaped(text, open, quote);
    }

    /// <summary>
    /// C#'s <c>@"..."</c>: it spans lines, a backslash is an ordinary character, and a doubled
    /// quote is one quote rather than the end.
    /// </summary>
    private static int EndOfVerbatim(string text, int open)
    {
        for (var at = open + 1; at < text.Length; at++)
        {
            if (text[at] != '"')
            {
                continue;
            }

            if (at + 1 < text.Length && text[at + 1] == '"')
            {
                at++;

                continue;
            }

            return at + 1;
        }

        return text.Length;
    }

    /// <summary>
    /// A literal delimited by a single <paramref name="quote"/> with backslash escapes, which
    /// covers <c>'...'</c> and <c>"..."</c> in both languages and TypeScript's backtick template.
    /// Only the template may cross a line.
    /// </summary>
    private static int EndOfEscaped(string text, int open, char quote)
    {
        var spansLines = quote == '`';

        for (var at = open + 1; at < text.Length; at++)
        {
            if (text[at] == '\\')
            {
                // Takes the escaped character with it, so a `\\` pair cannot end the literal.
                at++;

                continue;
            }

            if (text[at] == quote)
            {
                return at + 1;
            }

            if (!spansLines && text[at] == '\n')
            {
                return at;
            }
        }

        return text.Length;
    }

    private static int Newlines(string text, int from, int to)
    {
        var count = 0;

        for (var at = from; at < to; at++)
        {
            if (text[at] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsDanish(Comment comment)
    {
        var body = Unquoted(comment.Text);
        var danish = Danish.Matches(body).Count;
        var english = English.Matches(body).Count;

        return danish > english || (DanishLetter.IsMatch(body) && danish >= english);
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

    private static Regex WordsIn(string[] words) =>
        new(
            @"\b(" + string.Join('|', words.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// What git tracks, and the part of it this guard reads. Both, because a backlog entry git
    /// tracks but the filters remove is a different mistake from one git has never heard of, and a
    /// single list cannot tell a reader which of the two happened.
    /// </summary>
    private sealed record SourceListing(List<string> Tracked, List<string> Scanned);

    private static async Task<SourceListing> SourceFilesAsync()
    {
        var listing = await ExternalCommand.RunAsync(
            "git", RepoPaths.Root, ["ls-files", "*.cs", "*.ts", "*.html"]);

        Assert.True(
            listing.ExitCode == 0,
            $"git ls-files exited with {listing.ExitCode}. It said:"
                + Environment.NewLine
                + listing.Combined);

        var tracked = listing.Lines.Where(path => path.Length > 0).ToList();

        var scanned = tracked
            .Where(path => !Generated.Contains(path, StringComparer.Ordinal))
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .ToList();

        // A wrong working directory, caught early and cheaply. It proves git ran rather than that
        // the pathspecs matched - the .cs files alone clear it - so the assertion with teeth is
        // the per-extension floor over what the walk actually read.
        Assert.True(
            scanned.Count > 100,
            $"git ls-files reported only {scanned.Count} source file(s); this repo has hundreds.");

        return new SourceListing(tracked, scanned);
    }
}
