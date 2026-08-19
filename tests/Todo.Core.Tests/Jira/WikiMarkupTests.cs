using Todo.Core.Jira;

namespace Todo.Core.Tests.Jira;

public class WikiMarkupTests
{
    /// <summary>
    /// The reason this converter exists at all. Jira's `*x*` is bold; CommonMark's is emphasis.
    /// A passthrough silently demotes every bold word in every imported description, and nothing
    /// downstream can tell. This is the assertion that fails if someone "simplifies" the converter
    /// into a no-op.
    /// </summary>
    [Fact]
    public void Bold_stays_bold_rather_than_becoming_emphasis()
    {
        Assert.Equal("**vigtigt**", WikiMarkup.ToCommonMark("*vigtigt*"));
    }

    [Fact]
    public void Emphasis_uses_the_markdown_spelling()
    {
        Assert.Equal("*måske*", WikiMarkup.ToCommonMark("_måske_"));
    }

    [Theory]
    [InlineData("h1. Overskrift", "# Overskrift")]
    [InlineData("h3. Mindre", "### Mindre")]
    [InlineData("h6. Mindst", "###### Mindst")]
    public void A_heading_becomes_hashes(string wiki, string expected)
    {
        Assert.Equal(expected, WikiMarkup.ToCommonMark(wiki));
    }

    [Fact]
    public void Inline_code_becomes_backticks()
    {
        Assert.Equal("Kald `Foo()`", WikiMarkup.ToCommonMark("Kald {{Foo()}}"));
    }

    [Fact]
    public void A_code_block_becomes_a_fence()
    {
        Assert.Equal(
            "```java\nint x = 1;\n```",
            WikiMarkup.ToCommonMark("{code:java}\nint x = 1;\n{code}"));
    }

    [Fact]
    public void A_code_block_without_a_language_still_fences()
    {
        Assert.Equal("```\nrå\n```", WikiMarkup.ToCommonMark("{code}\nrå\n{code}"));
    }

    [Fact]
    public void A_named_link_becomes_a_markdown_link()
    {
        Assert.Equal(
            "[boardet](https://example.test/b)",
            WikiMarkup.ToCommonMark("[boardet|https://example.test/b]"));
    }

    [Fact]
    public void A_bare_link_becomes_an_autolink()
    {
        Assert.Equal("<https://example.test/b>", WikiMarkup.ToCommonMark("[https://example.test/b]"));
    }

    [Fact]
    public void A_numbered_list_becomes_an_ordered_list()
    {
        Assert.Equal("1. en\n1. to", WikiMarkup.ToCommonMark("# en\n# to"));
    }

    [Fact]
    public void A_bullet_list_becomes_dashes()
    {
        Assert.Equal("- en\n- to", WikiMarkup.ToCommonMark("* en\n* to"));
    }

    /// <summary>
    /// Jira spells nesting by repeating the marker, so `##` is a second-level ordered item. A rule
    /// that matched only a single marker left it alone, and CommonMark then read the bare `##` as a
    /// top-level heading: the item was promoted out of the list and the list itself split in three.
    /// Nested items are ordinary in acceptance criteria, which is where descriptions come from.
    /// </summary>
    [Fact]
    public void A_nested_ordered_item_nests_rather_than_becoming_a_heading()
    {
        Assert.Equal(
            "1. en\n    1. en-a\n1. to",
            WikiMarkup.ToCommonMark("# en\n## en-a\n# to"));
    }

    [Fact]
    public void A_quote_becomes_a_blockquote()
    {
        Assert.Equal("> sagt", WikiMarkup.ToCommonMark("bq. sagt"));
    }

    [Fact]
    public void A_rule_becomes_a_thematic_break()
    {
        Assert.Equal("***", WikiMarkup.ToCommonMark("----"));
    }

    /// <summary>
    /// Jira does not require a blank line before ----, and `---` under a line of text is a setext
    /// H2 in CommonMark: the rule vanishes and the paragraph above is promoted. `***` cannot be
    /// read that way. This is the case the first version of the converter got wrong.
    /// </summary>
    [Fact]
    public void A_rule_under_a_line_of_text_stays_a_rule()
    {
        Assert.Equal("tekst\n***\nmere", WikiMarkup.ToCommonMark("tekst\n----\nmere"));
    }

    /// <summary>
    /// The converter covers a subset, and the subset is documented. What matters is which way it
    /// fails on the rest: an unknown macro is left as literal text rather than dropped. Dropping
    /// is worse than showing `{color}`, because nobody can tell that something went missing.
    /// </summary>
    [Fact]
    public void An_unknown_macro_survives_as_text()
    {
        // The macro has to be *transparent*, not merely untouched: with the same string on both
        // sides, this passed for any converter that left `{color}` alone, a no-op included.
        Assert.Equal("{color:red}**fed**{color}", WikiMarkup.ToCommonMark("{color:red}*fed*{color}"));
    }

    [Fact]
    public void An_empty_description_is_null_rather_than_an_empty_note()
    {
        Assert.Null(WikiMarkup.ToCommonMark(null));
        Assert.Null(WikiMarkup.ToCommonMark("   "));
    }

    /// <summary>
    /// A bullet line inside a code fence is not a bullet. The line-based rules must not run
    /// inside a fence, and this is the only test that can tell.
    /// </summary>
    [Fact]
    public void The_line_rules_do_not_run_inside_a_code_block()
    {
        Assert.Equal(
            "```\n* ikke en liste\nh1. ikke en overskrift\n```",
            WikiMarkup.ToCommonMark("{code}\n* ikke en liste\nh1. ikke en overskrift\n{code}"));
    }

    /// <summary>
    /// {noformat} is Jira's other verbatim block, and the argument for protecting it is the one
    /// above word for word. It was missed because the fence test named only {code}.
    /// </summary>
    [Fact]
    public void The_line_rules_do_not_run_inside_a_noformat_block()
    {
        Assert.Equal(
            "```\n* ikke en liste\nh1. ikke en overskrift\n```",
            WikiMarkup.ToCommonMark("{noformat}\n* ikke en liste\nh1. ikke en overskrift\n{noformat}"));
    }

    /// <summary>Bold inside inline code is code, not bold. Same argument as the fence, one line up.</summary>
    [Fact]
    public void Inline_code_keeps_its_asterisks()
    {
        Assert.Equal("`a * b`", WikiMarkup.ToCommonMark("{{a * b}}"));
    }
}
