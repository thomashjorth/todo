using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Sources;
using Todo.Core.Time;

namespace Todo.Host.Ado;

/// <summary>
/// Reads the work items assigned to you out of one Azure DevOps Server collection. ITaskSource's
/// second implementation, which is the point of slice 12: whether the shape slice 11 left behind was
/// Jira-shaped. Measured against the real on-premises instance 2026-08-20, and the measurements are
/// what decide everything here.
///
/// Where it does not look like JiraTaskSource, and why:
///
/// <list type="bullet">
/// <item><b>Two calls, not one.</b> Jira's /search answers issues. Azure DevOps answers a WIQL query
/// with nothing but ids and then hydrates them in a batch, so one logical read is two round trips -
/// and the paging moved with it: WIQL is not paged, the <em>hydration</em> is, because ?ids= takes at
/// most <see cref="BatchSize"/> per request.</item>
/// <item><b>The order has to be put back.</b> The WIQL carries the ORDER BY; the batch does not
/// promise to answer in the order it was asked. Jira never had this problem, because its search
/// returned the issues themselves.</item>
/// <item><b>The project is in the path, not in the query.</b> Jira narrows with <c>project = X</c>
/// inside the JQL, where slice 11 measured that the parentheses around a disjunction were
/// load-bearing. Azure DevOps scopes by URL - measured, the WIQL was posted to
/// <c>{collection}/{project}/_apis/wit/wiql</c> - so the narrowing cannot be lost to operator
/// precedence, and the guard is on the path instead of on the query text.</item>
/// <item><b>An empty type filter refuses.</b> Jira's duty clause is omitted when the list is empty,
/// which is right there because duty is optional. Here the list <em>is</em> the limit - decision B -
/// so emptiness is a refusal, not an omission.</item>
/// <item><b>The waiting date is free.</b> Microsoft.VSTS.Common.StateChangeDate arrives with the work
/// item, so it rides on <see cref="ExternalTask.StatusChangedAt"/> and no caller has to pay per row.
/// Jira has no such field and needs the changelog call.</item>
/// <item><b>Basic auth, not Bearer.</b> A PAT goes in as <c>base64(":" + PAT)</c> - measured.</item>
/// <item><b>The error shape is <c>message</c>, not <c>errorMessages</c>.</b> One string, not an
/// array.</item>
/// </list>
/// </summary>
public sealed class AdoTaskSource : ITaskSource
{
    /// <summary>What goes in <c>TaskItem.SourceId</c>, and half of the deduplication key.</summary>
    public const string Id = "ado";

    /// <summary>
    /// Measured 2026-08-20 with <c>OPTIONS {collection}/_apis/wit</c>: wiql, updates and workItems all
    /// report maxVersion 7.2 and releasedVersion 7.1. Asking for 7.2 would be calling a preview API
    /// that is allowed to change underneath us.
    /// </summary>
    private const string ApiVersion = "api-version=7.1";

    /// <summary>
    /// Azure DevOps' documented cap on <c>?ids=</c>, and asking for more is a 400 rather than a
    /// truncated answer. Not measured here - the instance had twelve assigned work items on
    /// 2026-08-20, so the boundary was never reached - which is why it is written as a constant with
    /// this note rather than as a number in a loop. A value below the real cap is harmless; a value
    /// above it fails on the first user with a long list.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>
    /// Only the fields that are mapped. Two of them are the same idea spelled twice, and that is the
    /// measured shape rather than belt and braces: the note lives in
    /// Microsoft.VSTS.TCM.ReproSteps on a Bug and in System.Description on a User Story, and the Bug
    /// measured on 2026-08-20 did not carry System.Description at all. A field that a given work item
    /// type does not have is simply left out of the answer; only a field the <em>project</em> has
    /// never heard of is a 400.
    /// </summary>
    private const string ItemFields =
        "System.Title,System.State,System.WorkItemType,System.CreatedBy,System.Description,"
        + "Microsoft.VSTS.TCM.ReproSteps,Microsoft.VSTS.Common.StateChangeDate";

