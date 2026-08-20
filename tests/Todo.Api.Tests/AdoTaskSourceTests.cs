using System.Text;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.TestSupport.Ado;

namespace Todo.Api.Tests;

/// <summary>
/// ITaskSource's second implementation, measured over real HTTP against FakeAdo on loopback - which is
/// why these are Api tests rather than Core ones, exactly as JiraTaskSourceTests are. What a stub could
/// not see is the whole of what is being measured here: the Authorization header, the URL, the WIQL text
/// and the JSON binding.
/// </summary>
public class AdoTaskSourceTests
{
    [Fact]
    public async Task Testing_the_connection_answers_with_the_display_name()
    {
        await using var ado = await FakeAdo.StartAsync();

        var identity = await ado.SourceFor().TestAsync();

        Assert.Equal(FakeAdo.Owner, identity.DisplayName);
    }

    /// <summary>
    /// The PAT goes in as Basic auth with an <em>empty user name</em>, which is the form measured
    /// against the real instance on 2026-08-20: base64(":" + PAT) answers 200. Bearer is Jira's form
    /// and is equally plausible from the outside, so this pins which one - and it decodes the parameter
    /// rather than comparing base64, because a base64 blob compared against a base64 blob would pass
    /// for any encoding of anything.
    /// </summary>
    [Fact]
    public async Task The_token_is_sent_as_basic_auth_with_an_empty_user_name()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor().TestAsync();

