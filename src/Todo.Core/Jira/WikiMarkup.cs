using System.Text.RegularExpressions;

namespace Todo.Core.Jira;

/// <summary>
/// Converts the subset of Jira's wiki markup that appears in practice into the CommonMark the
/// notes are written in. Self-hosted Jira serves REST v2, where a description is wiki markup —
/// not Cloud's Atlassian Document Format; see the design document's section 10.
///
/// Two rules decide the shape of this class. Code is protected before anything else runs, because
/// a bullet inside a fence is not a bullet. And an unrecognised macro is left alone rather than
/// dropped: showing `{color}` is a visible imperfection, while dropping the text it wraps is an
/// invisible loss.
/// </summary>
public static partial class WikiMarkup
{
    // A sentinel that cannot occur in Jira text and survives the regexes below untouched.
    private const string Fence = "\u0001";

    public static string? ToCommonMark(string? wiki)
    {
        if (string.IsNullOrWhiteSpace(wiki))
        {
            return null;
        }

        var protectedBlocks = new List<string>();
        var text = wiki.Replace("\r\n", "\n").Replace('\r', '\n');

        // Fences before inline code: {{a}} inside a {code} block must not be pulled out
        // separately, and a fence is the outer of the two.
        text = CodeBlock().Replace(text, match => Protect(
            protectedBlocks,
            $"```{match.Groups["lang"].Value}\n{match.Groups["body"].Value.Trim('\n')}\n```"));

        text = InlineCode().Replace(
            text, match => Protect(protectedBlocks, $"`{match.Groups["body"].Value}`"));

        text = Quote().Replace(text, "> ");
        text = Rule().Replace(text, "---");

        // Lists before headings, because the heading rule *writes* a character the ordered-list
        // rule reads: `h1. Overskrift` becomes the line `# Overskrift`, and `^#[ \t]+` then takes
        // that for a Jira ordered item and rewrites it to `1. Overskrift`. Measured — with the
        // rules the other way round, only `h1` is wrong, because `h2.` and up put a second `#`
        // where the list rule needs a space. One heading level out of six, which is exactly the
        // kind of collision a single example would have missed.
        text = NumberedItem().Replace(text, "1. ");
        text = BulletItem().Replace(text, "- ");

        text = Heading().Replace(text, match =>
            new string('#', int.Parse(match.Groups["level"].ValueSpan))
                + " "
                + match.Groups["text"].Value);

        text = NamedLink().Replace(text, "[${text}](${url})");
        text = BareLink().Replace(text, "<${url}>");

        // Bold before emphasis: `*x*` must become `**x**` before `_x_` becomes `*x*`, or the
        // second rule would see the asterisks the first one just wrote.
        text = Bold().Replace(text, "**${text}**");
        text = Emphasis().Replace(text, "*${text}*");

        // Backwards, so a placeholder nested inside a restored block is still replaced.
        for (var i = protectedBlocks.Count - 1; i >= 0; i--)
        {
            text = text.Replace($"{Fence}{i}{Fence}", protectedBlocks[i]);
        }

        return text;
    }

    private static string Protect(List<string> blocks, string value)
    {
        blocks.Add(value);

        return $"{Fence}{blocks.Count - 1}{Fence}";
    }

    [GeneratedRegex(@"\{code(?::(?<lang>[^}]*))?\}(?<body>.*?)\{code\}", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\{\{(?<body>.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^h(?<level>[1-6])\.[ \t]*(?<text>.*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^bq\.[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^-{4,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    [GeneratedRegex(@"^#[ \t]+", RegexOptions.Multiline)]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"^\*[ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletItem();

    [GeneratedRegex(@"\[(?<text>[^\]|]+)\|(?<url>[^\]]+)\]")]
    private static partial Regex NamedLink();

    [GeneratedRegex(@"\[(?<url>(?:https?|mailto):[^\]|]+)\]")]
    private static partial Regex BareLink();

    // Anchored on non-space so `a * b` is not read as an unterminated bold run.
    [GeneratedRegex(@"(?<![\w*])\*(?<text>[^*\n]*[^\s*])\*(?![\w*])")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w_])_(?<text>[^_\n]*[^\s_])_(?![\w_])")]
    private static partial Regex Emphasis();
}