    /// <summary>Just enough to list the states the user's own work items are in.</summary>
    private const string StateField = "System.State";

    /// <summary>
    /// Jira's statuscategorychangedate, except this one exists. Measured 2026-08-20 on the work item
    /// itself, which is why no caller has to pay a round trip per row.
    /// </summary>
    private const string StateChangeField = "Microsoft.VSTS.Common.StateChangeDate";

    /// <summary>
    /// Measured 2026-08-20, and it was the last blocking assumption in the whole design: @Me resolves,
    /// and the query answered twelve work items. <c>&lt;&gt; 'Closed'</c> rather than a state category,
    /// because the state vocabulary differs per work item type on this instance and Closed is the one
    /// name shared by all of them.
    /// </summary>
    private const string BaseQuery =
        "SELECT [System.Id] FROM WorkItems "
        + "WHERE [System.AssignedTo] = @Me AND [System.State] <> 'Closed'";

    private const string Ordering = " ORDER BY [System.ChangedDate] DESC";

    /// <summary>
    /// State names are shown to a Danish user, so they sort the Danish way. Spelled out again rather
    /// than shared with JiraTaskSource: slice 12 exists to find out where the two sources converge,
    /// and folding two lines together before the answer is in would be assuming it. Measured on
    /// net10.0 with ICU - see the Jira test for the pair that can actually tell the comparers apart.
    /// </summary>
    private static readonly StringComparer DanishOrder =
        StringComparer.Create(new CultureInfo("da-DK"), ignoreCase: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly IClock _clock;
    private readonly Func<CancellationToken, Task<AdoSettings>> _settings;

    /// <summary>
    /// The one constructor DI sees. The settings are read per call rather than captured, because the
    /// user can change the collection, the project or the token while the app is running.
    /// </summary>
    public AdoTaskSource(HttpClient client, AdoSettingsReader reader, IClock clock)
        : this(client, reader.ReadAsync, clock)
    {
    }

    private AdoTaskSource(
        HttpClient client, Func<CancellationToken, Task<AdoSettings>> settings, IClock clock)
    {
        _client = client;
        _settings = settings;
        _clock = clock;
    }

    /// <summary>
    /// For a caller that already holds the settings and has no database to read them from. A second
    /// public constructor would be ambiguous to ActivatorUtilities, which picks a typed client's
    /// constructor by counting parameters it can resolve - the same reason JiraTaskSource.With exists.
    /// </summary>
    public static AdoTaskSource With(HttpClient client, AdoSettings settings, IClock clock) =>
        new(client, _ => Task.FromResult(settings), clock);

    public string SourceId => Id;

    /// <summary>
    /// <c>_apis/connectionData</c> at collection level, which is the call that measured
    /// <c>deploymentType: onPremises</c> on 2026-08-20 - so the endpoint answers, and this is what
    /// "Test connection" will verify the token against.
    ///
    /// <c>providerDisplayName</c> is what an Active Directory backed server fills. The response also
    /// carries <c>customDisplayName</c>, for a user who renamed themselves, and it is deliberately not
    /// read: nothing measured which of the two this server fills, and a preference no fixture can serve
    /// is a branch no test could reach. If it turns out empty in use, that is one measurement away.
    /// </summary>
    public async Task<SourceIdentity> TestAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var body = await SendAsync(settings, HttpMethod.Get, "_apis/connectionData", null, null, ct);
        var user = Read<ConnectionDataBody>(body)?.AuthenticatedUser;

        return new SourceIdentity(Blank(user?.ProviderDisplayName) ?? string.Empty);
    }

