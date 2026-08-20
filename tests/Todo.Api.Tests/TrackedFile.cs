namespace Todo.Api.Tests;

/// <summary>
/// One line of <c>git ls-files --eol</c>: <c>i/lf&#160;w/crlf&#160;attr/text=auto&#160;&lt;TAB&gt;path</c>.
/// </summary>
/// <param name="IndexEol">The <c>i/</c> column: the line endings inside the index blob.</param>
/// <param name="WorktreeEol">The <c>w/</c> column: the line endings of the file on disk.</param>
/// <param name="Attributes">The <c>attr/</c> column, verbatim — it decides what a checkout writes.</param>
/// <param name="Path">The repo-relative path, with forward slashes as git prints it.</param>
public sealed record TrackedFile(string IndexEol, string WorktreeEol, string Attributes, string Path)
{
    /// <summary>
    /// Null for a line that does not parse, so a future git version printing something else fails
    /// the count assertion in <see cref="LineEndingTests"/> rather than throwing here.
    /// </summary>
    public static TrackedFile? Parse(string line)
    {
        // The path is separated by a tab, so a path containing spaces survives. The three columns
        // before it are space-padded to a fixed width.
        var tab = line.IndexOf('\t');

        if (tab < 0)
        {
            return null;
        }

        var columns = line[..tab]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (columns.Length < 3
            || !columns[0].StartsWith("i/", StringComparison.Ordinal)
            || !columns[1].StartsWith("w/", StringComparison.Ordinal))
        {
            return null;
        }

        return new TrackedFile(
            columns[0]["i/".Length..],
            columns[1]["w/".Length..],
            // attr/ can hold several attributes separated by spaces, so it is joined back up.
            string.Join(' ', columns.Skip(2)),
            line[(tab + 1)..]);
    }
}
