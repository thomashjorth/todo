using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todo.Core.Ado;
using Todo.Core.Time;
using Todo.Host.Ado;
using Todo.TestSupport.Time;

namespace Todo.TestSupport.Ado;

/// <summary>
/// An Azure DevOps Server on loopback, answering the routes <see cref="AdoTaskSource"/> asks for.
/// Real HTTP on purpose, the same reason FakeJira is: a stubbed ITaskSource would skip the
/// Authorization header, the URL, the WIQL text and the JSON reading, which is the whole of what can
/// go wrong between here and a real instance.
///
/// The shapes it answers with were measured against the real on-premises instance on 2026-08-20:
/// api-version 7.1, a PAT as Basic auth with an empty user name, WIQL answering ids plus an asOf
/// watermark, and a batch read answering count plus value.
///
/// Two things it does deliberately rather than conveniently. The collection and the project names
/// both carry a <b>space</b>, because the measured collection does and the plan warns that several
/// layers could un-escape it - a fake with tidy names would dodge the trap rather than cover it. And
/// every timestamp it serves is a <b>string</b> in the <c>Z</c> form the instance writes, not a
/// serialised DateTimeOffset, which would emit <c>+00:00</c> and measure .NET against itself.
///
/// No host outside 127.0.0.1 is named anywhere in this file, and none may be: NoRealInstanceTests is
/// the guard, and the app runs on the user's machine and may have no network.
/// </summary>
public sealed class FakeAdo : IAsyncDisposable
{
    /// <summary>Not a credential. It exists so a test can assert which token was sent.</summary>
    public const string Token = "fake-pat";

    /// <summary>
    /// With a space in it, and that is the point. The measured collection is two words, which is
    /// <c>%20</c> in a URL, and the plan lists three layers that could turn it back into a space.
    /// </summary>
    public const string Collection = "Fake Collection";

    /// <summary>
    /// Also with a space, which the measured project does not have - <c>Saas</c> is one word. This one
    /// is constructed rather than measured, because the project is escaped on a different path from the
    /// collection: the collection arrives inside a URL the user pasted and is already escaped, while
    /// the project is a name the app has to escape itself. A one-word project could not tell the two
    /// apart.
    /// </summary>
    public const string Project = "Some Project";

    /// <summary>What <see cref="SourceFor"/>'s clock says, so a derived deadline is predictable.</summary>
    public static readonly DateOnly Today = new(2026, 8, 20);

    private const string Loopback = "http://127.0.0.1:0";

    /// <summary>
    /// What <c>_apis/connectionData</c> says the token belongs to, read out of
    /// <c>authenticatedUser.providerDisplayName</c>.
    /// </summary>
    public const string Owner = "Thomas fra kataloget";

