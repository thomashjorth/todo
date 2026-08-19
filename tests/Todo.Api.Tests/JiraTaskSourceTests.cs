using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.TestSupport.Jira;

namespace Todo.Api.Tests;

public class JiraTaskSourceTests
{
    [Fact]
    public async Task Testing_the_connection_answers_with_the_display_name()
    {
        await using var jira = await FakeJira.StartAsync();

        var identity = await jira.SourceFor("SAAS").TestAsync();

        Assert.Equal("Thomas", identity.DisplayName);
    }

    /// <summary>
    /// The PAT goes in as a Bearer token. Measured against the real instance 2026-08-18: GET
    /// /rest/api/2/myself with Authorization: Bearer answers 200. Basic auth would also be
    /// plausible from the outside, so this pins which one.
    /// </summary>
    [Fact]
    public async Task The_token_is_sent_as_a_bearer_token()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").TestAsync();

        Assert.Equal("Bearer", jira.LastAuthorizationScheme);
        Assert.Equal(FakeJira.Token, jira.LastAuthorizationParameter);
    }

    [Fact]
    public async Task A_refused_token_becomes_a_source_exception_rather_than_a_crash()
    {
        await using var jira = await FakeJira.StartAsync(rejectToken: true);

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS").TestAsync());

        Assert.Equal(ErrorCodes.JiraRefused, exception.Code);
    }

    [Fact]
    public async Task The_status_names_come_back_sorted_and_without_duplicates()
    {
        await using var jira = await FakeJira.StartAsync();

        var names = await jira.SourceFor("SAAS").FetchStatusNamesAsync();

        Assert.Equal(
            ["Afventer general", "I gang", "Løst", "Venter på support"],
            names);
    }

    /// <summary>
    /// Added on top of the plan's nine, because the guard above cannot see the comparer it was
    /// written for. Measured on net10.0 with ICU: da-DK, Ordinal and InvariantCulture all order
    /// those four names identically, so swapping the Danish comparer for StringComparer.Ordinal
    /// leaves it green. The plan's reason — that a code-point sort would put Løst after Venter —
    /// is not what happens: 'L' is 0x4C and 'V' is 0x56, the comparison is decided on the first
    /// character, and the 'ø' is never reached.
    ///
    /// Æ and Å are where the two actually part ways, and both are ordinary letters in a Danish
    /// status name. Danish collation sorts them after z in the order æ, ø, å; code points put Å
    /// (0xC5) before Æ (0xC6). So this pair, and only a pair like it, can tell the comparers
    /// apart. Measured both ways: da-DK gives Ændret then Åben, Ordinal gives Åben then Ændret.
    /// </summary>
    [Fact]
    public async Task The_status_names_sort_the_danish_way_rather_than_by_code_point()
    {
        await using var jira = await FakeJira.StartAsync(
            statusNames: ["Åben", "Ændret", "Zebra"]);

        var names = await jira.SourceFor("SAAS").FetchStatusNamesAsync();

        Assert.Equal(["Zebra", "Ændret", "Åben"], names);
    }

    /// <summary>
    /// The JQL is the whole requirement about only importing SAAS. Asserting on the query string
    /// the source sent is the only place that can see it — the fake would happily answer a JQL
    /// with no project clause at all.
    /// </summary>
    [Fact]
    public async Task The_query_is_narrowed_to_the_configured_project()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").FetchAssignedAsync();

        Assert.Contains("project = SAAS", jira.LastJql);
        Assert.Contains("assignee = currentUser()", jira.LastJql);
        Assert.Contains("resolution = Unresolved", jira.LastJql);
    }

    [Fact]
    public async Task An_issue_maps_field_by_field()
    {
        await using var jira = await FakeJira.StartAsync();

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        var issue = Assert.Single(page.Items, i => i.Key == "SAAS-1");

        Assert.Equal("Kunden kan ikke logge ind", issue.Title);
        Assert.Equal(new DateOnly(2026, 8, 20), issue.Deadline);
        Assert.Equal("Anna Andersen", issue.Requester);
        Assert.Equal("I gang", issue.StatusName);
        // The description arrives as wiki markup and is stored as CommonMark.
        Assert.Equal("**vigtigt**", issue.Note);
    }

    [Fact]
    public async Task An_issue_without_a_due_date_or_reporter_still_maps()
    {
        await using var jira = await FakeJira.StartAsync();

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        var issue = Assert.Single(page.Items, i => i.Key == "SAAS-3");

        Assert.Null(issue.Deadline);
        Assert.Null(issue.Requester);
        Assert.Null(issue.Note);
    }

    /// <summary>
    /// Classic pagination, measured 2026-08-18: startAt, maxResults, total, issues — not Cloud's
    /// nextPageToken/isLast. The fake serves two pages, so a source that reads only the first one
    /// fails here.
    /// </summary>
    [Fact]
    public async Task Every_page_is_read_rather_than_only_the_first()
    {
        await using var jira = await FakeJira.StartAsync(pageSize: 2);

        var page = await jira.SourceFor("SAAS").FetchAssignedAsync();

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task An_unreachable_host_becomes_a_source_exception()
    {
        await using var jira = await FakeJira.StartAsync();
        await jira.StopServerAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS").TestAsync());

        Assert.Equal(ErrorCodes.JiraUnreachable, exception.Code);
    }
}
