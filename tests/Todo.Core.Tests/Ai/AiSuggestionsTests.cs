using Todo.Core.Ai;

namespace Todo.Core.Tests.Ai;

public class AiSuggestionsTests
{
    [Fact]
    public void A_title_is_trimmed()
    {
        Assert.Equal("Køb mælk", AiSuggestions.Title("  Køb mælk ", "Køb mælk i dag"));
    }

    [Fact]
    public void An_empty_title_falls_back_to_the_typed_line()
    {
        Assert.Equal("Køb mælk i dag", AiSuggestions.Title(" ", " Køb mælk i dag "));
    }

    [Fact]
    public void A_title_is_cut_to_what_a_task_may_hold()
    {
        var title = AiSuggestions.Title(new string('x', 600), "x");

        Assert.Equal(AiSuggestions.TitleMaxLength, title.Length);
    }

    [Fact]
    public void A_blank_requester_is_no_requester()
    {
        Assert.Null(AiSuggestions.Requester("  "));
        Assert.Equal("Mads", AiSuggestions.Requester(" Mads "));
    }

    [Theory]
    [InlineData("2026-10-23", "2026-10-23")]
    [InlineData(" 2026-10-23 ", "2026-10-23")]
    [InlineData("23-10-2026", null)]
    [InlineData("fredag", null)]
    [InlineData("2026-02-30", null)]
    [InlineData(null, null)]
    public void Only_a_real_iso_date_becomes_a_deadline(string? suggested, string? expected)
    {
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), AiSuggestions.Deadline(suggested));
    }

    [Fact]
    public void Subtasks_lose_blanks_duplicates_and_what_the_task_already_has()
    {
        var titles = AiSuggestions.SubTasks(
            [" Find ejeren ", "", "find ejeren", "Bestil certifikat", null, "Genstart gatewayen"],
            ["BESTIL CERTIFIKAT"]);

        Assert.Equal(["Find ejeren", "Genstart gatewayen"], titles);
    }

    [Fact]
    public void At_most_six_subtasks_are_kept_and_the_first_six_win()
    {
        var titles = AiSuggestions.SubTasks(["1", "2", "3", "4", "5", "6", "7", "8"], []);

        Assert.Equal(["1", "2", "3", "4", "5", "6"], titles);
    }

    [Fact]
    public void No_subtasks_at_all_is_an_empty_list()
    {
        Assert.Empty(AiSuggestions.SubTasks(null, []));
    }

    [Fact]
    public void A_blank_summary_is_no_summary()
    {
        Assert.Null(AiSuggestions.Summary(" \n "));
        Assert.Equal("Eksporten fejler.", AiSuggestions.Summary(" Eksporten fejler. "));
    }
}
