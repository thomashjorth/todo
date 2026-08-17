using System.Net.Http.Json;
using Todo.Contracts;
using Todo.Core.Tasks;
using Todo.TestSupport;
using Todo.TestSupport.Builders;
using Todo.TestSupport.Time;
using ContractBucket = Todo.Contracts.DeadlineBucket;
using CoreBucket = Todo.Core.Tasks.DeadlineBucket;
using CoreStatus = Todo.Core.Tasks.TodoStatus;

namespace Todo.Api.Tests.Builders;

public class TaskItemBuilderTests
{
    // 2026-08-12 is a Wednesday, so the week ends Sunday 2026-08-16.
    private static readonly DateOnly Wednesday = new(2026, 8, 12);
    private static readonly DateOnly Saturday = new(2026, 8, 15);
    private static readonly DateOnly Sunday = new(2026, 8, 16);

    [Fact]
    public void A_task_with_nothing_set_is_already_usable()
    {
        var clock = new FixedClock(Wednesday);

        var task = new TaskItemBuilder(clock).Build();

        Assert.NotEmpty(task.Title);
        Assert.Equal("manual", task.SourceId);
        Assert.Equal(CoreStatus.Open, task.Status);
        Assert.Equal(clock.UtcNow, task.CreatedAt);
        Assert.Null(task.Deadline);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Overdue_is_a_day_the_list_has_already_passed()
    {
        var task = new TaskItemBuilder(new FixedClock(Wednesday)).Overdue().Build();

        Assert.Equal(CoreBucket.Overdue, DeadlineBuckets.For(task.Deadline, null, Wednesday));
    }

    [Fact]
    public void Due_today_is_today()
    {
        var task = new TaskItemBuilder(new FixedClock(Wednesday)).DueToday().Build();

        Assert.Equal(Wednesday, task.Deadline);
        Assert.Equal(CoreBucket.Today, DeadlineBuckets.For(task.Deadline, null, Wednesday));
    }

    [Fact]
    public void Due_this_week_is_the_last_day_of_the_week()
    {
        var task = new TaskItemBuilder(new FixedClock(Wednesday)).DueThisWeek().Build();

        Assert.Equal(Sunday, task.Deadline);
        Assert.Equal(CoreBucket.ThisWeek, DeadlineBuckets.For(task.Deadline, null, Wednesday));
    }

    [Fact]
    public void Due_this_week_on_a_saturday_is_the_day_after()
    {
        var task = new TaskItemBuilder(new FixedClock(Saturday)).DueThisWeek().Build();

        Assert.Equal(Sunday, task.Deadline);
        Assert.Equal(CoreBucket.ThisWeek, DeadlineBuckets.For(task.Deadline, null, Saturday));
    }

    [Fact]
    public void Due_this_week_on_a_sunday_says_the_week_is_over_instead_of_drifting_into_later()
    {
        var builder = new TaskItemBuilder(new FixedClock(Sunday));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.DueThisWeek());

        Assert.Contains("2026-08-16", exception.Message);
        Assert.Contains("Sunday", exception.Message);
    }

    [Fact]
    public void A_done_task_records_when_it_was_completed()
    {
        var clock = new FixedClock(Wednesday);

        var task = new TaskItemBuilder(clock).Done().Build();

        Assert.Equal(CoreStatus.Done, task.Status);
        Assert.Equal(clock.UtcNow, task.CompletedAt);
    }

    [Fact]
    public void An_in_progress_task_is_not_completed()
    {
        var task = new TaskItemBuilder(new FixedClock(Wednesday)).InProgress().Build();

        Assert.Equal(CoreStatus.InProgress, task.Status);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Sub_tasks_keep_the_order_they_were_added()
    {
        var task = new TaskItemBuilder()
            .WithSubTask("Bestil bønner")
            .WithSubTask("Male bønner", isDone: true)
            .Build();

        Assert.Equal(["Bestil bønner", "Male bønner"], task.SubTasks.Select(s => s.Title));
        Assert.Equal([0, 1], task.SubTasks.Select(s => s.SortOrder));
        Assert.Equal([false, true], task.SubTasks.Select(s => s.IsDone));
    }

    [Fact]
    public void A_task_from_retro_carries_the_key_a_re_import_matches_on()
    {
        var task = new TaskItemBuilder().FromRetro("abc123").Build();

        Assert.Equal("retro", task.SourceId);
        Assert.Equal("abc123", task.ExternalKey);
    }

    [Fact]
    public async Task An_arranged_task_and_alias_are_served_by_the_api()
    {
        await using var host = await RunningHost.StartAsync();

        await host.AddAndSaveChangesAsync(
            new TaskItemBuilder()
                .Titled("Køb kaffe")
                .DueToday()
                .RequestedBy("Anna")
                .WithSubTask("Bestil bønner")
                .Build(),
            UserAliases.Named("Thomas Hjorth Hansen"));

        var tasks = await host.Client.GetFromJsonAsync<TodoTaskListResponse>("/api/tasks");
        Assert.NotNull(tasks);
        var task = Assert.Single(tasks.Items);
        Assert.Equal("Køb kaffe", task.Title);
        Assert.Equal("Anna", task.Requester);
        Assert.Equal(ContractBucket.Today, task.Bucket);
        Assert.Equal("Bestil bønner", Assert.Single(task.SubTasks).Title);

        var aliases = await host.Client.GetFromJsonAsync<RetroAliasesResponse>("/api/retro/aliases");
        Assert.NotNull(aliases);
        Assert.Equal(["Thomas Hjorth Hansen"], aliases.Aliases);
    }
}
