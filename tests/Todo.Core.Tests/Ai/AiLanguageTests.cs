using Todo.Core.Ai;

namespace Todo.Core.Tests.Ai;

/// <summary>
/// The cases are the ones the model itself got wrong in the eval runner, which is why the language is
/// decided here rather than left to it.
/// </summary>
public class AiLanguageTests
{
    [Theory]
    [InlineData("Flyt til nyt kontor")]
    [InlineData("Ryd op i backloggen")]
    [InlineData("Køb mælk")]
    [InlineData("Planlæg teamdag i november")]
    public void Danish_lines_are_danish(string text)
    {
        Assert.Equal(AiLanguage.Danish, AiLanguage.Of(text));
    }

    [Theory]
    [InlineData("Prepare the quarterly security review")]
    [InlineData("Call the dentist tomorrow")]
    [InlineData("When users with more than 50 roles log in, the login page times out")]
    public void English_lines_are_english(string text)
    {
        Assert.Equal(AiLanguage.English, AiLanguage.Of(text));
    }

    /// <summary>The app's user is Danish, so a line with no marker word at all is read as Danish.</summary>
    [Fact]
    public void A_line_without_a_single_marker_word_is_danish()
    {
        Assert.Equal(AiLanguage.Danish, AiLanguage.Of("Sprint review"));
    }

    /// <summary>"for" is both languages; counted as English it made a Danish subtask English.</summary>
    [Fact]
    public void For_is_not_counted_as_either_language()
    {
        Assert.Equal(AiLanguage.Danish, AiLanguage.Of("Tjek transport for alle"));
    }
}
