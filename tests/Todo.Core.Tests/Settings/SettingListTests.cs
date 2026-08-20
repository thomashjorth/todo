using Todo.Core.Settings;

namespace Todo.Core.Tests.Settings;

/// <summary>
/// The storage shape of every list setting, tested here rather than through the API because it is a
/// pure function of a string. The reading half used to be private in JiraSettingsReader, where the
/// corrupt-JSON fallback could only be reached by putting rubbish in the database; the writing half
/// is new with the delegates list.
/// </summary>
public class SettingListTests
{
    [Fact]
    public void A_missing_row_reads_as_an_empty_list()
    {
        Assert.Empty(SettingList.Read(null));
    }

    /// <summary>
    /// A half-written row or a hand-edited file must not stop the settings page from opening, so
    /// unreadable JSON is an empty list rather than an exception.
    /// </summary>
    [Theory]
    [InlineData("{not json")]
    [InlineData("\"a string, not a list\"")]
    [InlineData("42")]
    public void A_corrupt_value_reads_as_an_empty_list(string json)
    {
        Assert.Empty(SettingList.Read(json));
    }

    [Fact]
    public void A_list_round_trips_through_write_and_read()
    {
        var stored = SettingList.Write(["Flemming", "Gitte"]);

        Assert.NotNull(stored);
        Assert.Equal(["Flemming", "Gitte"], SettingList.Read(stored));
    }

    /// <summary>
    /// Null rather than "[]", so StoreAsync removes the row. Two tests in SettingsEndpointsTests
    /// assert Assert.Empty and Assert.Single on the whole Settings table, and a leftover row makes
    /// them red — a coupling nothing in this file points at, hence this note.
    /// </summary>
    [Fact]
    public void An_empty_list_is_no_row_at_all()
    {
        Assert.Null(SettingList.Write([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_list_of_nothing_but_blanks_is_no_row_either(string? value)
    {
        Assert.Null(SettingList.Write([value]));
    }

    [Fact]
    public void A_blank_name_is_dropped_from_a_list_that_has_real_ones()
    {
        Assert.Equal(["Flemming"], SettingList.Read(SettingList.Write(["Flemming", "  ", null])));
    }

    /// <summary>
    /// Trimmed on the way in, so the value the frontend offers as a suggestion is the name and not
    /// the spacing around it. Asserted on the read-back rather than on the JSON so it says what the
    /// caller sees.
    /// </summary>
    [Fact]
    public void A_name_is_trimmed()
    {
        Assert.Equal(["Flemming"], SettingList.Read(SettingList.Write(["  Flemming  "])));
    }

    /// <summary>
    /// Case-insensitive, and the first spelling wins — the same rule RetroEndpoints applies to
    /// aliases. Note that the endpoint <em>rejects</em> a duplicate before it ever gets here; this
    /// is the last line of defence, not the message the user sees.
    /// </summary>
    [Fact]
    public void Two_names_that_differ_only_in_case_collapse_into_one()
    {
        Assert.Equal(["Flemming"], SettingList.Read(SettingList.Write(["Flemming", "FLEMMING"])));
    }

    [Fact]
    public void An_exact_duplicate_collapses_too()
    {
        Assert.Equal(["Gitte"], SettingList.Read(SettingList.Write(["Gitte", "Gitte"])));
    }
}
