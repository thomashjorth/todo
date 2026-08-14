using Todo.Core.Retro;

namespace Todo.Core.Tests.Retro;

public class RetroCsvParserTests
{
    private const string Header =
        @"""Content"",""Author"",""Created"",""Zone"",""Action Due Date"",""Action Owner""";

    private static string Fixture()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "retro-board.csv"));

    private static RetroRow Single(string csv) => Assert.Single(RetroCsvParser.Parse(csv).Rows);

    [Fact]
    public void The_export_keeps_only_the_seven_content_rows()
        => Assert.Equal(7, RetroCsvParser.Parse(Fixture()).Rows.Count);

    [Fact]
    public void The_export_reports_how_many_rating_cards_it_dropped()
        => Assert.Equal(18, RetroCsvParser.Parse(Fixture()).SkippedRatingCards);

    [Fact]
    public void Rating_cards_are_dropped_but_a_comment_in_the_same_zone_survives()
    {
        var csv = $"""
            {Header}
            "8","Mette Kirkegaard","7/17/26, 1:32 PM","Quality","",""
            "9/10","Rasmus Bjerre","7/17/26, 1:33 PM","Mood","",""
            "10 / 10","Sofie Dalgaard","7/17/26, 1:34 PM","Mood","",""
            "The mood was better once the pipeline stopped flaking","Sofie Dalgaard","7/17/26, 1:35 PM","Mood","",""
            """;

        var row = Single(csv);

        Assert.Equal("The mood was better once the pipeline stopped flaking", row.Title);
        Assert.Equal("Mood", row.Zone);
    }

    [Fact]
    public void A_danish_due_date_is_read_as_day_before_month()
    {
        var row = RetroCsvParser.Parse(Fixture()).Rows
            .First(r => r.Title.StartsWith("Since we dont have resqueue"));

        Assert.Equal(new DateOnly(2026, 7, 24), row.DueDate);
    }

    [Fact]
    public void An_empty_due_date_is_null_rather_than_an_error()
    {
        var row = RetroCsvParser.Parse(Fixture()).Rows
            .First(r => r.Title.StartsWith("preperation to retrospektive"));

        Assert.Null(row.DueDate);
    }

    [Fact]
    public void An_unreadable_due_date_is_null_rather_than_an_error()
    {
        var csv = $"""
            {Header}
            "Buy a whiteboard","Mette Kirkegaard","7/17/26, 1:32 PM","Actions","next friday","Mette Kirkegaard"
            """;

        Assert.Null(Single(csv).DueDate);
    }

    [Fact]
    public void The_american_created_stamp_is_read_as_month_before_day()
    {
        var row = RetroCsvParser.Parse(Fixture()).Rows
            .First(r => r.Title.StartsWith("Since we dont have resqueue"));

        Assert.Equal(new DateTime(2026, 7, 13, 16, 9, 0), row.Created);
    }

    [Fact]
    public void Runs_of_whitespace_in_the_title_collapse_to_one_space()
    {
        var row = RetroCsvParser.Parse(Fixture()).Rows
            .First(r => r.Title.StartsWith("Multi sub system tests"));

        Assert.Equal(
            "Multi sub system tests FSTYR process flow: external api -> scheduler (consumer) -> online booking api",
            row.Title);
    }

    [Fact]
    public void A_comma_inside_a_quoted_field_stays_in_the_same_field()
    {
        var csv = $"""
            {Header}
            "Discuss it on the SP with Nikolaj, Peter and the rest","Mette Kirkegaard","7/17/26, 2:01 PM","Actions","",""
            """;

        var row = Single(csv);

        Assert.Equal("Discuss it on the SP with Nikolaj, Peter and the rest", row.Title);
        Assert.Equal("Mette Kirkegaard", row.Author);
    }

    [Fact]
    public void A_newline_inside_a_quoted_field_stays_in_the_same_field()
    {
        var csv = $"{Header}\r\n\"Write it down\nbefore the meeting\",\"Rasmus Bjerre\",\"7/17/26, 1:40 PM\",\"Improve\",\"\",\"\"\r\n";

        var row = Single(csv);

        Assert.Equal("Write it down before the meeting", row.Title);
        Assert.Equal("Improve", row.Zone);
    }

    [Fact]
    public void Identical_content_in_two_zones_gets_two_dedup_keys()
    {
        var rows = RetroCsvParser.Parse(Fixture()).Rows
            .Where(r => r.Title.StartsWith("Be better at writting down"))
            .ToList();

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].DedupKey, rows[1].DedupKey);
    }

    [Fact]
    public void The_same_row_parsed_twice_gets_the_same_dedup_key()
    {
        var first = RetroCsvParser.Parse(Fixture()).Rows.Select(r => r.DedupKey);
        var second = RetroCsvParser.Parse(Fixture()).Rows.Select(r => r.DedupKey);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_dedup_key_fits_the_external_key_column()
        => Assert.All(RetroCsvParser.Parse(Fixture()).Rows, r => Assert.Equal(64, r.DedupKey.Length));

    [Fact]
    public void A_csv_without_a_content_column_says_which_columns_it_found()
    {
        var csv = """
            "Text","Author","Zone"
            "Buy a whiteboard","Mette Kirkegaard","Actions"
            """;

        var exception = Assert.Throws<FormatException>(() => RetroCsvParser.Parse(csv));

        Assert.Contains("Content", exception.Message);
        Assert.Contains("Text", exception.Message);
        Assert.Contains("Zone", exception.Message);
    }

    [Fact]
    public void The_optional_columns_may_be_missing_altogether()
    {
        var csv = """
            "Content","Author","Created","Zone"
            "Buy a whiteboard","Mette Kirkegaard","7/17/26, 1:32 PM","Actions"
            """;

        var row = Single(csv);

        Assert.Equal("Buy a whiteboard", row.Title);
        Assert.Null(row.Owner);
        Assert.Null(row.DueDate);
    }

    [Fact]
    public void An_owner_is_read_from_the_action_owner_column_whatever_the_zone_is()
    {
        var row = RetroCsvParser.Parse(Fixture()).Rows
            .First(r => r.Title.StartsWith("Since we dont have resqueue"));

        Assert.Equal("Add", row.Zone);
        Assert.Equal("Ida Lindqvist Medarbejder", row.Owner);
    }

    [Fact]
    public void A_blank_content_row_is_skipped()
    {
        var csv = $"""
            {Header}
            "","Mette Kirkegaard","7/17/26, 1:32 PM","Actions","",""
            "Buy a whiteboard","Mette Kirkegaard","7/17/26, 1:33 PM","Actions","",""
            """;

        Assert.Equal("Buy a whiteboard", Single(csv).Title);
    }
}
