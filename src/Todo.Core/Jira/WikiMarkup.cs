using System.Text.RegularExpressions;

namespace Todo.Core.Jira;

/// <summary>
/// Converts the subset of Jira's wiki markup that appears in practice into the CommonMark the
/// notes are written in. Self-hosted Jira serves REST v2, where a description is wiki markup —
/// not Cloud's Atlassian Document Format; see the design document's section 10.
///
/// Three rules decide the shape of this class. Verbatim blocks are protected before anything else
/// runs, because a bullet inside a fence is not a bullet. An unrecognised macro is left alone
/// rather than dropped: showing `{color}` is a visible imperfection, while dropping the text it
/// wraps is an invisible loss. And a construct this class does not convert must still not end up
/// *reinterpreted* — passing text through is only safe while the renderer reads it as the thing
/// Jira meant. Both bugs found in review were of that third kind, not of the first two.
///
/// This does not sanitise. The sanitising happens where the rendered result is bound with
/// [innerHTML]; a Jira description is text someone else wrote.
///
/// Known limitations, measured against the app's own marked with `gfm` and `breaks` enabled and
/// left standing deliberately rather than by accident:
/// <list type="bullet">
/// <item>`~x~` is subscript in Jira and strikethrough in GFM, so its meaning inverts. Rare in
/// practice, and a fix carries edge cases of its own.</item>
/// <item>Tables (`||h||h||` and `|c|c|`) pass through as raw text. That is misread-safe rather
/// than correct: GFM needs a `|---|` delimiter row, which Jira never emits, so the result is
/// visible pipe soup instead of a wrong table. Worth converting one day.</item>
/// <item>`-x-` strikethrough passes through as raw text, and that is safe both mid-line and at
/// the start of a line.</item>
/// <item>A code body that itself contains a triple backtick closes its fence early.</item>
/// <item>`NamedLink` has no scheme allowlist where `BareLink` does. Not a hole — see the note on
/// sanitising above — but the asymmetry is known rather than overlooked.</item>
/// </list>
/// </summary>
public static partial class WikiMarkup
{
    // Stands in for a verbatim block while the line rules run. Not named "fence": this class
    // emits markdown fences a few lines below, and that is a different idea.
    private const string Sentinel = "\u0001";

    public static string? ToCommonMark(string? wiki)
    {
        if (string.IsNullOrWhiteSpace(wiki))
        {
            return null;
        }

        var protectedBlocks = new List<string>();
        var text = wiki.Replace("\r\n", "\n").Replace('\r', '\n');

        // This is what makes the sentinel's claim true instead of hopeful. A forged placeholder in
        // the incoming description would be restored along with the real ones and emit a protected
        // block twice, because String.Replace is global.
        text = text.Replace(Sentinel, string.Empty);

        // Both verbatim blocks before inline code: a {{a}} inside a {code} or {noformat} body must
        // not be pulled out on its own, because the block is the outer of the two.
        text = CodeBlock().Replace(text, match => Protect(
            protectedBlocks,
            $"```{match.Groups["lang"].Value}\n{match.Groups["body"].Value.Trim('\n')}\n```"));

        text = NoFormat().Replace(text, match => Protect(
            protectedBlocks, $"```\n{match.Groups["body"].Value.Trim('\n')}\n```"));

        text = InlineCode().Replace(
            text, match => Protect(protectedBlocks, $"`{match.Groups["body"].Value}`"));

        text = Quote().Replace(text, "> ");

        // `***`, not `---`. Jira does not require a blank line before `----`, and `---` directly
        // under a line of text is a setext H2 in CommonMark: the rule itself disappears and the
        // paragraph above it is promoted to a heading. `***` cannot be read that way, and it also
        // survives Bold() untouched, because that rule's text group cannot begin on an asterisk.
        text = Rule().Replace(text, "***");

        // Lists before headings, because the heading rule *writes* the characters the list rule
        // reads: `h2. Mindre` becomes the line `## Mindre`, which ListItem() then takes for a
        // nested Jira ordered item. Measured in both orders — with headings first, every level
        // from h1 to h6 comes out as a list item.
        text = ListItem().Replace(text, match =>
        {
            var marks = match.Groups["marks"].Value;

            // Jira spells depth by repeating the marker, and mixes the two freely, so the last
            // character decides the kind and the count decides the indent. Four spaces per level,
            // because two do not nest under a `1. ` marker — marked flattens them.
            return new string(' ', (marks.Length - 1) * 4) + (marks[^1] == '#' ? "1. " : "- ");
        });

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
            text = text.Replace($"{Sentinel}{i}{Sentinel}", protectedBlocks[i]);
        }

        return text;
    }

    private static string Protect(List<string> blocks, string value)
    {
        blocks.Add(value);

        return $"{Sentinel}{blocks.Count - 1}{Sentinel}";
    }

    // NonBacktracking on the three lazy rules below. None of them backtracks catastrophically, but
    // all three are quadratic while unterminated, and a note has no maxLength on the contract — the
    // only bound on a description is Jira's own convention. Measured on an input of nothing but
    // `{{`: 27 ms at 25 KB, 93 at 50, 288 at 100, 1041 at 200, so a clean fourfold per doubling.
    // With the option, 100 KB is under a millisecond. Slow rather than dangerous, then, and this is
    // insurance bought cheaply — it is not luck that the option is available, because
    // NonBacktracking forbids lookaround and only Bold and Emphasis use it. The regex source
    // generator cannot emit a custom matcher for these three and falls back to a cached Regex,
    // which it records in the generated remarks without raising any diagnostic.
    [GeneratedRegex(
        @"\{code(?::(?<lang>[^}]*))?\}(?<body>.*?)\{code\}",
        RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(
        @"\{noformat(?::[^}]*)?\}(?<body>.*?)\{noformat\}",
        RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex NoFormat();

    [GeneratedRegex(
        @"\{\{(?<body>.*?)\}\}", RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^h(?<level>[1-6])\.[ \t]*(?<text>.*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^bq\.[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^-{4,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    // One rule for both list kinds, because Jira nests by repeating the marker and allows the two
    // to be mixed. Matching a single marker read `## en-a` as a heading and split the list.
    [GeneratedRegex(@"^(?<marks>[#*]+)[ \t]+", RegexOptions.Multiline)]
    private static partial Regex ListItem();

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
