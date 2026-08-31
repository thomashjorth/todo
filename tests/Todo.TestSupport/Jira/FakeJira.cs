using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todo.Core.Jira;
using Todo.Host.Jira;

namespace Todo.TestSupport.Jira;

/// <summary>
/// A Jira on loopback, answering the routes <see cref="JiraTaskSource"/> asks for. Real HTTP on
/// purpose: a stubbed ITaskSource would skip the Authorization header, the query string and the
/// JSON reading, which is the whole of what can go wrong between here and a real instance.
///
/// The shapes it answers with were measured against the real Data Center 10.3.24 on 2026-08-18 —
/// REST v2, a Bearer PAT, and classic startAt/maxResults/total paging.
///
/// No host outside 127.0.0.1 is named anywhere in this file, and none may be: the app runs on the
/// user's machine and may have no network, and task 7 turns that into a guard over the whole repo.
/// </summary>
public sealed class FakeJira : IAsyncDisposable
{
    /// <summary>Not a credential. It exists so a test can assert which token was sent.</summary>
    public const string Token = "fake-pat";

    /// <summary>
    /// The ordinary issue: open, the user's own, and carrying a due date. Named rather than spelled
    /// per test because what matters about it is those three facts and not its number - and the due
    /// date is what lets a reconciled row show its deadline was cleared rather than never there.
    /// </summary>
    public const string MineKey = "SAAS-1";

    /// <summary>
    /// Assigned to somebody else, which is the case reconciling exists for at all: the fetch's
    /// <c>assignee = currentUser()</c> cannot see it, while a read by key can. Constructed rather than
    /// measured, and who it belongs to is this fake's own knowledge rather than a field - see
    /// <see cref="NotMine"/>.
    /// </summary>
    public const string HandedOverKey = "SAAS-5";

    /// <summary>
    /// The other thing the fetch cannot see, and still the user's own: an issue with a resolution,
    /// which <c>resolution = Unresolved</c> drops. Its status is one the default picker offers, so a
    /// caller can put it in the done list the way a user would.
    /// </summary>
    public const string ResolvedKey = "SAAS-6";

    /// <summary>
    /// A key no issue here has. The deleted issue, which the reconciliation exists to report rather
    /// than an error it may die on.
    /// </summary>
    public const string DeletedKey = "SAAS-7";

    /// <summary>
    /// A key that answers 403. The other side of the design's unmeasured assumption that a deleted
    /// issue answers 404: a refusal must not be read as an absence, because the app's answer to an
    /// absence is to offer to close the task.
    /// </summary>
    public const string ForbiddenKey = "SAAS-8";

    private const string Loopback = "http://127.0.0.1:0";
    private const string Owner = "Thomas";

    /// <summary>
    /// Explicit rather than inherited from the hosting defaults, so the property names this fake
    /// answers with are decided here and cannot drift with a framework default.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The issues this Jira holds. Three of them cover the mapping cases - everything filled in, a
    /// waiting status, and every optional field absent - and are the three the ordinary fetch
    /// answers, in the order its ORDER BY duedate leaves them.
    ///
    /// The last two are the reconciliation's pair, and neither can reach the <em>ordinary</em> fetch:
    /// one belongs to somebody else and one carries a resolution, so <see cref="Matches"/>'s assignee
    /// and resolution clauses are what keep them out. That is why they can live here rather than in a
    /// fixture of their own - and it turns the assertions that count what the fetch answered, in
    /// JiraTaskSourceTests.Every_page_is_read_rather_than_only_the_first and
    /// JiraEndpointsTests.The_preview_reports_the_total_the_source_gave, into a guard on those two
    /// clauses being honoured rather than arithmetic over this array.
    ///
    /// <b>SAAS-5 does reach a duty-widened one</b>, and that qualifier is measured rather than
    /// careful wording: its status is "I gang", so a caller that puts that status in the duty list
    /// while on duty gets it in the answer - which is the whole point of the pool, and which
    /// <c>On_duty_the_pool_brings_in_an_issue_that_is_not_mine</c> measures. TaskEndpointsTests'
    /// wire-format test is configured exactly that way, so this issue joined that preview the day it
    /// was added here. Nothing there counts rows, so nothing said so.
    /// </summary>
    private static readonly IssueBody[] Issues =
    [
        new(MineKey, new IssueFieldsBody(
            "Kunden kan ikke logge ind", "*vigtigt*", "2026-08-20",
            new PersonBody("Anna Andersen"), new StatusBody("I gang"))),
        new("SAAS-2", new IssueFieldsBody(
            "Venter på svar fra kunden", "h1. Sag", null,
            new PersonBody("Bo Bertelsen"), new StatusBody("Afventer general"))),
        new("SAAS-3", new IssueFieldsBody(
            "Uden noget som helst", null, null, null, new StatusBody("I gang"))),
        new(HandedOverKey, new IssueFieldsBody(
            "Overdraget til en anden", null, null,
            new PersonBody("Bo Bertelsen"), new StatusBody("I gang"))),
        new(ResolvedKey, new IssueFieldsBody(
            "Løst i kilden", null, null,
            new PersonBody("Anna Andersen"), new StatusBody("Løst"))),
    ];

