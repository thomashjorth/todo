using Todo.Core;

namespace Todo.Core.Tests;

public class RetroOwnershipTests
{
    private static readonly string[] Aliases = ["Thomas Hjorth Hansen", "Thomas"];

    [Fact]
    public void An_owner_matches_an_alias_regardless_of_case_and_padding()
        => Assert.True(RetroOwnership.IsOwnedBy("  Thomas Hjorth Hansen  ", ["thomas hjorth hansen"]));

    [Fact]
    public void A_card_without_an_owner_belongs_to_nobody()
        => Assert.False(RetroOwnership.IsOwnedBy(null, Aliases));

    [Fact]
    public void A_blank_owner_belongs_to_nobody()
        => Assert.False(RetroOwnership.IsOwnedBy("   ", Aliases));

    [Fact]
    public void Someone_elses_name_is_not_yours()
        => Assert.False(RetroOwnership.IsOwnedBy("Mette Kirkegaard", Aliases));

    [Fact]
    public void An_empty_alias_list_owns_nothing()
        => Assert.False(RetroOwnership.IsOwnedBy("Thomas Hjorth Hansen", []));

    [Fact]
    public void A_leading_owner_marker_is_stripped_from_the_title()
        => Assert.Equal(
            "Multi sub system tests",
            RetroOwnership.StripOwnerPrefix("THOMAS - Multi sub system tests", Aliases));

    [Fact]
    public void A_prefix_that_is_not_an_alias_is_left_alone()
        => Assert.Equal(
            "METTE - Multi sub system tests",
            RetroOwnership.StripOwnerPrefix("METTE - Multi sub system tests", Aliases));

    [Fact]
    public void A_title_without_a_marker_is_left_alone()
        => Assert.Equal(
            "Multi sub system tests",
            RetroOwnership.StripOwnerPrefix("Multi sub system tests", Aliases));

    [Fact]
    public void A_dash_inside_the_title_is_not_a_marker()
        => Assert.Equal(
            "Rerun - and then check the queue",
            RetroOwnership.StripOwnerPrefix("Rerun - and then check the queue", Aliases));
}