    /// <summary>
    /// Explicit rather than inherited from the hosting defaults, so the property names this fake
    /// answers with are decided here and cannot drift with a framework default. Nulls are left out
    /// rather than written, because a field a work item type does not have is <em>absent</em> from the
    /// real answer - and "absent" is the case the source's per-type note mapping turns on.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Nine work items covering what can go wrong in the mapping, and the awkward ones are the point.
    ///
    /// 15664 is the measured Bug: repro steps and no System.Description at all. 16901 is a User Story
    /// the other way round. 17170 has every optional field absent. 17169 is a Test Suite, which the
    /// type filter must keep out - and it uses <c>In Progress</c> where a Bug says <c>Active</c>, which
    /// is the measured inconsistency that made the waiting list a user's choice rather than a
    /// heuristic.
    ///
    /// 17165 is a Bug carrying <b>both</b> note fields with different text, and it is constructed
    /// rather than measured: the instance's Bug had only repro steps, so with a fallback in the source
    /// nothing measured could tell "repro steps first" from "whichever field is filled". Only a work
    /// item holding both can. It also carries the older <em>string</em> identity shape, because which
    /// of the two shapes this server sends for System.CreatedBy was never measured.
    ///
    /// 17162 carries a timestamp that is not a date. That is what gives "bind the date as a string"
    /// something to fail on: a DTO typing the field as DateTimeOffset throws for the whole page here,
    /// while the source under test loses one row's date.
    /// </summary>
    private static readonly WorkItemBody[] Measured =
    [
        new(15664, new FieldsBody(
            "Kunden kan ikke logge ind", "Blocked", "Bug",
            Identity("Anna Andersen"), null, "<div>Trin 1: log ind</div>",
            "2026-08-17T14:10:13.593Z")),
        new(16901, new FieldsBody(
            "Som bruger vil jeg kunne filtrere", "Active", "User Story",
            Identity("Bo Bertelsen"), "<div>Som bruger vil jeg</div>", null,
            "2026-08-15T09:00:00Z")),
        new(17170, new FieldsBody(
            "Uden noget som helst", "New", "Task",
            null, null, null, null)),
        new(17169, new FieldsBody(
            "Testsuite for login", "In Progress", "Test Suite",
            Identity("Dorte Dahl"), "<div>Suite</div>", null,
            "2026-08-19T07:30:00Z")),
        new(17165, new FieldsBody(
            "Fejl i eksport", "PO Review", "Bug",
            JsonDocument.Parse("\"Citronella Clausen <cc@example.invalid>\"").RootElement,
            "<div>Beskrivelsen</div>", "<div>Reproduktionen</div>",
            "2026-08-18T11:00:00Z")),
        new(17162, new FieldsBody(
            "Med et ulaeseligt tidsstempel", "Active", "Task",
            Identity("Erik Eriksen"), "<div>Noget</div>", null,
            "ikke en dato")),

        // Two Test Plans, kept out of every import by the type filter and therefore free to carry
        // whatever states are useful. Theirs are the pair that can tell a Danish collation from a code
        // point sort: da-DK orders AE-ligature before A-ring, code points the other way round. A state
        // name may legally carry Danish letters, and no other fixture state does, so without these the
        // comparer in the source would be unguarded. They also make "the state list is not narrowed by
        // the import filter" an assertion about two different things rather than one.
        new(17150, new FieldsBody(
            "Testplan for eksport", "Ændret", "Test Plan",
            Identity("Frida Frank"), "<div>Plan</div>", null, "2026-08-14T08:00:00Z")),
        new(17149, new FieldsBody(
            "Testplan for import", "Åben", "Test Plan",
            Identity("Gorm Gram"), "<div>Plan</div>", null, "2026-08-13T08:00:00Z")),

        // A state that differs from another only in case, so the ordinal deduplication has something to
        // fail on. Constructed rather than measured - no instance state pair looks like this - and
        // precedented: JiraStatusRolesTests pins the same rule from the other end, that a name differing
        // only in case is not the same status. A case-insensitive fold here would offer the user one
        // name for two states the server keeps apart, and every other assertion in the suite would stay
        // green.
        new(17148, new FieldsBody(
            "Testplan med lille begyndelsesbogstav", "active", "Test Plan",
            Identity("Hanne Holm"), "<div>Plan</div>", null, "2026-08-12T08:00:00Z")),
    ];

    /// <summary>
    /// The order the WIQL's <c>ORDER BY [System.ChangedDate] DESC</c> leaves them in, and deliberately
    /// not ascending by id - the batch answers in id order, so a source that forgot to restore this
    /// order would look right until somebody compared the two lists.
    /// </summary>
    private static readonly long[] MeasuredOrder =
        [17170, 15664, 17165, 16901, 17162, 17169, 17150, 17149, 17148];

    private static readonly Regex TypeClause = new(
        @"\[System\.WorkItemType\]\s+IN\s+\(([^)]*)\)", RegexOptions.IgnoreCase);

    private static readonly Regex TypeName = new("'([^']*)'");

    private readonly HttpClient _client;
    private readonly int _batchLimit;
    private readonly bool _rejectToken;
    private readonly WorkItemBody[] _items;
    private readonly long[] _order;

    private readonly string? _providerDisplayName;
    private readonly string? _customDisplayName;

    private WebApplication? _app;

