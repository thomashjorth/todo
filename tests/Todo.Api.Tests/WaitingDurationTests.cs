using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Time;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;

namespace Todo.Api.Tests;

/// <summary>
/// A wait is counted in whole days, so these tests arrange one that started days ago and read
/// the count back. On the real clock a run that crossed midnight would count a day too many.
/// </summary>
public class WaitingDurationTests : TaskApiTest
{
    private static readonly FixedClock Clock = new(new DateOnly(2026, 8, 14));

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(Clock);

    [Fact]
    public async Task Editing_something_else_on_a_waiting_task_does_not_restart_the_wait()
    {
        await Host.AddAndSaveChangesAsync(new TaskItemBuilder(Clock)
            .Titled("Ask Bo for the numbers")
            .WaitingFor("Bo", Clock.UtcNow.AddDays(-5))
            .Build());

        var waiting = Assert.Single(await ListAsync());
        Assert.Equal(5, waiting.WaitingDays);

        var renamed = await UpdateAsync(
            waiting.Id, "Ask Bo for the numbers again", TodoStatus.WaitingFor, "Bo");

        Assert.Equal(waiting.WaitingSince, renamed.WaitingSince);
        Assert.Equal(5, renamed.WaitingDays);
    }

    [Fact]
    public async Task A_wait_that_started_twelve_days_ago_is_twelve_days_long()
    {
        await Host.AddAndSaveChangesAsync(new TaskItemBuilder(Clock)
            .Titled("Ask Bo for the numbers")
            .WaitingFor("Bo", Clock.UtcNow.AddDays(-12))
            .Build());

        var waiting = Assert.Single(await ListAsync());

        Assert.Equal("Bo", waiting.WaitingOn);
        Assert.Equal(12, waiting.WaitingDays);
    }
}