    /// <summary>
    /// Who an issue belongs to and whether it has a resolution, held here rather than served as
    /// fields - and that is the real shape rather than a shortcut. <c>currentUser()</c> and
    /// <c>Unresolved</c> are resolved by the server, and <see cref="JiraTaskSource"/> asks only for
    /// summary, description, duedate, reporter and status, so neither fact ever reaches the wire for
    /// it to read. A fake that served them would invite a source to compare a name, which is the one
    /// thing the two-set answer exists to avoid.
    /// </summary>
    private static readonly HashSet<string> NotMine =
        new([HandedOverKey], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Resolved =
        new([ResolvedKey], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Keys the token may not read. Constructed, because nobody has put the question to the real
    /// instance: the design assumes a deleted issue answers 404, and this is what says a source may
    /// not treat every failure that way.
    /// </summary>
    private static readonly HashSet<string> Forbidden =
        new([ForbiddenKey], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The JQL clauses this fake evaluates. Each is read where the source writes it rather than
    /// recognised as a whole query, so one matcher serves the fetch, the duty widening and the
    /// reconciliation alike.
    ///
    /// <c>key IN (…)</c> is the reconciliation's own narrowing and holds bare keys where the duty
    /// clause holds quoted status names. The word boundary matters: without it <c>key</c> would also
    /// fire inside a longer word, and JQL has several.
    /// </summary>
    private static readonly Regex KeyClause = new(
        @"\bkey\s+IN\s+\(([^)]*)\)", RegexOptions.IgnoreCase);

    private static readonly Regex StatusClause = new(
        @"\bstatus\s+IN\s+\(([^)]*)\)", RegexOptions.IgnoreCase);

    private static readonly Regex StatusName = new("\"([^\"]*)\"");

    private static readonly Regex AssignedToMe = new(
        @"assignee\s*=\s*currentUser\(\)", RegexOptions.IgnoreCase);

    private static readonly Regex Unresolved = new(
        @"resolution\s*=\s*Unresolved", RegexOptions.IgnoreCase);

    /// <summary>
    /// One changelog per issue key. The created values are strings rather than DateTimeOffsets on
    /// purpose: measured 2026-08-18, Jira DC 10.3.24 writes the offset without a colon —
    /// 2026-08-17T14:10:13.593+0200 — while serialising a DateTimeOffset would emit +02:00, so a
    /// typed field here would measure the source against a format the real instance never sends.
    ///
    /// Histories arrive oldest first, as they do from the real instance, so a source that takes the
    /// last entry rather than the newest status entry cannot pass by accident.
    /// </summary>
    private static readonly Dictionary<string, HistoryBody[]> Changelogs =
        new(StringComparer.Ordinal)
        {
            // Two status changes and nothing else, ending in the status the issue shows now.
            ["SAAS-1"] =
            [
                new("2026-08-11T10:00:00.000+0200",
                    [new("status", "Ny SLA", "Afventer general")]),
                new("2026-08-12T13:45:00.000+0200",
                    [new("status", "Afventer general", "I gang")]),
            ],

            // The newest entry is deliberately not a status change: the wait dates from 14:10 on
            // the 17th, not from the assignee change the day after.
            ["SAAS-2"] =
            [
                new("2026-08-15T09:00:00.000+0200",
                    [new("status", "Ny SLA", "I gang")]),
                new("2026-08-17T14:10:13.593+0200",
                    [new("status", "I gang", "Afventer general")]),
                new("2026-08-18T08:00:00.000+0200",
                    [new("assignee", null, "thh")]),
            ],

            // An issue still in the status it was created in has no history at all.
            ["SAAS-3"] = [],

            // Exactly one entry, so the newest-wins ordering is measured on a list of one too.
            ["SAAS-4"] =
            [
                new("2026-08-10T11:30:00.000+0200",
                    [new("status", "Ny SLA", "Afventer general")]),
            ],

            // The reconciliation's pair. Neither issue reaches the ordinary fetch, so neither needed a
            // history until a reconciled row could suggest a closure - and a closure carries the
            // moment the source finished with it. Without these two the completion time would be null
            // whatever the endpoint did, so the assertion on it could not fail and would say nothing.
            //
            // SAAS-5 has one although it is the handed-over issue, whose closure carries no timestamp
            // at all: that is what gives the assertion its teeth. An implementation that fetched the
            // changelog for every closing row rather than only for the resolved ones would answer this
            // value instead of null.
            ["SAAS-5"] =
            [
                new("2026-08-14T08:05:00.000+0200",
                    [new("status", "Ny SLA", "I gang")]),
            ],

            ["SAAS-6"] =
            [
                new("2026-08-13T09:20:00.000+0200",
                    [new("status", "I gang", "Løst")]),
            ],
        };

    private readonly HttpClient _client;
    private readonly int _pageSize;
    private readonly bool _rejectToken;
    private readonly string[] _statusNames;

    private WebApplication? _app;

    private FakeJira(bool rejectToken, int pageSize, string[] statusNames)
    {
        _rejectToken = rejectToken;
        _pageSize = pageSize;
        _statusNames = statusNames;

        // Mirrors TodoHost's registration, so a source that hangs hangs the same way here.
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Survives <see cref="StopServerAsync"/>, so a caller can meet a Jira that is simply not there.
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public string? LastAuthorizationScheme { get; private set; }

    public string? LastAuthorizationParameter { get; private set; }

    /// <summary>The decoded <c>jql</c> parameter of the most recent search.</summary>
    public string? LastJql { get; private set; }

    /// <summary>The raw query string of every search, so a caller can count the pages it asked for.</summary>
    public List<string> SearchRequests { get; } = [];

    /// <summary>
    /// The issue key of every read that asked for the changelog, which is what its callers mean by
    /// it: "was a changelog call paid for this row". Narrowed to the expanded reads when the
    /// reconciliation started reading the same route without expanding - before that every read was
    /// a changelog read, so the two were the same list and the name was accidentally true. Reads
    /// that do not expand belong in <see cref="IssueRequests"/> instead; conflating them would make
    /// an existing <c>Assert.Empty</c> mean something it was never written to mean.
    ///
    /// <b>Nothing fells this split today</b> - measured, by recording every read here again: the
    /// suite stays green, because the only unexpanded reads are the reconciliation's and no test yet
    /// runs one through a preview. The reads that would collide with it arrive with task 6, which is
    /// when <c>Assert.Empty(jira.ChangelogRequests)</c> in JiraEndpointsTests would otherwise quietly
    /// stop being about changelogs. Written ahead of that rather than after it.
    /// </summary>
    public List<string> ChangelogRequests { get; } = [];

    /// <summary>The issue key of every read of one issue, expanded or not.</summary>
    public List<string> IssueRequests { get; } = [];

    /// <summary>
    /// The raw query string of the most recent issue read. Jira only carries the changelog when the
    /// caller expands it, so whether that parameter was sent is worth being able to assert on.
    /// </summary>
    public string? LastIssueQuery { get; private set; }

    /// <param name="rejectToken">Answers 401 to everything, the way a revoked PAT does.</param>
    /// <param name="pageSize">
    /// How many issues one page carries. The value the caller asked for is deliberately ignored,
    /// because Jira caps maxResults at the instance's own limit and reports what it actually used.
    /// </param>
    /// <param name="statusNames">
    /// Overrides the project's status names. At least two are needed: they are served as two
    /// overlapping issue types either way, because the overlap is the reason the source has to
    /// deduplicate and the grouping is the reason it has to flatten.
    /// </param>
    public static async Task<FakeJira> StartAsync(
        bool rejectToken = false, int pageSize = 50, string[]? statusNames = null)
    {
        var names = statusNames ?? ["I gang", "Afventer general", "Løst", "Venter på support"];

        if (names.Length < 2)
        {
            throw new ArgumentException(
                "Two status names are the fewest that can overlap across issue types.",
                nameof(statusNames));
        }

        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls(Loopback);

        // A fake answering requests has nothing to say on the test runner's console.
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var fake = new FakeJira(rejectToken, pageSize, names);

        fake.MapRoutes(app);

        await app.StartAsync();

        fake._app = app;
        fake.BaseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return fake;
    }

    /// <summary>
    /// A real <see cref="JiraTaskSource"/> over a real <see cref="HttpClient"/>, pointed at this
    /// fake. Not the DI constructor, because there is no database here to read settings out of.
    /// </summary>
    /// <param name="duty">
    /// The statuses the duty rotation covers. Null rather than <c>[]</c> as the default because a
    /// collection expression is not a compile-time constant and cannot be a parameter default; the
    /// two are the same thing to a caller, and every test written before duty existed keeps sending
    /// an empty list without saying so.
    /// </param>
    /// <param name="onDuty">Whether the rotation is currently on.</param>
    public JiraTaskSource SourceFor(
        string projectKey, string[]? duty = null, bool onDuty = false) => JiraTaskSource.With(
        _client,
        new JiraSettings(
            BaseUrl: BaseUrl,
            ProjectKey: projectKey,
            Token: Token,
            WaitingStatuses: [],
            IncludeWaiting: false,
            DutyStatuses: duty ?? [],
            OnDuty: onDuty,
            DoneStatuses: []));

    /// <summary>
    /// Stops answering while keeping <see cref="BaseUrl"/>. Nothing is listening on the port
    /// afterwards, so the connection is refused rather than left to time out.
    /// </summary>
    public async Task StopServerAsync()
    {
        if (_app is not { } app)
        {
            return;
        }

        _app = null;

        await app.StopAsync();
        await app.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopServerAsync();

        _client.Dispose();
    }

    private void MapRoutes(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            Record(context.Request);

            if (_rejectToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                await context.Response.WriteAsJsonAsync(new ErrorBody(["Log ind krævet"]), Json);

                return;
            }

            await next();
        });

        app.MapGet("/rest/api/2/myself", () => Results.Json(new MyselfBody(Owner), Json));

        app.MapGet("/rest/api/2/project/{key}/statuses", () => Results.Json(StatusGroups(), Json));

        app.MapGet("/rest/api/2/search", (HttpRequest request) =>
        {
            SearchRequests.Add(request.QueryString.Value ?? string.Empty);

            var jql = request.Query["jql"].ToString();

            LastJql = jql;

            // The reason Jira reconciles per key instead of in one search, modelled rather than
            // described: an issue key nobody knows is a 400 for the whole query, so a single deleted
            // issue would take the reconciliation down on the very case it exists to report.
            //
            // UNMEASURED against the real instance - nobody has yet put a made-up key in a key IN
            // list there - so this is the design's assumption written where it can be seen rather
            // than dressed up as a measurement. What a green suite then says is that the source never
            // names an unfound key; it does not say Jira would have refused one.
            var unknown = Named(jql).FirstOrDefault(key => !Knows(key));

            if (unknown is not null)
            {
                return Results.Json(
                    new ErrorBody([$"An issue with key '{unknown}' does not exist for field 'key'."]),
                    Json,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var startAt = int.TryParse(request.Query["startAt"], out var asked) ? asked : 0;
            var matching = Issues.Where(issue => Matches(jql, issue)).ToArray();

            return Results.Json(
                new SearchBody(
                    startAt, _pageSize, matching.Length, [.. matching.Skip(startAt).Take(_pageSize)]),
                Json);
        });

        // The expand parameter is load-bearing, not decoration: the real instance leaves the
        // changelog out of the issue unless it is asked for, so a source that forgets it gets an
        // issue with no history rather than an error, and every waiting date would be null.
        app.MapGet("/rest/api/2/issue/{key}", (string key, HttpRequest request) =>
        {
            IssueRequests.Add(key);
            LastIssueQuery = request.QueryString.Value ?? string.Empty;

            var expanded = request.Query["expand"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("changelog", StringComparer.OrdinalIgnoreCase);

            if (expanded)
            {
                ChangelogRequests.Add(key);
            }

            // Before the lookup, so a refusal cannot be mistaken for a key this Jira does not have.
            // The two answers mean opposite things to a reconciliation: 404 is an outcome to report,
            // 403 is a failure to raise.
            if (Forbidden.Contains(key))
            {
                // Constructed, and the odd one out in this file: every other message here is Jira's
                // own English. Nobody has seen what this instance answers for an issue the token may
                // not read - or whether it answers 403 at all, see the 404 below - so a made-up
                // Danish sentence is the honest form. Nothing asserts on the words.
                return Results.Json(
                    new ErrorBody(["Du har ikke rettigheder til at se sagen"]),
                    Json,
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var issue = Find(key);

            // 404 for a key this Jira has never heard of, which is the answer the whole per-key shape
            // turns on. "Has heard of" is the issues plus the changelog fixtures, because SAAS-4 is a
            // changelog with no issue behind it - deliberately, since JiraTaskSourceTests pins how
            // many issues the fetch answers - and it would otherwise start answering 404 to the
            // changelog tests that have always read it.
            if (issue is null && !Changelogs.ContainsKey(key))
            {
                return Results.Json(
                    new ErrorBody(["Issue Does Not Exist"]),
                    Json,
                    statusCode: StatusCodes.Status404NotFound);
            }

            // The issue's own spelling of the key rather than the caller's, because that is what Jira
            // does: ask for saas-1 and SAAS-1 comes back. Load-bearing for
            // The_sets_answer_with_the_key_the_caller_handed_in - remove it and that test passes on
            // nothing, because both spellings would then be the same string.
            //
            // Fields are answered whole rather than projected to the fields parameter. The real
            // server answers only what was asked for, so this fake is the more generous of the two,
            // and what that costs is that nothing here can see a source asking for a field name that
            // does not exist. The search route above has always answered whole issues; projecting one
            // of the two routes only would be a rule with a hole in it rather than a rule.
            return Results.Json(
                new IssueDetailBody(
                    issue?.Key ?? key,
                    issue?.Fields,
                    expanded ? new ChangelogBody(Changelog(key)) : null),
                Json);
        });
    }

    /// <summary>
    /// The issue behind a key, found the way Jira finds one: a key is not case sensitive. Null for a
    /// changelog fixture with no issue behind it, which is what SAAS-4 is.
    /// </summary>
    private static IssueBody? Find(string key) => Issues.FirstOrDefault(
        candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this Jira has anything at all under that key - an issue or a changelog.</summary>
    private static bool Knows(string key) => Find(key) is not null || Changelogs.ContainsKey(key);

    /// <summary>The keys a <c>key IN (…)</c> clause names, or none when the query has no such clause.</summary>
    private static string[] Named(string jql) => KeyClause.Match(jql) is { Success: true } clause
        ? clause.Groups[1].Value.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];

    /// <summary>
    /// The JQL evaluated clause by clause. Each is honoured when it is there and ignored when it is
    /// not, so the fetch, the duty widening and the reconciliation all go through one matcher.
    ///
    /// The assignee clause is not simply a filter, because the duty form widens it: the source writes
    /// <c>(assignee = currentUser() OR status IN (…))</c>, so an issue belonging to somebody else is
    /// still in the pool's answer. <see cref="InPool"/> is what lets that happen here, and
    /// <c>On_duty_the_pool_brings_in_an_issue_that_is_not_mine</c> is what holds it.
    ///
    /// <b>It does not guard the parentheses, and this note said it did until that was measured.</b>
    /// Clauses are read where they appear rather than parsed for precedence, so
    /// <c>status IN (…)</c> matches the same whether or not it sits inside brackets - dropping them
    /// from JqlFor leaves this matcher answering identically, and the only thing that goes red is
    /// <c>On_duty_the_query_also_asks_for_the_pool</c>, on the grouped literal it asserts. That
    /// literal is the guard on precedence; this is the guard on a foreign-owned issue reaching the
    /// answer at all.
    /// </summary>
    private static bool Matches(string jql, IssueBody issue)
    {
        if (Named(jql) is { Length: > 0 } named
            && !named.Contains(issue.Key, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Unresolved.IsMatch(jql) && Resolved.Contains(issue.Key))
        {
            return false;
        }

        return !AssignedToMe.IsMatch(jql)
            || !NotMine.Contains(issue.Key)
            || InPool(jql, issue);
    }

    private static bool InPool(string jql, IssueBody issue) =>
        StatusClause.Match(jql) is { Success: true } pool
        && StatusName.Matches(pool.Groups[1].Value).Any(
            name => string.Equals(
                name.Groups[1].Value, issue.Fields.Status.Name, StringComparison.Ordinal));

    /// <summary>
    /// A key with no changelog answers an empty history rather than a 404. The 404 belongs to the
    /// issue itself and is decided by the caller above; an issue that exists and never changed status
    /// has an empty history, which is what SAAS-3 is.
    /// </summary>
    private static HistoryBody[] Changelog(string key) =>
        Changelogs.TryGetValue(key, out var histories) ? histories : [];

    private void Record(HttpRequest request)
    {
        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header))
        {
            LastAuthorizationScheme = header.Scheme;
            LastAuthorizationParameter = header.Parameter;
        }
    }

    /// <summary>
    /// Jira answers one group per issue type, each with its own statuses, and the same status turns
    /// up in several groups. Both halves matter: without the overlap nothing would need
    /// deduplicating, and without the grouping nothing would need flattening.
    /// </summary>
    private IssueTypeStatusesBody[] StatusGroups() =>
    [
        new("Support", [.. _statusNames[..^1].Select(name => new StatusBody(name))]),
        new("Bug", [new StatusBody(_statusNames[0]), new StatusBody(_statusNames[^1])]),
    ];

    private sealed record MyselfBody(string DisplayName);

    private sealed record IssueTypeStatusesBody(string Name, StatusBody[] Statuses);

    private sealed record StatusBody(string Name);

    private sealed record PersonBody(string DisplayName);

    private sealed record SearchBody(int StartAt, int MaxResults, int Total, IssueBody[] Issues);

    private sealed record IssueBody(string Key, IssueFieldsBody Fields);

    /// <summary>Jira spells it <c>duedate</c>, one word, which the web policy would camel-case.</summary>
    private sealed record IssueFieldsBody(
        string Summary,
        string? Description,
        [property: JsonPropertyName("duedate")] string? DueDate,
        PersonBody? Reporter,
        StatusBody Status);

    /// <summary>
    /// One issue read on its own. It carries the fields as well as the changelog, because the
    /// reconciliation reads this route for the state and the title rather than for the history - the
    /// same body the search answers, plus the changelog when it was expanded.
    /// </summary>
    private sealed record IssueDetailBody(
        string Key, IssueFieldsBody? Fields, ChangelogBody? Changelog);

    private sealed record ChangelogBody(HistoryBody[] Histories);

    private sealed record HistoryBody(string Created, HistoryItemBody[] Items);

    /// <summary>
    /// Jira names the old and new values <c>fromString</c> and <c>toString</c>, and a property
    /// actually called ToString would hide object.ToString, so both carry the wire name explicitly.
    /// Only <c>field</c> is read by the source; the other two are here because a fixture that does
    /// not look like the real payload cannot show that the real payload binds.
    /// </summary>
    private sealed record HistoryItemBody(
        string Field,
        [property: JsonPropertyName("fromString")] string? From,
        [property: JsonPropertyName("toString")] string? To);

    private sealed record ErrorBody(string[] ErrorMessages);
}