        Assert.Equal("Basic", ado.LastAuthorizationScheme);
        Assert.Equal(
            $":{FakeAdo.Token}",
            Encoding.ASCII.GetString(Convert.FromBase64String(ado.LastAuthorizationParameter!)));
    }

    [Fact]
    public async Task A_refused_token_becomes_a_source_exception_rather_than_a_crash()
    {
        await using var ado = await FakeAdo.StartAsync(rejectToken: true);

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor().TestAsync());

        Assert.Equal(ErrorCodes.AdoRefused, exception.Code);
    }

    /// <summary>
    /// Azure DevOps puts its reason in a single <c>message</c> string where Jira uses an
    /// <c>errorMessages</c> array, so the two sources cannot share the reading of it. Without this the
    /// user would get a bare status code for every refusal.
    /// </summary>
    [Fact]
    public async Task A_refusal_carries_the_words_azure_devops_used()
    {
        await using var ado = await FakeAdo.StartAsync(rejectToken: true);

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor().TestAsync());

        Assert.Contains("401", exception.Message);
        Assert.Contains("TF400813", exception.Message);
    }

    [Fact]
    public async Task An_unreachable_host_becomes_a_source_exception()
    {
        await using var ado = await FakeAdo.StartAsync();
        await ado.StopServerAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor().TestAsync());

        Assert.Equal(ErrorCodes.AdoUnreachable, exception.Code);
    }

    /// <summary>
    /// The collection and the project are how Azure DevOps scopes a WIQL - measured, the query was
    /// posted to <c>{collection}/{project}/_apis/wit/wiql</c> - so this is the counterpart of Jira's
    /// "the query is narrowed to the configured project". There the narrowing was a JQL clause, here it
    /// is the path, so this is where it has to be asserted.
    /// </summary>
    [Fact]
    public async Task The_collection_and_the_project_are_both_in_the_url()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            $"/{FakeAdo.Collection}/{FakeAdo.Project}/_apis/wit/wiql",
            Assert.Single(ado.WiqlPaths));
    }

    /// <summary>
    /// The measured collection name has a space in it, and the plan lists three layers that could turn
    /// <c>%20</c> back into one. Asserted on the raw request line, because that is the only place the
    /// escaping is still visible - ASP.NET has already decoded HttpRequest.Path.
    ///
    /// Honest about its own reach, because that was measured rather than assumed. Rebuilding UriFor
    /// around UriBuilder leaves this green - UriBuilder preserves <c>%20</c> in a path on net10.0 -
    /// and so does interpolating the base Uri, whose ToString does un-escape, because <c>new Uri</c>
    /// re-escapes the literal space straight back. The one fault this can see is <b>double</b>
    /// escaping, <c>Fake%2520Collection</c>, which is what escaping an already-escaped URL produces,
    /// and it was seen to fail on exactly that. Labelled rather than presented as a guard over the
    /// whole class.
    /// </summary>
    [Fact]
    public async Task The_space_in_the_collection_name_stays_escaped_on_the_wire()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor().FetchAssignedAsync();

        Assert.Contains("Fake%20Collection", ado.LastRawTarget);
        Assert.DoesNotContain("%2520", ado.LastRawTarget);
    }

    /// <summary>
    /// A blank project is the source's own refusal, with its own code, and it happens <em>before</em>
    /// the request - the empty WiqlRequests list is what says so. Without that half the user would get
    /// whatever a server answers for a URL with an empty path segment, which is a 404 blamed on the
    /// instance.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_project_is_refused_before_the_call(string? project)
    {
        await using var ado = await FakeAdo.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor(project: project).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.AdoProjectRequired, exception.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    /// <summary>
    /// The query itself, which the fake would happily answer in any shape - the same reason the Jira
    /// tests assert on LastJql. @Me was the last blocking assumption in the design and is measured;
    /// Closed is excluded by name because the state vocabulary differs per work item type on this
    /// instance and Closed is the one name all of them share.
    /// </summary>
    [Fact]
    public async Task The_query_asks_for_the_users_own_open_work_items()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor().FetchAssignedAsync();

        Assert.Contains("[System.AssignedTo] = @Me", ado.LastWiql);
        Assert.Contains("[System.State] <> 'Closed'", ado.LastWiql);
    }

    /// <summary>
    /// Decision B. Two of the twelve measured work items were test artefacts - a Test Plan and a Test
    /// Suite - so the filter is what keeps 17% noise out. Both halves are asserted because they fail
    /// differently: the clause is what the source asked for, and the absent Test Suite is what came
    /// back.
    /// </summary>
    [Fact]
    public async Task The_query_filters_on_work_item_type()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Contains(
            "[System.WorkItemType] IN ('Bug', 'User Story', 'Task')", ado.LastWiql);
        Assert.DoesNotContain("Test Suite", page.Items.Select(item => item.ItemType));
        Assert.DoesNotContain("17169", page.Items.Select(item => item.Key));
    }

    /// <summary>
    /// The opposite of Jira's duty clause, and deliberately so. There an empty list means "add no
    /// clause", because duty is an optional widening; here the list <em>is</em> the limit, so emptiness
    /// has to be a refusal or the import would ask for every type - which is the trap slice 11 measured
    /// on the empty project key, that the absence of a limit is not a neutral default. The plan's first
    /// answer said an empty list meant every type; task 2 reversed it, and this is where that reversal
    /// is enforced.
    /// </summary>
    [Fact]
    public async Task An_empty_type_filter_is_refused_before_the_call()
    {
        await using var ado = await FakeAdo.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor(workItemTypes: []).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.AdoWorkItemTypesRequired, exception.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    /// <summary>
    /// A list of nothing but blanks has to behave like an empty one, and only a list like this can show
    /// that the filtering happens before the emptiness check rather than after it. Filtered afterwards,
    /// this would emit <c>IN ()</c> - a WIQL syntax error against the real server on every import,
    /// while a fake answers whatever it is asked.
    /// </summary>
    [Fact]
    public async Task A_type_filter_of_only_blanks_is_refused_too()
    {
        await using var ado = await FakeAdo.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor(workItemTypes: ["  ", ""]).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.AdoWorkItemTypesRequired, exception.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    /// <summary>
    /// A stored blank alongside a real name would become <c>IN ('Bug', ' ')</c>, valid WIQL matching
    /// nothing extra but hiding that the setting is broken; an untrimmed name would become
    /// <c>IN ('  Bug  ')</c>, valid WIQL matching <em>nothing at all</em> - a silent empty import. The
    /// closing bracket is what gives this teeth: neither faulty form contains this substring.
    /// </summary>
    [Fact]
    public async Task A_type_name_is_trimmed_and_a_blank_one_is_dropped()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor(workItemTypes: ["  Bug  ", "   "]).FetchAssignedAsync();

        Assert.Contains("[System.WorkItemType] IN ('Bug')", ado.LastWiql);
    }

    /// <summary>
    /// A work item type comes from a setting, and a setting is user input. WIQL literals are
    /// single-quoted, so a name carrying a quote could change the query's meaning. The list is only ever
    /// the app's own defaults or names the instance reported, so this cannot happen by accident - which
    /// is exactly why it needs a test rather than trust.
    /// </summary>
    [Theory]
    [InlineData("Bu'g")]
    [InlineData("Bu\"g")]
    [InlineData("Bu\\g")]
    public async Task A_type_name_with_a_quote_or_a_backslash_is_refused(string name)
    {
        await using var ado = await FakeAdo.StartAsync();

        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor(workItemTypes: [name]).FetchAssignedAsync());

        Assert.Equal(ErrorCodes.AdoWorkItemTypeInvalid, exception.Code);
        Assert.Empty(ado.WiqlRequests);
    }

    [Fact]
    public async Task A_work_item_maps_field_by_field()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        var item = Assert.Single(page.Items, i => i.Key == "16901");

        Assert.Equal("Som bruger vil jeg kunne filtrere", item.Title);
        Assert.Equal("Active", item.StatusName);
        Assert.Equal("User Story", item.ItemType);
        Assert.Equal("Bo Bertelsen", item.Requester);
        Assert.Equal("<div>Som bruger vil jeg</div>", item.Note);
        Assert.Equal(new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc), item.StatusChangedAt);
    }

    [Fact]
    public async Task A_work_item_with_every_optional_field_absent_still_maps()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        var item = Assert.Single(page.Items, i => i.Key == "17170");

        Assert.Equal("Uden noget som helst", item.Title);
        Assert.Null(item.Note);
        Assert.Null(item.Requester);
        Assert.Null(item.StatusChangedAt);
    }

    /// <summary>
    /// The measured shape: work item 15664 is a Bug and carries Microsoft.VSTS.TCM.ReproSteps but not
    /// System.Description. CLAUDE.md's duedate lesson in a worse form - not one wrong name but several
    /// right ones - so reading the note from a single field would leave every Bug's note empty.
    /// </summary>
    [Fact]
    public async Task A_bug_takes_its_note_from_the_repro_steps()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            "<div>Trin 1: log ind</div>",
            Assert.Single(page.Items, i => i.Key == "15664").Note);
    }

    /// <summary>
    /// The test the fallback would otherwise hide, and the reason FakeAdo has a Bug carrying both note
    /// fields. With a fallback in place, a Bug that has only repro steps reads correctly even from a
    /// source that asked for System.Description first - so nothing measured could tell the priority
    /// from the fallback. Only a work item holding both, with different text, can.
    /// </summary>
    [Fact]
    public async Task A_bug_carrying_both_note_fields_prefers_the_repro_steps()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            "<div>Reproduktionen</div>",
            Assert.Single(page.Items, i => i.Key == "17165").Note);
    }

    /// <summary>
    /// System.CreatedBy has two wire shapes across Azure DevOps versions - an object with displayName,
    /// or the older <c>Name &lt;address&gt;</c> string - and measurement 0b printed field names only,
    /// so which one this server sends is unknown. Both are read rather than one being guessed, because
    /// guessing wrong gives null in every requester without a test falling, exactly as Jira's duedate
    /// did. The address is dropped: a requester is shown to a person, not mailed to.
    /// </summary>
    [Fact]
    public async Task An_identity_sent_as_a_string_maps_to_the_name_without_the_address()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            "Citronella Clausen",
            Assert.Single(page.Items, i => i.Key == "17165").Requester);
    }

    /// <summary>
    /// Decision A: Azure DevOps has no due date field at all, so the app derives one from the clock and
    /// the setting. Every row gets the same date, because it is a function of today rather than of the
    /// work item.
    /// </summary>
    [Fact]
    public async Task Every_row_carries_the_derived_deadline()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor(today: new DateOnly(2026, 8, 20)).FetchAssignedAsync();

        Assert.All(page.Items, item => Assert.Equal(new DateOnly(2026, 8, 23), item.Deadline));
    }

    /// <summary>
    /// Zero days means no deadline, and this is the end-to-end half of AdoDeadlineTests: the rule being
    /// right in Core is worth nothing if the source never consults it.
    /// </summary>
    [Fact]
    public async Task Zero_days_leaves_every_row_without_a_deadline()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor(defaultDeadlineDays: 0).FetchAssignedAsync();

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Null(item.Deadline));
    }

    /// <summary>
    /// The measured advantage over Jira, and the one that changed the interface.
    /// Microsoft.VSTS.Common.StateChangeDate arrives with the work item, where Jira DC 10.3.24 has no
    /// such field and needs a changelog call per issue. The empty WorkItemRequests list is the half that
    /// matters: it says the page cost no extra round trips.
    /// </summary>
    [Fact]
    public async Task The_state_change_date_arrives_with_the_page_and_costs_no_extra_call()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            new DateTime(2026, 8, 17, 14, 10, 13, 593, DateTimeKind.Utc),
            Assert.Single(page.Items, i => i.Key == "15664").StatusChangedAt);
        Assert.Empty(ado.WorkItemRequests);
    }

    /// <summary>
    /// Bound as a string and parsed rather than typed as a DateTimeOffset on the DTO. Azure DevOps
    /// writes <c>Z</c>, which System.Text.Json would actually bind - unlike Jira's <c>+0200</c> - so
    /// this is not the same trap; it is the same shape for a different reason. A typed field throws for
    /// the <em>whole page</em> on one odd value, and this is the assertion that says so: one unreadable
    /// timestamp costs that row its date and nothing else.
    /// </summary>
    [Fact]
    public async Task An_unreadable_state_change_date_costs_one_row_rather_than_the_page()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Null(Assert.Single(page.Items, i => i.Key == "17162").StatusChangedAt);
        Assert.Equal(5, page.Items.Count);
    }

    /// <summary>
    /// The fallback path on the interface. Azure DevOps never needs it - the field rides on the row -
    /// but a real read is what keeps the interface member honest: a stub returning null would be a lie
    /// nothing could see. The recorded request is what proves it actually went out.
    /// </summary>
    [Fact]
    public async Task The_state_change_date_can_also_be_read_for_one_work_item()
    {
        await using var ado = await FakeAdo.StartAsync();

        var changed = await ado.SourceFor().FetchStatusChangedAtAsync("15664");

        Assert.Equal(new DateTime(2026, 8, 17, 14, 10, 13, 593, DateTimeKind.Utc), changed);
        Assert.Equal(15664, Assert.Single(ado.WorkItemRequests));
    }

    /// <summary>
    /// An Azure DevOps key is a work item id. A Jira key handed to this source is a caller mixing up
    /// two sources, and asking the server for <c>/workitems/SAAS-1</c> would be a 400 the user would
    /// read as a problem with their instance. The empty request list is the assertion.
    /// </summary>
    [Fact]
    public async Task A_key_that_is_not_a_work_item_id_asks_nothing()
    {
        await using var ado = await FakeAdo.StartAsync();

        Assert.Null(await ado.SourceFor().FetchStatusChangedAtAsync("SAAS-1"));
        Assert.Empty(ado.WorkItemRequests);
    }

    /// <summary>
    /// Where Azure DevOps pages, which is not where Jira does. WIQL is not paged; the hydration is,
    /// because <c>?ids=</c> is capped and answers 400 above the cap rather than truncating. A source
    /// that read only the first chunk would import the newest few work items and look like it had
    /// finished - so both the chunk count and the item count are asserted, because a source that
    /// chunked but dropped the results would pass on the first alone.
    /// </summary>
    [Fact]
    public async Task Every_batch_is_read_rather_than_only_the_first()
    {
        // 250 filler work items on top of the five importable measured ones: 255 ids against a chunk
        // size of 200 is two reads, and the second one is the one a naive source never makes.
        await using var ado = await FakeAdo.StartAsync(filler: 250);

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(255, page.Total);
        Assert.Equal(255, page.Items.Count);
        Assert.Equal(2, ado.BatchRequests.Count);
    }

    /// <summary>
    /// The order comes from the WIQL's ORDER BY, and the batch is allowed to answer in any order it
    /// likes - FakeAdo answers in ascending id, as a real one may. Jira never had this problem, because
    /// its search returned the issues themselves. Without the restoration the user's list would be
    /// sorted by work item id, which is roughly creation order and not what anyone asked for.
    /// </summary>
    [Fact]
    public async Task The_order_the_query_asked_for_survives_the_batch()
    {
        await using var ado = await FakeAdo.StartAsync();

        var page = await ado.SourceFor().FetchAssignedAsync();

        Assert.Equal(
            ["17170", "15664", "17165", "16901", "17162"],
            page.Items.Select(item => item.Key));
    }

    /// <summary>
    /// The state names the user picks their waiting list from. Read off the user's own work items rather
    /// than off _apis/wit/workitemtypes, because that is the shape measurement 0c actually saw - see the
    /// note on the source. Deduplicated because two work items share a state, and sorted the Danish way
    /// for the same reason Jira's are.
    /// </summary>
    [Fact]
    public async Task The_state_names_come_back_sorted_and_without_duplicates()
    {
        await using var ado = await FakeAdo.StartAsync();

        var names = await ado.SourceFor().FetchStatusNamesAsync();

        // Active and active are both here, and that is the assertion rather than an accident: the
        // deduplication is ordinal, so a state name differing only in case is a different state - which
        // is the rule slice 11 measured for Jira statuses and the one the role decision in task 4 will
        // depend on. Their neighbouring order is measured, not chosen: da-DK collation sorts the
        // lowercase form immediately after the uppercase one.
        Assert.Equal(
            ["Active", "active", "Blocked", "In Progress", "New", "PO Review", "Ændret", "Åben"],
            names);
    }

    /// <summary>
    /// The state list is not the import filter, and that is the assertion. <c>In Progress</c> belongs to
    /// the Test Suite and <c>Ændret</c> to a Test Plan, both of which the type filter keeps out of an
    /// import - but a user still has to be able to name them as waiting, or the vocabulary would be a
    /// subset of what they are looking at. Filtering this query by type would fell exactly this.
    /// </summary>
    [Fact]
    public async Task The_state_list_is_not_narrowed_by_the_import_type_filter()
    {
        await using var ado = await FakeAdo.StartAsync();

        var names = await ado.SourceFor().FetchStatusNamesAsync();

        Assert.Contains("In Progress", names);
        Assert.Contains("Ændret", names);
        Assert.DoesNotContain("[System.WorkItemType] IN", ado.LastWiql);
    }

    /// <summary>
    /// The same test the Jira suite needed, and for the same reason: Danish collation and code points
    /// order most name pairs identically, so only a pair carrying Æ, Ø or Å can tell the comparers
    /// apart. Measured on net10.0 with ICU - da-DK gives Ændret then Åben, Ordinal gives Åben then
    /// Ændret, because Å is U+00C5 and Æ is U+00C6. A state name may legally carry Danish letters, so
    /// this is not hypothetical - and the assertion above would pass under either comparer without this
    /// pair, which is why the two Test Plan fixtures exist.
    /// </summary>
    [Fact]
    public async Task The_state_names_sort_the_danish_way_rather_than_by_code_point()
    {
        await using var ado = await FakeAdo.StartAsync();

        var names = await ado.SourceFor().FetchStatusNamesAsync();

        Assert.Equal(names.Count - 2, names.ToList().IndexOf("Ændret"));
        Assert.Equal(names.Count - 1, names.ToList().IndexOf("Åben"));
    }

    /// <summary>
    /// The batch's cap is a 400 with a sentence rather than a shorter list, measured 2026-08-20 on
    /// invalid ids. A source that asked for more than the cap would get an error the user cannot act on,
    /// so the chunk size has to be at or below it - and this is what says the fake enforces the same
    /// rule the real server does, which is what makes the paging test above mean anything.
    /// </summary>
    [Fact]
    public async Task Asking_for_more_ids_than_the_cap_is_a_refusal_rather_than_a_short_list()
    {
        await using var ado = await FakeAdo.StartAsync(batchLimit: 1);

        // The source chunks at 200, so with a cap of one the very first chunk is over it. That is the
        // fault this measures: a chunk size above the server's cap.
        var exception = await Assert.ThrowsAsync<SourceException>(
            () => ado.SourceFor().FetchAssignedAsync());

        Assert.Equal(ErrorCodes.AdoRefused, exception.Code);
        Assert.Contains("VS402337", exception.Message);
    }

    /// <summary>
    /// The defaults are what an absent settings row reads as, and this is where they meet the query.
    /// Without it, a source could hardcode its own list and every test above would still pass.
    /// </summary>
    [Fact]
    public async Task The_default_type_filter_is_the_one_the_settings_reader_would_have_given()
    {
        await using var ado = await FakeAdo.StartAsync();

        await ado.SourceFor().FetchAssignedAsync();

        foreach (var type in AdoDefaults.WorkItemTypes)
        {
            Assert.Contains($"'{type}'", ado.LastWiql);
        }
    }
}
