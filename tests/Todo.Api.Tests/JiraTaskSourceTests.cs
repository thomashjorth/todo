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

    /// <summary>
    /// The duty clause widens the query beyond assignee, which is the whole requirement. Asserting on
    /// the JQL the source sent is the only place that can see it — the fake would happily answer a
    /// query with no duty clause at all.
    /// </summary>
    [Fact]
    public async Task On_duty_the_query_also_asks_for_the_pool()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general"], onDuty: true).FetchAssignedAsync();

        Assert.Contains("project = SAAS", jira.LastJql);
        Assert.Contains("assignee = currentUser()", jira.LastJql);
        Assert.Contains("status IN (\"Afventer general\")", jira.LastJql);
        Assert.Contains(" OR ", jira.LastJql);

        // JQL binds AND tighter than OR, so without the parentheses this reads as
        // "(project AND resolution AND assignee) OR (any issue in a duty status, anywhere)" — and the
        // token sees four projects, the customer one included. Asserting on the two clauses separately
        // cannot see that; only the grouped literal can.
        Assert.Contains(
            "(assignee = currentUser() OR status IN (\"Afventer general\"))", jira.LastJql);
    }

    /// <summary>
    /// Off duty the query must be what slice 11 sent. An empty IN list is not merely pointless —
    /// `status IN ()` is a JQL syntax error, so a naive implementation fails against the real instance
    /// while the fake answers anything. That is why this asserts on the string rather than the answer.
    /// </summary>
    [Fact]
    public async Task Off_duty_the_query_is_unchanged()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general"], onDuty: false).FetchAssignedAsync();

        Assert.DoesNotContain("status IN", jira.LastJql);
        Assert.DoesNotContain(" OR ", jira.LastJql);
    }

    [Fact]
    public async Task An_empty_duty_list_adds_no_clause_even_on_duty()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: [], onDuty: true).FetchAssignedAsync();

        Assert.DoesNotContain("status IN", jira.LastJql);
    }

    [Fact]
    public async Task Two_duty_statuses_are_both_in_the_clause()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general", "Venter på support"], onDuty: true)
            .FetchAssignedAsync();

        Assert.Contains("\"Afventer general\", \"Venter på support\"", jira.LastJql);
    }

    /// <summary>
    /// A stored blank would become <c>status IN (" ")</c>, which is syntactically valid JQL matching
    /// nothing — a silent failure, unlike the syntax error an empty IN list gives. The source filters
    /// rather than trusting PUT /api/settings: a row written before that validation existed outlives
    /// it, and the source is what builds the query.
    /// </summary>
    [Fact]
    public async Task A_blank_duty_status_is_left_out_of_the_clause()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["Afventer general", "  "], onDuty: true)
            .FetchAssignedAsync();

        // The closing bracket is what carries this: unfiltered, the clause would read
        // status IN ("Afventer general", "  ") and this substring would not be in it.
        Assert.Contains("status IN (\"Afventer general\")", jira.LastJql);
    }

    /// <summary>
    /// Only a list that is nothing but blanks can show that the filtering happens before the
    /// emptiness check rather than after it. Filtered afterwards, this would emit
    /// <c>status IN ()</c> and fail against the real instance on every import.
    ///
    /// Not a duplicate of the test above, and that is measured rather than assumed: moving the filter
    /// to after the emptiness check fells this one and leaves the other green, while removing the
    /// filter altogether fells both. The two catch different faults — "blanks reach the clause" and
    /// "filtered too late" — so merging them because they look alike would leave the second unguarded.
    /// </summary>
    [Fact]
    public async Task A_duty_list_of_only_blanks_behaves_like_an_empty_one()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["  ", ""], onDuty: true).FetchAssignedAsync();

        Assert.DoesNotContain("status IN", jira.LastJql);
        Assert.DoesNotContain(" OR ", jira.LastJql);
    }

    /// <summary>
    /// The same fault as the blank name, and closed in the same place: <c>"  Afventer general  "</c>
    /// becomes <c>status IN ("  Afventer general  ")</c>, which is valid JQL matching nothing, so it
    /// fails silently. Closing only the blank would leave the class looking closed while half of it
    /// was open.
    ///
    /// Hangs on the same mechanic as the blank test: <c>status IN ("Afventer general")</c> is not a
    /// substring of the untrimmed clause, so this has teeth rather than passing on a prefix.
    /// </summary>
    [Fact]
    public async Task A_duty_status_is_trimmed_before_it_reaches_the_clause()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS", duty: ["  Afventer general  "], onDuty: true)
            .FetchAssignedAsync();

        Assert.Contains("status IN (\"Afventer general\")", jira.LastJql);
    }

    /// <summary>
    /// A status name comes from a setting, and a setting is user input. JQL has quotes, so a name
    /// carrying one could change the query's meaning. The picker only ever offers instance names, so
    /// this cannot happen by accident — which is exactly why it needs a test rather than trust.
    /// </summary>
    [Fact]
    public async Task A_status_name_with_a_quote_is_refused()
    {
        await using var jira = await FakeJira.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS", duty: ["Af\"venter"], onDuty: true).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.JiraStatusNameInvalid, exception.Code);
        Assert.Empty(jira.SearchRequests);
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

    /// <summary>
    /// Reconciling in one answer, and the reason the whole feature exists. Both issues come back from
    /// the per-key reads, because reading an issue by key is independent of who it is assigned to,
    /// and only one comes back from the ownership search, because that one asks currentUser().
    ///
    /// Both halves are asserted because they fail differently: Found says the source can still see an
    /// issue the fetch has lost, StillMine says it can tell the two apart. A source that answered
    /// every key it was handed as still mine would pass on the count alone.
    ///
    /// The Single is what carries StillMine's extent rather than a tidier DoesNotContain: Contains
    /// and DoesNotContain together say nothing about what else is in the set, which is the hole two
    /// mutations found on the Azure DevOps side of this pair.
    /// </summary>
    [Fact]
    public async Task Reconciling_finds_an_issue_that_is_no_longer_assigned_to_me()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor("SAAS")
            .ReconcileAsync([FakeJira.HandedOverKey, FakeJira.MineKey]);

        Assert.Equal(2, answer.Found.Count);
        Assert.Equal(FakeJira.MineKey, Assert.Single(answer.StillMine));
    }

    /// <summary>
    /// The other thing the fetch drops. <c>resolution = Unresolved</c> is in the fetch's JQL and not
    /// in the reconciliation's, so a resolved issue is invisible to one and ordinary to the other.
    ///
    /// The fetch is run here as well rather than the exclusion being taken on trust: "the fetch
    /// cannot see it" is half of what this measures, and the read carrying the status back is the
    /// other half, because that status is what will let JiraStatusRoles call the issue done.
    ///
    /// <b>The third assertion is the one with teeth</b>, and it was missing until a review measured
    /// its absence: a resolved issue that is <em>still yours</em> has to stay in StillMine, because
    /// the four outcomes read "found and mine" as resolved and "found and not mine" as handed over.
    /// Add <c>AND resolution = Unresolved</c> to the ownership JQL and, without this line, the whole
    /// suite stays green while every resolved-but-still-mine issue is reported as somebody else's -
    /// a wrong suggestion rather than a missing one, which is the harder kind to notice.
    /// </summary>
    [Fact]
    public async Task Reconciling_finds_a_resolved_issue_the_fetch_cannot_see()
    {
        await using var jira = await FakeJira.StartAsync();
        var source = jira.SourceFor("SAAS");

        var page = await source.FetchAssignedAsync();
        var answer = await source.ReconcileAsync([FakeJira.ResolvedKey]);

        Assert.DoesNotContain(FakeJira.ResolvedKey, page.Items.Select(item => item.Key));
        Assert.Equal("Løst", Assert.Single(answer.Found).StatusName);
        Assert.Equal(FakeJira.ResolvedKey, Assert.Single(answer.StillMine));
    }

    /// <summary>
    /// The pool is what the duty rotation is for: a status in the duty list brings in an issue that
    /// is <em>not</em> assigned to you, which is the one thing the widened query does that the
    /// ordinary one cannot. Both directions are asserted from the same fixture, so this says the
    /// switch decides it rather than that the issue is always there.
    ///
    /// It lives here rather than beside the JQL tests because it measures the answer instead of the
    /// query text, and those are two different guards on the same clause: the grouped literal in
    /// <c>On_duty_the_query_also_asks_for_the_pool</c> holds JQL's precedence, and this holds that a
    /// foreign-owned issue reaches the answer at all. Measured to be independent - dropping the
    /// parentheses from JqlFor fells that one and leaves this green, because FakeJira reads clauses
    /// where they appear rather than parsing precedence.
    /// </summary>
    [Fact]
    public async Task On_duty_the_pool_brings_in_an_issue_that_is_not_mine()
    {
        await using var jira = await FakeJira.StartAsync();

        var onDuty = await jira.SourceFor("SAAS", duty: ["I gang"], onDuty: true).FetchAssignedAsync();
        var offDuty = await jira.SourceFor("SAAS", duty: ["I gang"], onDuty: false)
            .FetchAssignedAsync();

        Assert.Contains(FakeJira.HandedOverKey, onDuty.Items.Select(item => item.Key));
        Assert.DoesNotContain(FakeJira.HandedOverKey, offDuty.Items.Select(item => item.Key));
    }

    /// <summary>
    /// The deleted issue is one of the legal outcomes rather than a failure, and 404 is how Jira says
    /// so - which is why <c>GetOrNullAsync</c> exists beside GetAsync instead of a flag on it.
    ///
    /// Three assertions, and the third is what makes the name true: without it a source that never
    /// asked about the second key at all would pass, because "not in Found and not in StillMine" is
    /// also what silence looks like.
    ///
    /// The middle one cannot fail on its own and is not pretending to. StillMine is built by
    /// filtering Found's own keys, so a key absent from Found is absent from StillMine by
    /// construction - what the line does is spell out the two-set reading at the place a reader meets
    /// it. Cheap, and honest as long as nobody counts it as a guard.
    /// </summary>
    [Fact]
    public async Task A_deleted_issue_is_asked_for_by_key_and_answers_404()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor("SAAS")
            .ReconcileAsync([FakeJira.MineKey, FakeJira.DeletedKey]);

        Assert.Equal(FakeJira.MineKey, Assert.Single(answer.Found).Key);
        Assert.DoesNotContain(FakeJira.DeletedKey, answer.StillMine);
        Assert.Contains(FakeJira.DeletedKey, jira.IssueRequests);
    }

    /// <summary>
    /// The line 404 must not be read on the wrong side of. A refusal is not an absence: absence means
    /// "the source does not know it", which the app answers by offering to close the task, so
    /// swallowing every error the way a 404 is swallowed would turn one expired permission into an
    /// invitation to close everything it covered.
    ///
    /// This is the assertion that separates <c>GetOrNullAsync</c> from a method answering null for
    /// every failure, and seeing that line takes a status which is neither 200 nor 404 - which a key
    /// the token may read can never produce, however many tests read one. Seen to fail on exactly
    /// that mutation, <c>answer.Success ? answer.Body : null</c>, where the reconciliation answers an
    /// empty result and nothing is thrown at all.
    /// </summary>
    [Fact]
    public async Task A_forbidden_issue_is_a_failure_rather_than_an_absence()
    {
        await using var jira = await FakeJira.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => jira.SourceFor("SAAS").ReconcileAsync([FakeJira.ForbiddenKey]));

        // The code alone would be satisfied by any refusal at all, 404 excepted, so the status is
        // asserted too: this is about where the 404 line falls, not about errors reaching the user.
        Assert.Equal(ErrorCodes.JiraRefused, exception.Code);
        Assert.Contains("403", exception.Message);
    }

    /// <summary>
    /// Reconciling asks for no project key, and this is what keeps that a decision rather than an
    /// accident: the fetch refuses without one, the guard is one call away in ProjectKeyOf, and
    /// adding it here is the natural thing for someone tidying the two paths into a shape. Measured
    /// before it was written - <c>ProjectKeyOf(settings);</c> at the top of ReconcileAsync leaves the
    /// whole suite green, because every other test in this file configures a project key.
    ///
    /// The reason it must not be added is slice 11's own lesson read the right way round: the fetch
    /// needs a project because without one its JQL asks for everything the token can see, and here
    /// the key list is already the limit. A refusal with nothing behind it is still a refusal.
    ///
    /// No harm is claimed beyond that, because none can be reached today: the preview endpoint calls
    /// FetchAssignedAsync first, which refuses through JqlFor's ProjectKeyOf before reconciling is
    /// ever entered, so a user without a project key never gets this far either way. What this holds
    /// is the principle, against the day a caller reaches the reconciliation on its own.
    /// </summary>
    [Fact]
    public async Task Reconciling_asks_for_no_project_key()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor(projectKey: "  ").ReconcileAsync([FakeJira.MineKey]);

        Assert.Equal(FakeJira.MineKey, Assert.Single(answer.Found).Key);
        Assert.Equal(FakeJira.MineKey, Assert.Single(answer.StillMine));
    }

    /// <summary>
    /// The empty set is the ordinary state: when the fetch brought everything back, there is nothing
    /// left to reconcile.
    ///
    /// Two different halves keep the network quiet here, and only one of them is a guard. No read
    /// goes out because a loop over an empty list makes no call, which is construction rather than a
    /// decision - so a <c>keys.Count</c> guard like the Azure DevOps sibling's would be a line no
    /// test here could fell, and there is none. No search goes out because Found is then empty, which
    /// <em>is</em> a guard: seen to fail on deleting it, together with
    /// <c>Nothing_found_asks_no_ownership_question</c>, both on an ownership search that went out
    /// naming <c>key IN ()</c>.
    ///
    /// This note said the opposite until that mutation was run - that nothing here could see a guard
    /// at all. It read the two halves as one and was wrong about the half that matters.
    ///
    /// <c>Assert.Empty(answer.StillMine)</c> follows from the line above it, here and in
    /// <c>Nothing_found_asks_no_ownership_question</c>: StillMine is filtered out of Found's own
    /// keys, so an empty Found cannot leave anything in it. It is here to read, not to catch.
    /// </summary>
    [Fact]
    public async Task No_keys_is_no_request()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor("SAAS").ReconcileAsync([]);

        Assert.Empty(answer.Found);
        Assert.Empty(answer.StillMine);
        Assert.Empty(jira.IssueRequests);
        Assert.Empty(jira.SearchRequests);
    }

    /// <summary>
    /// N reads for N keys, which is the price of the per-key shape and therefore the thing that must
    /// actually happen. A loop that stopped after the first key fails worse here than on the fetch
    /// side: a key nobody asked about lands in neither set, which ExternalReconciliation defines as
    /// "the source does not know it", so the app would offer to close a live task for every key past
    /// the first.
    ///
    /// The request list carries it rather than the count of Found, because the list says the calls
    /// went out in the order the keys arrived - and Found could in principle be filled from anywhere.
    ///
    /// Seen to fail under <c>keys.Take(1)</c>, which fells three others for reasons worth knowing
    /// apart: <c>Reconciling_finds_an_issue_that_is_no_longer_assigned_to_me</c> because Found then
    /// holds one row of two, <c>A_deleted_issue_is_asked_for_by_key_and_answers_404</c> because the
    /// deleted key is the second one and is never asked about, and
    /// <c>Every_page_of_the_ownership_search_is_read_rather_than_only_the_first</c> because two of its
    /// three keys never reach the search. What survives says as much as what falls: a test handing in
    /// a single key cannot see this mutation at all, which is why the two that do are written with
    /// more than one.
    /// </summary>
    [Fact]
    public async Task Every_key_is_asked_for_rather_than_only_the_first()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor("SAAS").ReconcileAsync(
            [FakeJira.MineKey, FakeJira.HandedOverKey, FakeJira.ResolvedKey]);

        Assert.Equal(
            [FakeJira.MineKey, FakeJira.HandedOverKey, FakeJira.ResolvedKey], jira.IssueRequests);
        Assert.Equal(3, answer.Found.Count);
    }

    /// <summary>
    /// The whole reason Jira's shape is not Azure DevOps'. A key IN list naming a key Jira does not
    /// know is a 400 for the entire query, so a reconciliation built as one search would die on the
    /// deleted issue - the very case it exists to report. Only the keys that just answered 200 may go
    /// in, and this is where that is visible: the fake answers whatever it is asked, so nothing in
    /// the answer could show it.
    ///
    /// FakeJira does refuse an unknown key in a key IN list, so building the JQL out of every key
    /// fells this on the exception rather than on the assertion - and takes
    /// <c>A_deleted_issue_is_asked_for_by_key_and_answers_404</c> with it, because a throw is not one
    /// of that test's outcomes either. Seen to fail exactly that way:
    /// <c>SourceException : Jira answered 400 for search: An issue with key 'SAAS-7' does not exist
    /// for field 'key'.</c>
    ///
    /// Both refusals are the design's assumption about the real instance rather than a measurement of
    /// it; FakeJira's search route says so where it is written, and so does ReconcileAsync.
    /// </summary>
    [Fact]
    public async Task The_ownership_search_never_names_a_key_that_was_not_found()
    {
        await using var jira = await FakeJira.StartAsync();

        await jira.SourceFor("SAAS").ReconcileAsync([FakeJira.MineKey, FakeJira.DeletedKey]);

        Assert.Single(jira.SearchRequests);
        Assert.Contains(FakeJira.MineKey, jira.LastJql);
        Assert.DoesNotContain(FakeJira.DeletedKey, jira.LastJql);
        Assert.Contains("assignee = currentUser()", jira.LastJql);
    }

    /// <summary>
    /// The edge of the same rule. With nothing found there is no key left to name, and
    /// <c>key IN ()</c> is a JQL syntax error - the same empty-IN fault the duty clause has its own
    /// guard against, one clause over.
    ///
    /// The last assertion is what stops this passing for the wrong reason: without it, a source that
    /// never asked about anything would satisfy every other line here.
    ///
    /// Seen to fail on deleting the source's empty-Found guard, together with
    /// <c>No_keys_is_no_request</c>: the search then goes out carrying <c>key IN ()</c>. Note what
    /// the fake does <em>not</em> do with that - an empty IN list names no unknown key, so it is
    /// answered rather than refused, and only the request having been made at all shows it. Against
    /// the real instance it is a syntax error.
    /// </summary>
    [Fact]
    public async Task Nothing_found_asks_no_ownership_question()
    {
        await using var jira = await FakeJira.StartAsync();

        var answer = await jira.SourceFor("SAAS").ReconcileAsync([FakeJira.DeletedKey]);

        Assert.Empty(answer.Found);
        Assert.Empty(answer.StillMine);
        Assert.Empty(jira.SearchRequests);
        Assert.Equal([FakeJira.DeletedKey], jira.IssueRequests);
    }

    /// <summary>
    /// Both sets answer in the spelling the caller used, and seeing that takes a key spelled
    /// differently from Jira's own - which is what this hands in and what nothing else has reason to.
    /// Jira canonicalises a key's case - ask for saas-1 and SAAS-1 comes back - so a source building
    /// either set out of the response's keys hands back a set the caller's own Contains cannot find.
    /// It fails silently and it fails in the worst direction: every reconciled issue would read as
    /// one the source does not know, which is the outcome that offers to close the task.
    ///
    /// Both sets are asserted because they are filled by different code: Found by the per-key read,
    /// StillMine by the search. The title is asserted too, so this cannot pass on a source that
    /// simply echoed the key it was handed without ever resolving it.
    ///
    /// What it rests on is FakeJira answering the issue's own spelling; that line is marked in the
    /// fake as load-bearing for this test, because removing it would leave both spellings identical
    /// and every assertion here true of nothing.
    ///
    /// Seen to fail by handing the search's own answer back as StillMine, which is the shorter way to
    /// write it: <c>Assert.Equal() Failure: Strings differ / Expected: "saas-1" / Actual: "SAAS-1"</c>.
    /// </summary>
    [Fact]
    public async Task The_sets_answer_with_the_key_the_caller_handed_in()
    {
        await using var jira = await FakeJira.StartAsync();
        var asked = FakeJira.MineKey.ToLowerInvariant();

        var answer = await jira.SourceFor("SAAS").ReconcileAsync([asked]);

        var found = Assert.Single(answer.Found);

        Assert.Equal(asked, found.Key);
        Assert.Equal("Kunden kan ikke logge ind", found.Title);
        Assert.Equal(asked, Assert.Single(answer.StillMine));
    }

    /// <summary>
    /// A reconciled row carries no deadline, and that is the contract's rule rather than each
    /// source's: the row is never imported as new work, so a suggested deadline is noise on a row
    /// whose tick box means "close this one". Azure DevOps passes null because it has no due date to
    /// carry at all; Jira has one and has to clear it, or the two sources would answer differently
    /// with nothing saying which was meant.
    ///
    /// The fetched row is asserted first, and that is the tooth: without it, a fixture that simply
    /// had no due date would pass against an implementation that clears nothing.
    ///
    /// Seen to fail by dropping <c>Deadline = null</c> from the reconciled row, which is how this is
    /// naturally got wrong: Map fills the field from duedate already, so writing nothing is what
    /// inherits it.
    /// </summary>
    [Fact]
    public async Task A_reconciled_row_carries_no_deadline()
    {
        await using var jira = await FakeJira.StartAsync();
        var source = jira.SourceFor("SAAS");

        var fetched = Assert.Single(
            (await source.FetchAssignedAsync()).Items, item => item.Key == FakeJira.MineKey);
        var reconciled = Assert.Single((await source.ReconcileAsync([FakeJira.MineKey])).Found);

        Assert.NotNull(fetched.Deadline);
        Assert.Null(reconciled.Deadline);
    }

    /// <summary>
    /// One JQL, but not necessarily one page. Jira caps maxResults at its own limit and says what it
    /// used, so a search that read the first page only would answer a short list of owners - and
    /// every key past it would land in Found without landing in StillMine, which is the app's word
    /// for "handed to somebody else". A silent offer to close live tasks, again, from the opposite
    /// direction to the per-key loop.
    ///
    /// Three keys against a page size of two is two rounds, and the second is the one an unpaged read
    /// never makes. It goes through the same pager as FetchAssignedAsync, so what this adds to
    /// <c>Every_page_is_read_rather_than_only_the_first</c> is that the ownership search uses it -
    /// and the two fall together when the pager stops after one page, which is what a shared rule
    /// looks like from a mutation. What only this one can see is the ownership search leaving that
    /// pager for a single unpaged read of its own.
    /// </summary>
    [Fact]
    public async Task Every_page_of_the_ownership_search_is_read_rather_than_only_the_first()
    {
        await using var jira = await FakeJira.StartAsync(pageSize: 2);

        var answer = await jira.SourceFor("SAAS")
            .ReconcileAsync([FakeJira.MineKey, "SAAS-2", "SAAS-3"]);

        Assert.Equal(3, answer.StillMine.Count);
        Assert.Equal(2, jira.SearchRequests.Count);
    }
}