    /// <summary>
    /// The states the user's own work items are currently in, deduplicated and sorted.
    ///
    /// Read off the work items rather than off <c>_apis/wit/workitemtypes</c>, and the honest reason is
    /// that this is the shape that was measured: measurement 0c read the states out of the items and
    /// answered <c>count</c> + <c>value</c>, while measurement 0d asked workitemtypes only for
    /// <c>name</c> and <c>referenceName</c> - so whether that endpoint carries a states array on this
    /// server is unknown, and CLAUDE.md's duedate lesson is precisely about binding a field nobody has
    /// seen.
    ///
    /// The cost of the choice, said out loud rather than hidden: a state that none of your work items
    /// is in right now cannot be offered, so a user cannot mark Blocked as waiting on a day when
    /// nothing is blocked. Both of the plan's obvious candidates - Blocked and PO Review - were among
    /// the twelve measured, so it works in practice; if it turns out not to, the fix is one measurement
    /// of workitemtypes away and not a redesign.
    ///
    /// No type filter on this query on purpose. The filter says which work items to <em>import</em>;
    /// the vocabulary the user picks waiting states from should be the whole of what they are looking
    /// at, or a state only a Test Suite uses could never be named.
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchStatusNamesAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var ids = await QueryIdsAsync(settings, BaseQuery + Ordering, ct);
        var items = await HydrateAsync(settings, ids, StateField, ct);