    private FakeAdo(
        bool rejectToken,
        int batchLimit,
        int filler,
        string? providerDisplayName,
        string? customDisplayName)
    {
        _rejectToken = rejectToken;
        _batchLimit = batchLimit;
        _providerDisplayName = providerDisplayName;
        _customDisplayName = customDisplayName;

        // Filler work items exist for one reason: the source chunks at Azure DevOps' real cap of 200,
        // so the only honest way to measure that it reads every chunk is to have more than 200 of them.
        // Making the source's chunk size a test hook would have measured the hook instead.
        var filled = Enumerable.Range(0, filler).Select(index => new WorkItemBody(
            20000 + index,
            new FieldsBody(
                $"Fyld {index.ToString(CultureInfo.InvariantCulture)}",
                "Active",
                "Task",
                null,
                null,
                null,
                null)));

        _items = [.. Measured, .. filled];
        _order = [.. MeasuredOrder, .. _items.Skip(Measured.Length).Select(item => item.Id)];

        // Mirrors TodoHost's registration, so a source that hangs hangs the same way here.
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// The collection URL, escaped as a URL carrying a space has to be. Survives
    /// <see cref="StopServerAsync"/>, so a caller can meet an Azure DevOps that is simply not there.
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public string? LastAuthorizationScheme { get; private set; }

    public string? LastAuthorizationParameter { get; private set; }

    /// <summary>
    /// The decoded path of the most recent request, so a caller can assert that the collection and the
    /// project are both in it and in that order.
    /// </summary>
    public string? LastPath { get; private set; }

    /// <summary>
    /// The request line exactly as it arrived, before ASP.NET decoded anything. The only place the
    /// escaping of the collection name is visible at all: <see cref="LastPath"/> has already been
    /// un-escaped by the time anyone can read it.
    /// </summary>
    public string? LastRawTarget { get; private set; }

    /// <summary>The <c>query</c> of the most recent WIQL post.</summary>
    public string? LastWiql { get; private set; }

    /// <summary>Every WIQL post, so a caller can prove one was refused before it went out.</summary>
    public List<string> WiqlRequests { get; } = [];

    /// <summary>
    /// The decoded path of every WIQL post. Its own list rather than <see cref="LastPath"/>, because by
    /// the time a fetch has finished the last path is a batch read and the collection-plus-project
    /// assertion belongs to the WIQL.
    /// </summary>
    public List<string> WiqlPaths { get; } = [];

    /// <summary>The raw query string of every batch read, so a caller can count the chunks.</summary>
    public List<string> BatchRequests { get; } = [];

    /// <summary>The id of every single work item read.</summary>
    public List<long> WorkItemRequests { get; } = [];

    /// <param name="rejectToken">Answers 401 to everything, the way a revoked PAT does.</param>
    /// <param name="batchLimit">
    /// How many ids one <c>?ids=</c> read accepts. Azure DevOps caps this at 200 and answers 400 above
    /// it rather than truncating, so this fake does the same - a lower value is how a test meets the
    /// boundary without seeding two hundred work items.
    /// </param>
    /// <param name="filler">
    /// How many extra, uninteresting work items to serve on top of the measured six. The only way to
    /// cross the source's real chunk size of 200 without turning that constant into a test hook, which
    /// would have measured the hook rather than the cap.
    /// </param>
    /// <param name="providerDisplayName">
    /// What an Active Directory backed server fills. Null and blank are both worth passing: a server
    /// that fills neither name is why "Test connection" can succeed with nothing to show.
    /// </param>
    /// <param name="customDisplayName">
    /// What a server fills for a user who renamed themselves. Azure DevOps' own UI prefers it, so the
    /// source does too - and until a user reported the button saying nothing, this field was left out
    /// of the fake on the grounds that no test could reach it.
    /// </param>
    public static async Task<FakeAdo> StartAsync(
        bool rejectToken = false,
        int batchLimit = 200,
        int filler = 0,
        string? providerDisplayName = Owner,
        string? customDisplayName = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls(Loopback);

        // A fake answering requests has nothing to say on the test runner's console.
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var fake = new FakeAdo(
            rejectToken, batchLimit, filler, providerDisplayName, customDisplayName);

        fake.MapRoutes(app);

        await app.StartAsync();

        fake._app = app;

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        // Escaped here, because this stands in for what the user pastes into the settings page: a URL,
        // not a name. Uri.EscapeDataString rather than a literal %20, so the escaping and the name stay
        // in step if the name ever changes.
        fake.BaseUrl = $"{address.TrimEnd('/')}/{Uri.EscapeDataString(Collection)}";

        return fake;
    }

    /// <summary>
    /// A real <see cref="AdoTaskSource"/> over a real <see cref="HttpClient"/>, pointed at this fake.
    /// Not the DI constructor, because there is no database here to read settings out of.
    /// </summary>
    /// <param name="project">
    /// Null and blank are both worth passing: a missing project is the source's own refusal.
    /// </param>
    /// <param name="workItemTypes">
    /// Null rather than the default list as the parameter default, because a collection expression is
    /// not a compile-time constant. Null means AdoDefaults.WorkItemTypes, which is what an absent
    /// settings row reads as - so a test that says nothing gets the shipped behaviour.
    /// </param>
    /// <param name="defaultDeadlineDays">Zero means no deadline, which is a value rather than an absence.</param>
    public AdoTaskSource SourceFor(
        string? project = Project,
        string[]? workItemTypes = null,
        int defaultDeadlineDays = AdoDefaults.DeadlineDays,
        DateOnly? today = null) => AdoTaskSource.With(
        _client,
        new AdoSettings(
            BaseUrl: BaseUrl,
            Project: project,
            Token: Token,
            WaitingStates: [],
            IncludeWaiting: false,
            WorkItemTypes: workItemTypes ?? AdoDefaults.WorkItemTypes,
            DefaultDeadlineDays: defaultDeadlineDays),
        Clock(today));

    private static IClock Clock(DateOnly? today) => new FixedClock(today ?? Today);

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

    /// <summary>
    /// The object shape of an identity field, which is what api-version 7.1 documents for
    /// System.CreatedBy. The unique name rides along because a fixture that does not look like the real
    /// payload cannot show that the real payload binds - and because the source must read the display
    /// name rather than the address.
    /// </summary>
    private static JsonElement Identity(string displayName) => JsonSerializer.SerializeToElement(
        new CreatedByBody(displayName, $"EXAMPLE\\{displayName.Split(' ')[0].ToLowerInvariant()}"),
        Json);

    private void MapRoutes(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            Record(context);

            if (_rejectToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                // Azure DevOps puts its reason in one message string, not in Jira's array.
                await context.Response.WriteAsJsonAsync(
                    new ErrorBody("TF400813: The user is not authorized to access this resource."),
                    Json);

                return;
            }

            await next();
        });

