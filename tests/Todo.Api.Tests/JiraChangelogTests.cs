using Todo.TestSupport.Jira;

namespace Todo.Api.Tests;

/// <summary>
/// WaitingSince is what the app counts a waiting duration from, and Jira DC 10.3.24 does not return
/// statuscategorychangedate (measured 2026-08-18), so it has to come out of the changelog: the
/// created date of the newest history entry that carries a status item.
/// </summary>
public class JiraChangelogTests
{
    /// <summary>
    /// The offset is the point. Jira answers 2026-08-17T14:10:13.593+0200; the app stores UTC
    /// DateTime — never DateTimeOffset, which SQLite cannot sort — so the stored value has to be
    /// 12:10. A plain DateTime.Parse keeps 14:10 and passes every other assertion here.
    /// </summary>
    [Fact]
    public async Task The_status_change_is_converted_to_utc()
    {
        await using var jira = await FakeJira.StartAsync();

        var changed = await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Equal(new DateTime(2026, 8, 17, 12, 10, 13, 593, DateTimeKind.Utc), changed);
        Assert.Equal(DateTimeKind.Utc, changed!.Value.Kind);
    }

    /// <summary>
    /// SAAS-3 is still in the status it was created in, so its changelog is empty. The second
    /// assertion is what makes the first one mean anything: on its own, Assert.Null passes for an
    /// implementation that answers null without ever asking Jira, which is exactly the shape this
    /// method had before it was written. Measured — a method returning null outright passed the
    /// null assertion and failed only on the call.
    /// </summary>
    [Fact]
    public async Task An_issue_that_never_changed_status_has_no_waiting_since()
    {
        await using var jira = await FakeJira.StartAsync();

        Assert.Null(await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-3"));
        Assert.Equal(["SAAS-3"], jira.ChangelogRequests);
    }

    /// <summary>
    /// What this forbids is the reason the status filter exists: SAAS-2's newest history entry is
    /// an assignee change, and taking the newest entry outright would date the wait from it. The
    /// negative assertion carries the name because it is the one that names the wrong answer —
    /// 2026-08-18T08:00:00+0200, which is 06:00 UTC. The positive assertion beside it says which
    /// entry should have won instead, so a third wrong answer cannot slip past either.
    /// </summary>
    [Fact]
    public async Task The_newest_status_change_wins_over_a_newer_unrelated_change()
    {
        await using var jira = await FakeJira.StartAsync();

        var changed = await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.NotEqual(new DateTime(2026, 8, 18, 6, 0, 0, DateTimeKind.Utc), changed);
        Assert.Equal(new DateTime(2026, 8, 17, 12, 10, 13, 593, DateTimeKind.Utc), changed);
    }

    /// <summary>
    /// One HTTP call per issue, not one per history entry: the changelog arrives whole with the
    /// issue, so reading it must not turn into a request per row. The stronger-sounding claim that
    /// the changelog is only fetched for issues that are actually waiting cannot live here —
    /// FetchStatusChangedAtAsync fetches for the key it is handed, and deciding which keys to hand
    /// it is the preview's job in task 6. That is where the assertion belongs.
    /// </summary>
    [Fact]
    public async Task One_call_per_issue_rather_than_a_call_per_history_entry()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Equal(["SAAS-2"], jira.ChangelogRequests);
    }

    /// <summary>
    /// SAAS-4 carries exactly one history entry and nothing else. Without it the newest-wins
    /// ordering is only ever exercised on SAAS-2's three entries, where a list of several is what
    /// makes the sort visible — a one-entry changelog is the case where a sort that throws on an
    /// empty sequence, or an ordering written as Last() instead, would still look right. It also
    /// pins that the single entry is found at all rather than skipped.
    /// </summary>
    [Fact]
    public async Task A_lone_status_change_is_found_in_a_changelog_of_one()
    {
        await using var jira = await FakeJira.StartAsync();

        var changed = await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-4");

        Assert.Equal(new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc), changed);
    }

    /// <summary>
    /// Jira leaves the changelog out unless the caller expands it, so the query parameter is the
    /// difference between a real waiting date and null for every row. Asserted here rather than
    /// left to the fake's fixture: this is the one failure mode that would look like "Jira has no
    /// history for any of my issues" instead of like a bug.
    /// </summary>
    [Fact]
    public async Task The_changelog_is_asked_for_by_expanding_it()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").FetchStatusChangedAtAsync("SAAS-2");

        Assert.Contains("expand=changelog", jira.LastIssueQuery);
    }
}