        return
        [
            .. items
                .Select(item => item.Fields?.State)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                // Ordinal, because JiraStatusRoles' successor will compare state names ordinally and
                // a case-insensitive fold here would offer one name for two states the server keeps
                // apart. Measured 2026-08-20: this instance really does use two names for one idea -
                // Test Suite says In Progress where a Bug says Active - so near-duplicates are the
                // normal case rather than the odd one.
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, DanishOrder)
        ];
    }

    public async Task<ExternalTaskPage> FetchAssignedAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);

        // Built before anything goes out, so a work item type that cannot go in a WIQL literal is
        // refused before the first request rather than after the query has already run.
        var wiql = AssignedQuery(settings);

        var ids = await QueryIdsAsync(settings, wiql, ct);
        var items = await HydrateAsync(settings, ids, ItemFields, ct);

        // The deadline is the same for every row and is derived once: it is a function of today and a
        // setting, not of the work item. Azure DevOps has no due date field at all - decision A.
        var deadline = AdoDeadline.For(_clock.Today, settings.DefaultDeadlineDays);

        // The batch does not promise to answer in the order it was asked, so the WIQL's ORDER BY is
        // restored here. Jira needed nothing like this: its search returned the issues in order.
        var byId = items
            .Where(item => item.Fields is not null)
            .ToDictionary(item => item.Id, item => item);

        var mapped = ids
            .Where(byId.ContainsKey)
            .Select(id => Map(byId[id], deadline))
            .ToList();

        // Total is the number of ids the query matched rather than the number of rows mapped, so a
        // hydration that dropped something is visible as items.Count < Total instead of looking like
        // the whole answer. Nothing here is truncated the way a Jira page can be.
        return new ExternalTaskPage(mapped, ids.Count);
    }

    /// <summary>
    /// The fallback path, and for Azure DevOps it is only that: every row from
    /// <see cref="FetchAssignedAsync"/> already carries
    /// <see cref="ExternalTask.StatusChangedAt"/>, because the field arrives with the work item. Kept
    /// as a real read rather than a null-returning stub - a caller holding nothing but a key still
    /// gets an answer, and a stub would be a lie the interface cannot see.
    /// </summary>
    public async Task<DateTime?> FetchStatusChangedAtAsync(
        string externalKey, CancellationToken ct = default)
    {
        var settings = await _settings(ct);

        // An Azure DevOps key is a work item id. Anything else is a caller mixing up two sources
        // rather than a server problem, and asking for /workitems/SAAS-1 would be a 400 blamed on the
        // instance.
        if (!long.TryParse(externalKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var body = await SendAsync(
            settings,
            HttpMethod.Get,
            $"_apis/wit/workitems/{id.ToString(CultureInfo.InvariantCulture)}",
            $"fields={StateChangeField}",
            null,
            ct);

        return Moment(Read<WorkItemBody>(body)?.Fields?.StateChangeDate)?.UtcDateTime;
    }

    /// <summary>
    /// The assigned query plus the type filter from decision B. An empty list is a refusal rather than
    /// an omitted clause, which is the opposite of what Jira's duty clause does - and deliberately so:
    /// duty is an optional widening, while this list <em>is</em> the limit. Slice 11's lesson applies
    /// literally here, that the absence of a limit is not a neutral default, and the plan's first
    /// answer (an empty list means every type) was reversed in task 2 for that reason. Two of the
    /// twelve measured work items were test artefacts.
    ///
    /// Blanks are dropped and the rest trimmed before the emptiness check rather than after, so a list
    /// holding nothing but blanks behaves as an empty one. Same fault and same fix as the Jira duty
    /// list: <c>' '</c> would become <c>IN (' ')</c>, which is valid WIQL matching nothing - a silent
    /// failure - and closing only half of it would leave the class looking closed. The trimming is here
    /// rather than in SettingsEndpoints for the same reason it is there: a row stored before a
    /// validation existed outlives it, and this is what builds the query.
    /// </summary>
    private static string AssignedQuery(AdoSettings settings)
    {
        var types = settings.WorkItemTypes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();

        if (types.Count == 0)
        {
            throw new SourceException(
                ErrorCodes.AdoWorkItemTypesRequired,
                "At least one work item type is needed, or the import would ask for everything.");
        }

        var names = string.Join(", ", types.Select(TypeLiteral));

        return $"{BaseQuery} AND [System.WorkItemType] IN ({names}){Ordering}";
    }

    /// <summary>
    /// One work item type as the quoted literal WIQL wants. A blocklist rather than a whitelist, the
    /// same choice JiraTaskSource.StatusLiteral documents: <c>User Story</c> and <c>Test Suite</c> have
    /// spaces in them, and a whitelist strict enough to be safe would refuse the values the feature
    /// exists for. WIQL string literals are single-quoted, so <c>'</c> is the character that matters,
    /// and the backslash comes along because escaping rules turn on it too.
    ///
    /// Refused rather than escaped, because the picker only ever offers what the app itself defaulted
    /// to or what the instance reported. A name carrying one of these is a bug elsewhere, and papering
    /// over it would hide that.
    /// </summary>
    private static string TypeLiteral(string name)
    {
        if (name.Contains('\'') || name.Contains('"') || name.Contains('\\'))
        {
            throw new SourceException(
                ErrorCodes.AdoWorkItemTypeInvalid,
                "A work item type cannot contain a quotation mark or a backslash.");
        }

        return $"'{name}'";
    }

    /// <summary>
    /// The WIQL half. Answers ids and an <c>asOf</c> watermark; the watermark is not read yet - slice
    /// 13's incremental fetch is what it is for - but it is what makes one available for free.
    /// </summary>
    private async Task<List<long>> QueryIdsAsync(
        AdoSettings settings, string wiql, CancellationToken ct)
    {
        var project = ProjectOf(settings);
        var body = await SendAsync(
            settings,
            HttpMethod.Post,
            $"{Uri.EscapeDataString(project)}/_apis/wit/wiql",
            null,
            JsonSerializer.Serialize(new WiqlRequestBody(wiql), Json),
            ct);

        return [.. (Read<WiqlResponseBody>(body)?.WorkItems ?? []).Select(item => item.Id)];
    }

    /// <summary>
    /// The hydration half, and this is where Azure DevOps pages. <c>?ids=</c> takes at most
    /// <see cref="BatchSize"/>, so the list is read in chunks and every chunk is read - a source that
    /// stopped after the first would silently import the newest 200 work items and look like it had
    /// finished.
    /// </summary>
    private async Task<List<WorkItemBody>> HydrateAsync(
        AdoSettings settings, List<long> ids, string fields, CancellationToken ct)
    {
        var items = new List<WorkItemBody>();

        for (var offset = 0; offset < ids.Count; offset += BatchSize)
        {
            var chunk = ids.Skip(offset).Take(BatchSize)
                .Select(id => id.ToString(CultureInfo.InvariantCulture));

            var body = await SendAsync(
                settings,
                HttpMethod.Get,
                "_apis/wit/workitems",
                $"ids={string.Join(",", chunk)}&fields={fields}",
                null,
                ct);

            items.AddRange(Read<BatchBody>(body)?.Value ?? []);
        }

        return items;
    }

    private static ExternalTask Map(WorkItemBody item, DateOnly? deadline) => new(
        Key: item.Id.ToString(CultureInfo.InvariantCulture),
        Title: item.Fields?.Title ?? string.Empty,
        Note: NoteOf(item.Fields),
        Deadline: deadline,
        Requester: RequesterOf(item.Fields),
        StatusName: item.Fields?.State ?? string.Empty,
        ItemType: Blank(item.Fields?.WorkItemType),
        StatusChangedAt: Moment(item.Fields?.StateChangeDate)?.UtcDateTime);

    /// <summary>
    /// Which field the note comes from depends on the work item type, and that is measured rather than
    /// assumed: work item 15664 is a Bug and carries Microsoft.VSTS.TCM.ReproSteps but <em>not</em>
    /// System.Description, while a User Story carries System.Description. This is CLAUDE.md's duedate
    /// lesson in a worse form - not one wrong name, but several right ones.
    ///
    /// The fallback is second and the per-type choice is first, and the order is the whole of it: a
    /// Bug that happens to carry both fields must show the repro steps, because that is where the
    /// Bug's form puts the text a person wrote. The fallback exists for the types nobody has measured -
    /// there are more than the four seen on 2026-08-20 - so an unknown type gets whichever field it
    /// filled instead of an empty note.
    ///
    /// The text is left as Azure DevOps' HTML rather than converted to CommonMark here, and that is a
    /// decision with a reason rather than an omission: measurement 0b deliberately printed field
    /// <em>names</em> only, so nobody has seen what this instance's rich text actually looks like, and
    /// slice 13 needs the very same converter for comment HTML. One converter built against two
    /// measured samples beats two built against none. Until then the app's marked renders inline HTML
    /// through, so the note is readable rather than mangled.
    /// </summary>
    private static string? NoteOf(FieldsBody? fields)
    {
        if (fields is null)
        {
            return null;
        }

        var preferred = string.Equals(fields.WorkItemType, "Bug", StringComparison.Ordinal)
            ? fields.ReproSteps
            : fields.Description;

        return Blank(preferred) ?? Blank(fields.ReproSteps) ?? Blank(fields.Description);
    }

    /// <summary>
    /// System.CreatedBy is an identity, and an identity has two wire shapes across Azure DevOps
    /// versions: an object carrying displayName, or the older single string <c>Name &lt;email&gt;</c>.
    /// Measurement 0b printed field names only, so which one this server sends is unknown - and
    /// binding the wrong one would give null in every requester without a test falling, exactly as
    /// Jira's duedate did. Both are read instead of one being guessed, and the address is dropped from
    /// the string form because a requester is shown to a person, not mailed to.
    /// </summary>
    private static string? RequesterOf(FieldsBody? fields)
    {
        if (fields?.CreatedBy is not { } created)
        {
            return null;
        }

        return created.ValueKind switch
        {
            JsonValueKind.Object => Blank(
                created.TryGetProperty("displayName", out var name) ? name.GetString() : null),
            JsonValueKind.String => Blank(WithoutAddress(created.GetString())),
            _ => null,
        };
    }

    private static string? WithoutAddress(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var bracket = value.IndexOf('<', StringComparison.Ordinal);

        return bracket < 0 ? value : value[..bracket];
    }

    /// <summary>
    /// Bound as a string and parsed here rather than typed as a DateTimeOffset on the DTO. Azure DevOps
    /// writes a timestamp with a <c>Z</c> - measured on revisedDate, 2026-08-20 - which System.Text.Json
    /// would actually bind, unlike Jira's <c>+0200</c>, so this is not the same trap. It is still the
    /// right shape for a different reason: a typed field throws for the <em>whole page</em> on one odd
    /// value, while this costs that one row its date. Invariant culture because under da-DK the current
    /// culture reads a date as dd-MM-yyyy.
    /// </summary>
    private static DateTimeOffset? Moment(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var moment)
            ? moment
            : null;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The project goes in the URL path, which is how Azure DevOps scopes a WIQL - measured, the query
    /// was posted to <c>{collection}/{project}/_apis/wit/wiql</c>. Its own refusal rather than part of
    /// AdoSettings.IsConfigured, so the user is told which field is blank; see the note on that
    /// property. No character validation beyond that: a project is a name rather than a query
    /// fragment, and Uri.EscapeDataString makes one path segment out of whatever it is.
    /// </summary>
    private static string ProjectOf(AdoSettings settings)
    {
        var project = settings.Project?.Trim() ?? string.Empty;

        return project.Length == 0
            ? throw new SourceException(
                ErrorCodes.AdoProjectRequired,
                "An Azure DevOps project is needed, because it is what scopes the query.")
            : project;
    }

    private async Task<string> SendAsync(
        AdoSettings settings,
        HttpMethod method,
        string path,
        string? query,
        string? json,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, UriFor(settings, path, query));

        // Per request rather than on the client. The client is shared and long-lived, so a header set
        // on it would outlive a token the user has since cleared.
        //
        // Basic with an empty user name, not Bearer: measured 2026-08-20, base64(":" + PAT) answers
        // 200 where Jira's Bearer form is what its own instance wants. ASCII because that is what the
        // measurement used and a PAT is base64url text anyway.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($":{settings.Token}")));

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;

        try
        {
            response = await _client.SendAsync(request, ct);
        }
        catch (HttpRequestException exception)
        {
            // The message names the host and the socket error, never a header.
            throw Unreachable(exception.Message);
        }
        catch (TaskCanceledException)
        {
            // HttpClient throws this both for its own timeout and for a cancelled call, and "A task
            // was canceled" in a 500 tells the user nothing they can act on.
            throw Unreachable("it did not answer in time");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // The status code and Azure DevOps' own words, and nothing of the request: the token
                // rides on the request, and the whole settings design is about it not getting out.
                throw new SourceException(
                    ErrorCodes.AdoRefused,
                    $"Azure DevOps answered {(int)response.StatusCode} for {path}{Explanation(body)}.");
            }

            return body;
        }
    }

    private static SourceException Unreachable(string reason) =>
        new(ErrorCodes.AdoUnreachable, $"Azure DevOps could not be reached: {reason}");

    /// <summary>
    /// The collection URL is a runtime setting, so HttpClient.BaseAddress is deliberately never set -
    /// one client serves whichever collection the settings currently name.
    ///
    /// Built as a string, which is the safe way - but the plan's reason for it does not hold here, and
    /// three mutations of this method measured on net10.0 say so:
    ///
    /// <list type="bullet">
    /// <item>UriBuilder does <b>not</b> un-escape the <c>%20</c> in the collection name. It keeps it in
    /// the path and in the query alike, and rebuilding this method around one leaves all 36 tests
    /// green. Slice 11 measured a JQL <em>query string</em>; that does not carry over to a path.</item>
    /// <item>Interpolating the base Uri does un-escape - <c>Uri.ToString()</c> gives
    /// <c>/Fake Collection</c> - and it still changes nothing, because <c>new Uri(...)</c> re-escapes
    /// the literal space on the way back in. Also measured: 36 green.</item>
    /// <item>What is observable is <b>double</b> escaping. Escaping a path that already carries
    /// <c>%20</c> sends <c>Fake%2520Collection</c>, and that is the one fault the raw-target assertion
    /// in AdoTaskSourceTests can catch.</item>
    /// </list>
    ///
    /// So a path self-heals. A query string would not, and the batch read has one - which is why this
    /// stays a string rather than being "simplified" now that the trap has been shown to be smaller
    /// than advertised.
    ///
    /// AbsolutePath and not just the authority, because the collection <em>is</em> a path segment here:
    /// dropping it would ask the server for /_apis at the root.
    /// </summary>
    private static Uri UriFor(AdoSettings settings, string path, string? query)
    {
        // Unreachable for a caller that checked AdoSettings.IsConfigured, which is the same test -
        // deliberately, so that flag being true means this cannot throw. Kept anyway, because a
        // configuration problem must not surface as a 500.
        if (settings.BaseUri is not { } baseUri)
        {
            throw new SourceException(
                ErrorCodes.AdoNotConfigured,
                "The Azure DevOps collection URL is not an absolute http or https address.");
        }

        var root = $"{baseUri.GetLeftPart(UriPartial.Authority)}{baseUri.AbsolutePath.TrimEnd('/')}";
        var suffix = query is null ? $"?{ApiVersion}" : $"?{query}&{ApiVersion}";

        return new Uri($"{root}/{path}{suffix}");
    }

    /// <summary>
    /// Azure DevOps puts its reason in a single <c>message</c> string, where Jira uses an
    /// <c>errorMessages</c> array - so the two cannot share this. Measured 2026-08-20: a batch asked
    /// for ids that do not exist answers 400 saying so rather than answering an empty list, and that
    /// sentence is worth passing on. Anything that is not this shape is dropped rather than repeated: a
    /// reverse proxy answers a whole HTML page, and no part of it belongs in an error a user reads.
    /// </summary>
    private static string Explanation(string body)
    {
        try
        {
            return Blank(Read<ErrorBody>(body)?.Message) is { } message ? $": {message}" : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static T? Read<T>(string body) => JsonSerializer.Deserialize<T>(body, Json);

    private sealed record WiqlRequestBody(string Query);

    /// <summary>
    /// <c>asOf</c> is bound but unread: it is the watermark for slice 13's incremental fetch, and
    /// leaving it out of the type would hide that the server hands one over for free.
    /// </summary>
    private sealed record WiqlResponseBody(string? AsOf, List<WiqlItemBody>? WorkItems);

    private sealed record WiqlItemBody(long Id);

    private sealed record BatchBody(int Count, List<WorkItemBody>? Value);

    private sealed record WorkItemBody(long Id, FieldsBody? Fields);

    /// <summary>
    /// Every field is named explicitly, and here there is no choice about it: Azure DevOps' field names
    /// contain dots, so no naming policy could ever reach them. That is the easy half of CLAUDE.md's
    /// duedate lesson. The hard half is that a name can be right and still be the wrong field - see
    /// <see cref="NoteOf"/>.
    ///
    /// CreatedBy is a JsonElement rather than a typed identity because the wire shape is unmeasured -
    /// see <see cref="RequesterOf"/> - and StateChangeDate is a string rather than a DateTimeOffset -
    /// see <see cref="Moment"/>.
    /// </summary>
    private sealed record FieldsBody(
        [property: JsonPropertyName("System.Title")] string? Title,
        [property: JsonPropertyName("System.State")] string? State,
        [property: JsonPropertyName("System.WorkItemType")] string? WorkItemType,
        [property: JsonPropertyName("System.CreatedBy")] JsonElement? CreatedBy,
        [property: JsonPropertyName("System.Description")] string? Description,
        [property: JsonPropertyName("Microsoft.VSTS.TCM.ReproSteps")] string? ReproSteps,
        [property: JsonPropertyName(StateChangeField)] string? StateChangeDate);

    private sealed record ConnectionDataBody(IdentityBody? AuthenticatedUser);

    private sealed record IdentityBody(string? ProviderDisplayName);

    private sealed record ErrorBody(string? Message);
}
