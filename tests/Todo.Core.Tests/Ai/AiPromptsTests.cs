using Todo.Core.Ai;

namespace Todo.Core.Tests.Ai;

public class AiPromptsTests
{
    /// <summary>A Thursday, so "næste mandag" is in the second week of the calendar.</summary>
    private static readonly DateOnly Thursday = new(2026, 10, 22);

    [Fact]
    public void The_quick_entry_prompt_names_today_with_its_weekday()
    {
        Assert.Contains("Today is torsdag 2026-10-22.", AiPrompts.ParseTask("Køb mælk", Thursday));
    }

    /// <summary>
    /// The calendar is why the dates came out right when measured. These two days are the ones the
    /// model got wrong without it, and the boundary of the fourteen.
    /// </summary>
    [Theory]
    [InlineData("- fredag 2026-10-23")]
    [InlineData("- mandag 2026-10-26")]
    [InlineData("- torsdag 2026-11-05")]
    public void The_quick_entry_prompt_writes_out_the_next_two_weeks(string line)
    {
        Assert.Contains(line, AiPrompts.ParseTask("Køb mælk", Thursday));
    }

    [Fact]
    public void The_calendar_starts_tomorrow_and_stops_after_fourteen_days()
    {
        var prompt = AiPrompts.ParseTask("Køb mælk", Thursday);

        Assert.DoesNotContain("- torsdag 2026-10-22", prompt);
        Assert.DoesNotContain("2026-11-06", prompt);
        Assert.Equal(AiPrompts.CalendarDays, prompt.Split('\n').Count(l => l.StartsWith("- ")) - 3);
    }

    [Fact]
    public void The_quick_entry_prompt_ends_with_the_typed_line()
    {
        var prompt = AiPrompts.ParseTask("Ring til Anna om fakturaen på fredag", Thursday);

        Assert.EndsWith("The line:\nRing til Anna om fakturaen på fredag", prompt.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_split_prompt_carries_the_title_the_note_and_the_existing_subtasks()
    {
        var prompt = AiPrompts.Split(
            "Opdatér certifikatet", "Det ligger i Key Vault.", ["Find ejeren", "Bestil certifikat"], AiLanguage.Danish);

        Assert.Contains("Task: Opdatér certifikatet", prompt);
        Assert.Contains("Note: Det ligger i Key Vault.", prompt);
        Assert.Contains("Existing subtasks: Find ejeren; Bestil certifikat", prompt);
    }

    [Fact]
    public void The_split_prompt_says_none_rather_than_leaving_a_field_empty()
    {
        var prompt = AiPrompts.Split("Opdatér certifikatet", null, [], AiLanguage.Danish);

        Assert.Contains("Note: (none)", prompt);
        Assert.Contains("Existing subtasks: (none)", prompt);
    }

    [Fact]
    public void The_summary_prompt_carries_the_title_and_the_whole_note()
    {
        var prompt = AiPrompts.Summarize("CSV-eksport fejler", "Første linje.\nAnden linje.", AiLanguage.Danish);

        Assert.Contains("Title: CSV-eksport fejler", prompt);
        Assert.Contains("Første linje.\nAnden linje.", prompt.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The language is named, and Danish rules Norwegian out in so many words: the first eval run got
    /// "Ring tannlegen" and Norwegian subtasks for Danish input.
    /// </summary>
    [Fact]
    public void A_prompt_names_the_language_it_was_given()
    {
        Assert.Contains(
            "Write in Danish - not Norwegian, not Swedish.",
            AiPrompts.Summarize("Login fejler", "Noget fejler.", AiLanguage.Danish));
        Assert.Contains(
            "Write in English.",
            AiPrompts.Split("Login fejler", null, [], AiLanguage.English));
    }

    [Fact]
    public void The_quick_entry_prompt_names_the_language_of_the_line()
    {
        Assert.Contains("Write in English.", AiPrompts.ParseTask("Call the dentist tomorrow", Thursday));
        Assert.Contains("Write in Danish", AiPrompts.ParseTask("Ryd op i backloggen", Thursday));
    }
}
