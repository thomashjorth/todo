using Todo.Core.Ado;

namespace Todo.Core.Tests.Ado;

/// <summary>
/// A pure function of two values, so it belongs here rather than behind the source. It is also the one
/// place in slice 12 where a wrong answer would be invisible: every imported work item would simply
/// have a deadline, and a deadline is exactly what nobody would look twice at.
/// </summary>
public class AdoDeadlineTests
{
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public void The_deadline_is_today_plus_the_configured_days()
    {
        Assert.Equal(new DateOnly(2026, 8, 23), AdoDeadline.For(Today, AdoDefaults.DeadlineDays));
    }

    /// <summary>
    /// The whole reason this is a function and not an addition written inline. Zero is a value here -
    /// "no deadline" - because the setting is a non-nullable int so the frontend gets no extra @if
    /// branch. Read as arithmetic instead, it would file every imported work item as due today, and
    /// turning the deadline off would be the loudest possible way to turn it on.
    /// </summary>
    [Fact]
    public void Zero_days_means_no_deadline_rather_than_today()
    {
        Assert.Null(AdoDeadline.For(Today, 0));
    }

    /// <summary>
    /// One is the neighbour of zero and has to still be a date, or "no deadline" would have eaten the
    /// smallest real setting. Guarding zero alone cannot see that.
    /// </summary>
    [Fact]
    public void One_day_is_tomorrow_rather_than_nothing()
    {
        Assert.Equal(new DateOnly(2026, 8, 21), AdoDeadline.For(Today, 1));
    }

    /// <summary>
    /// Cannot arrive - the endpoint refuses it and AdoSettingsReader reads an out-of-range row as the
    /// default - and pinned anyway, because "overdue the moment it was imported" is the one outcome
    /// nobody could have asked for and the arithmetic would happily produce it.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-365)]
    public void A_negative_number_of_days_is_no_deadline_rather_than_a_date_in_the_past(int days)
    {
        Assert.Null(AdoDeadline.For(Today, days));
    }

    /// <summary>
    /// Month and year boundaries, because AddDays is the only thing standing between this and a hand
    /// rolled date calculation somebody might "simplify" it into. 2028 is a leap year.
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 30, 3, 2026, 9, 2)]
    [InlineData(2026, 12, 30, 3, 2027, 1, 2)]
    [InlineData(2028, 2, 27, 3, 2028, 3, 1)]
    public void The_arithmetic_crosses_month_and_year_boundaries(
        int year, int month, int day, int days, int expectedYear, int expectedMonth, int expectedDay)
    {
        Assert.Equal(
            new DateOnly(expectedYear, expectedMonth, expectedDay),
            AdoDeadline.For(new DateOnly(year, month, day), days));
    }

    /// <summary>
    /// The upper bound is not this function's business, and that is a decision rather than an
    /// oversight: the endpoint refuses anything above AdoDefaults.DeadlineDaysMax and the reader reads
    /// such a row as the default, so by the time a number reaches here it has already been vetted
    /// twice. Folding a third check in would make one of the other two unreachable, and slice 11
    /// measured what that costs - two layers trimming the same trailing slash, neither able to say
    /// which one had ever fired.
    /// </summary>
    [Fact]
    public void A_number_above_the_allowed_maximum_is_still_arithmetic_here()
    {
        Assert.Equal(
            Today.AddDays(AdoDefaults.DeadlineDaysMax + 1),
            AdoDeadline.For(Today, AdoDefaults.DeadlineDaysMax + 1));
    }
}