        // Collection level. The route parameter is what proves the collection segment is really in the
        // URL rather than the app having talked to the server's root.
        app.MapGet("/{collection}/_apis/connectionData", () => Results.Json(
            new ConnectionDataBody(
                new IdentityBody(_providerDisplayName, _customDisplayName)), Json));

        // Project level, because that is what scopes a WIQL - measured 2026-08-20. Both segments are
        // route parameters so a source that dropped one gets a 404 rather than an answer.
        app.MapPost("/{collection}/{project}/_apis/wit/wiql", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);

            var query = JsonSerializer.Deserialize<WiqlRequestBody>(await reader.ReadToEndAsync(), Json)
                ?.Query ?? string.Empty;

            WiqlRequests.Add(query);
            WiqlPaths.Add(request.Path.Value ?? string.Empty);
            LastWiql = query;

            var ids = _order.Where(id => Matches(query, id)).ToArray();

            // asOf is the watermark the real server hands over for free. Nothing reads it yet, and it
            // is here so that stays visible rather than becoming a surprise in slice 13.
            return Results.Json(
                new WiqlResponseBody("2026-08-20T06:00:00Z", [.. ids.Select(id => new WiqlItemBody(id))]),
                Json);
        });

        app.MapGet("/{collection}/_apis/wit/workitems", (HttpRequest request) =>
        {
            BatchRequests.Add(request.QueryString.Value ?? string.Empty);

            var asked = (request.Query["ids"].ToString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => long.Parse(id, CultureInfo.InvariantCulture))
                .ToArray();

            if (asked.Length > _batchLimit)
            {
                return Results.Json(
                    new ErrorBody(
                        $"VS402337: The number of work items requested exceeds the maximum "
                        + $"of {_batchLimit}."),
                    Json,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Measured: unknown ids are a 400 saying which ones, not a silently shorter list.
            var unknown = asked.Where(id => _items.All(item => item.Id != id)).ToArray();

            if (unknown.Length > 0)
            {
                return Results.Json(
                    new ErrorBody($"The following Ids are not valid: {string.Join(",", unknown)}."),
                    Json,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Ascending by id rather than in the order asked, which is what the real batch is allowed
            // to do and what makes the source's re-ordering measurable.
            var value = _items
                .Where(item => asked.Contains(item.Id))
                .OrderBy(item => item.Id)
                .Select(item => Projected(item, request.Query["fields"].ToString()))
                .ToArray();

            return Results.Json(new BatchBody(value.Length, value), Json);
        });

        app.MapGet("/{collection}/_apis/wit/workitems/{id:long}", (long id, HttpRequest request) =>
        {
            WorkItemRequests.Add(id);

            var item = _items.FirstOrDefault(candidate => candidate.Id == id);

            return item is null
                ? Results.Json(
                    new ErrorBody($"TF401232: Work item {id} does not exist, or you do not have "
                        + "permissions to read it."),
                    Json,
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(Projected(item, request.Query["fields"].ToString()), Json);
        });
    }

    /// <summary>
    /// Only the fields that were asked for, because the real server answers only those - and a fake
    /// that answered every field would let a source pass while forgetting to ask for one.
    /// </summary>
    private static WorkItemBody Projected(WorkItemBody item, string fields)
    {
        var wanted = fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0 || item.Fields is not { } all)
        {
            return item;
        }

        return item with
        {
            Fields = new FieldsBody(
                wanted.Contains("System.Title") ? all.Title : null,
                wanted.Contains("System.State") ? all.State : null,
                wanted.Contains("System.WorkItemType") ? all.WorkItemType : null,
                wanted.Contains("System.CreatedBy") ? all.CreatedBy : null,
                wanted.Contains("System.Description") ? all.Description : null,
                wanted.Contains("Microsoft.VSTS.TCM.ReproSteps") ? all.ReproSteps : null,
                wanted.Contains("Microsoft.VSTS.Common.StateChangeDate") ? all.StateChangeDate : null),
        };
    }

    /// <summary>
    /// The one piece of WIQL this fake understands, and it understands it because the type filter is
    /// what decision B is: a fake that ignored the clause would answer test artefacts to a source that
    /// correctly asked for none, and the item-level assertion would be measuring nothing. The state
    /// clause is not interpreted - no work item here is Closed - so that half is asserted on the query
    /// text instead.
    /// </summary>
    private bool Matches(string wiql, long id)
    {
        var item = _items.First(candidate => candidate.Id == id);
        var clause = TypeClause.Match(wiql);

        if (!clause.Success)
        {
            return true;
        }

        return TypeName.Matches(clause.Groups[1].Value)
            .Any(name => string.Equals(
                name.Groups[1].Value, item.Fields?.WorkItemType, StringComparison.Ordinal));
    }

    private void Record(HttpContext context)
    {
        if (AuthenticationHeaderValue.TryParse(
            context.Request.Headers.Authorization.ToString(), out var header))
        {
            LastAuthorizationScheme = header.Scheme;
            LastAuthorizationParameter = header.Parameter;
        }

        LastPath = context.Request.Path.Value;
        LastRawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
    }

    private sealed record WiqlRequestBody(string Query);

    private sealed record WiqlResponseBody(string AsOf, WiqlItemBody[] WorkItems);

    private sealed record WiqlItemBody(long Id);

    private sealed record BatchBody(int Count, WorkItemBody[] Value);

    private sealed record WorkItemBody(long Id, FieldsBody? Fields);

    /// <summary>
    /// Azure DevOps' field names carry dots, so every one of them is named explicitly. StateChangeDate
    /// is a <b>string</b> and not a DateTimeOffset on purpose: measured 2026-08-20, the server writes
    /// <c>2026-08-20T06:00:00Z</c>, while serialising a DateTimeOffset would emit <c>+00:00</c> - so a
    /// typed field here would measure .NET against .NET instead of against the instance. That is
    /// CLAUDE.md's Jira lesson pointed at the fake rather than at the source.
    /// </summary>
    private sealed record FieldsBody(
        [property: JsonPropertyName("System.Title")] string? Title,
        [property: JsonPropertyName("System.State")] string? State,
        [property: JsonPropertyName("System.WorkItemType")] string? WorkItemType,
        [property: JsonPropertyName("System.CreatedBy")] JsonElement? CreatedBy,
        [property: JsonPropertyName("System.Description")] string? Description,
        [property: JsonPropertyName("Microsoft.VSTS.TCM.ReproSteps")] string? ReproSteps,
        [property: JsonPropertyName("Microsoft.VSTS.Common.StateChangeDate")] string? StateChangeDate);

    private sealed record ConnectionDataBody(IdentityBody AuthenticatedUser);

    /// <summary>
    /// Both names the real response carries. <c>customDisplayName</c> was left out at first, on the
    /// grounds that reading a field no fixture serves would be a branch no test could reach - which was
    /// backwards: the fixture is this file, so leaving it out is what made the branch unreachable. A
    /// user reporting that "Test connection" said nothing is what measured it.
    /// </summary>
    private sealed record IdentityBody(string? ProviderDisplayName, string? CustomDisplayName);

    /// <summary>The identity shape a field carries, which is not the one connectionData uses.</summary>
    private sealed record CreatedByBody(string DisplayName, string UniqueName);

    private sealed record ErrorBody(string Message);
}
