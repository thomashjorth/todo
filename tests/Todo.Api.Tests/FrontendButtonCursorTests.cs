using System.Text.RegularExpressions;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// Tailwind 4's base styles give every button <c>cursor: default</c>, so a button without
/// <c>cursor-pointer</c> shows the arrow when the mouse is over it and reads as dead text. No button
/// in the app had one until the user hovered over the AI suggestion's buttons and saw no pointer
/// (2026-10-22) - every button was affected, they were only the first to be noticed.
/// <para>
/// A class on each button rather than one rule in styles.css, because the app keeps to utility
/// classes and has exactly one CSS rule on purpose. The price is that a new button can forget it,
/// which is what this guard is for. It reads the templates as text, with comments removed first:
/// several comments say "a &lt;button&gt;, not an &lt;a href&gt;", and they are not buttons.
/// </para>
/// </summary>
public partial class FrontendButtonCursorTests
{
    [Fact]
    public void Every_button_shows_a_pointer()
    {
        var app = Path.Combine(RepoPaths.Root, "src", "Todo.Web", "src", "app");
        var buttons = 0;
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(app, "*.html", SearchOption.AllDirectories))
        {
            var template = Comment().Replace(File.ReadAllText(path), string.Empty);

            foreach (Match tag in ButtonTag().Matches(template))
            {
                buttons++;

                if (!tag.Value.Contains("cursor-pointer", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(app, path)}: {Regex.Replace(tag.Value, @"\s+", " ")}");
                }
            }
        }

        // A scan that found nothing also finds nothing wrong: a moved folder would pass this.
        Assert.True(buttons > 30, $"Only {buttons} buttons were found under {app}.");
        Assert.True(
            offenders.Count == 0,
            "These buttons have no cursor-pointer, so the mouse shows an arrow over them:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>The start tag only. An Angular binding can hold "=&gt;", so attributes are matched whole.</summary>
    [GeneratedRegex("""<button\b(?:"[^"]*"|[^">])*>""")]
    private static partial Regex ButtonTag();
}
