using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// The same silent failure has been recorded through three different tools: <c>sed -i</c>, the
/// Write tool and a naive Python rewrite all write LF into this CRLF working copy, and
/// <c>git diff</c> then shows <b>nothing</b>, because <c>core.autocrlf</c> normalises on the way
/// in. The file on disk really did change; Git says it did not. And the verification that was
/// written against it — <c>grep -cv $'\r$'</c> — cannot fail: Git Bash's grep reads in text mode
/// and has stripped the CRs before the pattern is tried, so "no LF-only lines" is true whatever
/// the file holds. Every delivery since has hand-verified with <c>tr</c> instead.
///
/// This class is that verification, done once. It is built on <c>git ls-files --eol</c>, which
/// reports the index (<c>i/</c>) and the working tree (<c>w/</c>) as <b>Git</b> computed them —
/// the one source no text mode can fool.
///
/// What it can and cannot catch is worth being exact about. Every text file in this repo is
/// <c>i/lf</c> and always will be: <c>.gitattributes</c> says <c>* text=auto</c>, so Git
/// normalises on the way into the index and CRLF cannot be committed. The drift is therefore
/// <b>working-tree only</b> — which is also why a fresh checkout, CI included, can never fail
/// these two assertions, and why the fix is <c>git checkout -- .</c> rather than a commit. That is
/// not a weakness: the working tree is where the tools write, and a developer's dirty copy is the
/// only place the bug has ever appeared.
/// </summary>
public class LineEndingTests
{
    /// <summary>
    /// Read from the index rather than from HEAD, because that is what <c>git ls-files</c> reports
    /// and what a checkout writes.
    /// </summary>
    private const string IndexPrefix = ":";

    [Fact]
    public async Task No_file_mixes_line_endings()
    {
        var files = await ListFilesAsync();

        var mixed = files
            .Where(file => file.IndexEol == "mixed" || file.WorktreeEol == "mixed")
            .Select(file => $"{file.Path} (index {file.IndexEol}, working tree {file.WorktreeEol})")
            .ToList();

        Assert.True(
            mixed.Count == 0,
            $"{mixed.Count} file(s) mix CRLF and LF line endings. That is wrong on every platform "
                + "and under every configuration — a tool wrote part of the file. Fix with "
                + "`git checkout -- <path>`:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, mixed));
    }

    /// <summary>
    /// The expectation is not hard-coded and not derived from <c>core.autocrlf</c> either. It is
    /// asked of Git: <c>git cat-file --filters</c> runs the index blob through the same smudge
    /// filter a checkout uses, so its output <i>is</i> what Git would write to disk. One probe per
    /// distinct attribute set, because the attributes are what decide — <c>*.cmd text eol=crlf</c>
    /// pins those two files to CRLF even on a machine where everything else is LF, and a rule
    /// written from <c>core.autocrlf</c> alone would call them wrong.
    /// </summary>
    [Fact]
    public async Task Every_text_file_matches_what_git_would_check_out()
    {
        var files = await ListFilesAsync();

        // Skipped on purpose. "-text" is Git's answer for a binary file — favicon.ico — where the
        // question does not apply. "none" is a file with no line ending at all, either empty
        // (.gitkeep) or a single line without a trailing newline (.source-hash): it cannot be
        // wrong in either direction, because there is nothing to be wrong.
        var text = files
            .Where(file => file.WorktreeEol is "lf" or "crlf")
            .ToList();

        Assert.True(
            text.Count > 100,
            $"git ls-files --eol reported only {text.Count} text file(s). This repo has hundreds, "
                + "so that is a parsing or working-directory problem rather than a clean result.");

        var wrong = new List<string>();

        foreach (var group in text.GroupBy(file => file.Attributes, StringComparer.Ordinal))
        {
            var expected = await AskGitWhatItWouldWriteAsync(group);

            wrong.AddRange(group
                .Where(file => file.WorktreeEol != expected)
                .Select(file =>
                    $"{file.Path} is {file.WorktreeEol} in the working tree, but git "
                        + $"{group.Key} would write {expected}"));
        }

        Assert.True(
            wrong.Count == 0,
            $"{wrong.Count} file(s) have the wrong line endings on disk. `git diff` will show "
                + "nothing for them, because Git normalises on the way in — so a change you think "
                + "you rolled back may still be there. Fix with `git checkout -- <path>`:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// Walks the group until a probe carries a line ending: a single-line file would answer
    /// nothing, and silently skipping the group would let the assertion pass on nothing.
    /// </summary>
    private static async Task<string> AskGitWhatItWouldWriteAsync(IEnumerable<TrackedFile> group)
    {
        foreach (var file in group)
        {
            var blob = await GitAsync("cat-file", "--filters", IndexPrefix + file.Path);

            var newline = blob.StandardOutput.IndexOf('\n');

            if (newline < 0)
            {
                continue;
            }

            return newline > 0 && blob.StandardOutput[newline - 1] == '\r' ? "crlf" : "lf";
        }

        throw new InvalidOperationException(
            "Not one file that git reports as text came back from `git cat-file --filters` with a "
                + "line ending, so there was nothing to compare against. Attribute set: "
                + group.First().Attributes);
    }

    private static async Task<List<TrackedFile>> ListFilesAsync()
    {
        var listing = await GitAsync("ls-files", "--eol");

        var files = listing.Lines.Select(TrackedFile.Parse).OfType<TrackedFile>().ToList();

        Assert.True(
            files.Count > 100,
            "git ls-files --eol did not produce a readable listing. It said:"
                + Environment.NewLine
                + listing.Combined);

        return files;
    }

    private static async Task<ExternalCommandResult> GitAsync(params string[] arguments)
    {
        // The working directory is the repo root, not the test's output directory: git resolves
        // everything from where it is standing, and bin\Debug is inside the same repo only by luck.
        var result = await ExternalCommand.RunAsync("git", RepoPaths.Root, arguments);

        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} exited with {result.ExitCode}. Git has to be on "
                + $"PATH and {RepoPaths.Root} has to be a working copy. It said:"
                + Environment.NewLine
                + result.Combined);

        return result;
    }
}
