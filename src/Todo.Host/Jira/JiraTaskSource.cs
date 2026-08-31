using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Todo.Core.Errors;
using Todo.Core.Jira;
using Todo.Core.Sources;

namespace Todo.Host.Jira;

/// <summary>
/// Reads the issues assigned to you out of one Jira Data Center. Measured against the real instance
/// 2026-08-18, and the measurements are what decide the shape: 10.3.24 serves REST v2, takes a
/// personal access token as <c>Authorization: Bearer</c>, and pages classically with
/// <c>startAt</c>/<c>maxResults</c>/<c>total</c> rather than Cloud's <c>nextPageToken</c>.
/// </summary>
public sealed partial class JiraTaskSource : ITaskSource
{
    /// <summary>What goes in <c>TaskItem.SourceId</c>, and half of the deduplication key.</summary>
    public const string Id = "jira";

    private const string ApiRoot = "rest/api/2";

    /// <summary>
    /// Only the fields that are actually mapped. Jira returns every field of every issue otherwise,
    /// which on this project is a few hundred kilobytes per page of nothing anyone reads.
    ///
    /// The reconciliation's ownership search asks for this same list although it reads none of it -
    /// its answer is the issue key, which Jira puts beside fields rather than inside them. A shorter
    /// list would be a second constant to keep true for a saving nobody has measured, and asking for
    /// nothing is the expensive case rather than the cheap one.
    /// </summary>
    private const string Fields = "summary,description,duedate,reporter,status";

    /// <summary>
    /// What to ask for. Jira caps this at the instance's own limit and says what it actually used
    /// in the response, so this is a request rather than a promise — which is why the paging loop
    /// below counts the issues it got instead of trusting this number.
    /// </summary>
    private const int PageSize = 50;

    /// <summary>
    /// Status names are shown to a Danish user, so they sort the Danish way: æ, ø and å come after
    /// z, where their code points put å and æ among the accented A's. Measured on net10.0 with ICU.
    /// </summary>
    private static readonly StringComparer DanishOrder =
        StringComparer.Create(new CultureInfo("da-DK"), ignoreCase: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<JiraSettings>> _settings;

    /// <summary>
    /// The one constructor DI sees. The settings are read per call rather than captured, because
    /// the user can change the Jira, the project or the token while the app is running.
    /// </summary>
    public JiraTaskSource(HttpClient client, JiraSettingsReader reader)
        : this(client, reader.ReadAsync)
    {
    }

    private JiraTaskSource(HttpClient client, Func<CancellationToken, Task<JiraSettings>> settings)
    {
        _client = client;
        _settings = settings;
    }

    /// <summary>
    /// For a caller that already holds the settings and has no database to read them from. A second
    /// public constructor would be ambiguous to ActivatorUtilities, which picks a typed client's
    /// constructor by counting parameters it can resolve.
    /// </summary>
    public static JiraTaskSource With(HttpClient client, JiraSettings settings) =>
        new(client, _ => Task.FromResult(settings));

    public string SourceId => Id;

    public async Task<SourceIdentity> TestAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var body = await GetAsync(settings, "myself", query: null, ct);

        return new SourceIdentity(Read<MyselfBody>(body)?.DisplayName ?? string.Empty);
    }

    public async Task<IReadOnlyList<string>> FetchStatusNamesAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var key = ProjectKeyOf(settings);
        var body = await GetAsync(settings, $"project/{key}/statuses", query: null, ct);

        // Jira answers one group per issue type, and the same status appears in several of them, so
        // flattening without Distinct would offer the user "I gang" three times.
        return
        [
            .. (Read<List<IssueTypeStatusesBody>>(body) ?? [])
                .SelectMany(group => group.Statuses ?? [])
                .Select(status => status.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, DanishOrder)
        ];
    }

    public async Task<ExternalTaskPage> FetchAssignedAsync(CancellationToken ct = default)
    {
        var settings = await _settings(ct);

        // Built before the search, so a status name that cannot go in a query is refused before any
        // request goes out rather than after the first page has already been read.
        var jql = JqlFor(settings);

        var (issues, total) = await SearchAsync(settings, jql, ct);

        return new ExternalTaskPage([.. issues.Select(Map)], total);
    }

    /// <summary>
    /// One JQL read to the end of its pages. Classic paging, measured 2026-08-18: startAt,
    /// maxResults, total and issues, rather than Cloud's nextPageToken.
    ///
    /// Shared by the fetch and by the reconciliation's ownership question, and the sharing is
    /// load-bearing rather than tidy: a missed page fails differently on the two sides and silently
    /// on both. The fetch would import a short list; the ownership search would leave the owners it
    /// never read out of StillMine, which the app reads as "assigned to somebody else" and offers to
    /// close. Jira caps maxResults at its own limit and reports what it used, so the page size here
    /// is a request rather than a promise and neither caller may assume one page is enough.
    ///
    /// Total is re-read on every page rather than only the first, which is what the loop has always
    /// done: Jira answers it per page, and the last word is the one the caller shows.
    ///
    /// An empty page ends the read even when total claims there is more. Advancing by the count means
    /// an instance that answers inconsistently would otherwise ask for the same offset forever, and a
    /// hung import is worse than a short one.
    /// </summary>
    private async Task<(List<IssueBody> Issues, int Total)> SearchAsync(
        JiraSettings settings, string jql, CancellationToken ct)
    {
        var issues = new List<IssueBody>();
        var startAt = 0;
        var total = 0;

        while (true)
        {
            var query =
                $"jql={Uri.EscapeDataString(jql)}&startAt={startAt}"
                + $"&maxResults={PageSize}&fields={Fields}";

            var page = Read<SearchBody>(await GetAsync(settings, "search", query, ct));
            var got = page?.Issues ?? [];

            total = page?.Total ?? 0;

            if (got.Count == 0)
            {
                break;
            }

            issues.AddRange(got);
            startAt += got.Count;

            if (startAt >= total)
            {
                break;
            }
        }

        return (issues, total);
    }

    /// <summary>
    /// Slice 11's query, widened with the duty pool only when the rotation is on and there is
    /// something to widen it with. The two forms are written out in full rather than assembled from
    /// shared fragments, so the off-duty string stays readable as the one it has always been —
    /// rewriting it would make <c>Off_duty_the_query_is_unchanged</c> a test of nothing.
    ///
    /// Note where the parentheses go. The project and the resolution stay conjunctions and only the
    /// assignee joins the disjunction: <c>a AND b OR c</c> without them would let any unresolved
    /// issue in the pool through from <em>any</em> project, because JQL binds AND tighter than OR.
    ///
    /// The emptiness check is not tidiness. <c>status IN ()</c> is a JQL syntax error, so a source
    /// that emitted it would fail against the real instance on every single import while a fake
    /// happily answered whatever it was asked.
    /// </summary>
    private static string JqlFor(JiraSettings settings)
    {
        var key = ProjectKeyOf(settings);

        // Blanks out and the rest trimmed, both before the emptiness check below rather than after,
        // so a list holding nothing but blanks behaves as an empty one. They are one fix because they
        // are one fault: " " becomes status IN (" ") and "  Afventer general  " becomes
        // status IN ("  Afventer general  "), and both are syntactically valid JQL matching nothing —
        // silently, where the empty IN list at least announces itself as a syntax error. Closing only
        // the first would be worse than closing neither, because the class would look closed.
        //
        // Blanks are dropped before the trim so a null could not be dereferenced, and the trim is the
        // only one of its kind on this value: slice 11 measured JiraSettings.BrowseUrl and
        // PUT /api/settings both trimming the base URL, arriving in the same commit, so one of them
        // had never been able to fire and nobody could say which. Here rather than in
        // SettingsEndpoints.StatusList for the same reason the filtering is: a row stored before a
        // validation existed outlives it, and this is what builds the query.
        var duty = settings.DutyStatuses
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();

        if (!settings.OnDuty || duty.Count == 0)
        {
            return $"project = {key} AND assignee = currentUser() "
                + "AND resolution = Unresolved ORDER BY duedate ASC";
        }

        var names = string.Join(", ", duty.Select(StatusLiteral));

        return $"project = {key} AND resolution = Unresolved "
            + $"AND (assignee = currentUser() OR status IN ({names})) "
            + "ORDER BY duedate ASC";
    }

    /// <summary>
    /// One status name as the quoted literal JQL wants. A blocklist rather than the project key's
    /// character whitelist, and deliberately so: a status name may legally carry spaces, slashes and
    /// Danish letters — <c>Afventer PO/FA</c> and <c>Venter på support</c> are real names off the
    /// instance — so a whitelist would refuse the very values this feature exists for. The two
    /// characters JQL quoting turns on, <c>"</c> and <c>\</c>, are a short and complete list instead.
    ///
    /// Refused rather than escaped, because the picker only ever offers names the instance itself
    /// reported. A name carrying one of these is a bug somewhere else, and papering over it would
    /// hide that. The message says nothing of the request, for the same reason as everywhere else
    /// here: the token rides on the request.
    /// </summary>
    private static string StatusLiteral(string name)
    {
        if (name.Contains('"') || name.Contains('\\'))
        {
            throw new SourceException(
                ErrorCodes.JiraStatusNameInvalid,
                "A Jira status name cannot contain a quotation mark or a backslash.");
        }

        return $"\"{name}\"";
    }

    /// <summary>
    /// Measured 2026-08-18: 10.3.24 does not return <c>statuscategorychangedate</c>, so the cheap
    /// field is not there to read. <c>expand=changelog</c> does work, and it answered
    /// <c>2026-08-17T14:10:13.593+0200</c> — the created date of the newest history entry carrying a
    /// status item, which is what the wait is counted from. The expand is not optional: without it
    /// Jira leaves the changelog out of the issue entirely and every answer here would be null.
    /// </summary>
    public async Task<DateTime?> FetchStatusChangedAtAsync(
        string externalKey, CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var body = await GetAsync(
            settings, $"issue/{Uri.EscapeDataString(externalKey)}", "expand=changelog", ct);

        var newest = Read<IssueDetailBody>(body)?.Changelog?.Histories
            ?.Where(history => history.Items?.Any(
                item => string.Equals(item.Field, "status", StringComparison.OrdinalIgnoreCase))
                    == true)
            .Select(history => Moment(history.Created))
            .Where(created => created is not null)
            .OrderByDescending(created => created!.Value)
            .FirstOrDefault();

        // Flattened to a UTC DateTime at the boundary: SQLite cannot sort a DateTimeOffset, so one
        // must never reach the entity. The offset is honoured before it is dropped, not ignored.
        return newest?.UtcDateTime;
    }

    /// <summary>
    /// What Jira says about keys the app already holds: one <c>GET issue/{key}</c> for each of them,
    /// and then one JQL naming only the keys that answered. A 404 <em>is</em> an answer here - the
    /// key is one the source does not know - which is why <see cref="GetOrNullAsync"/> stands beside
    /// <see cref="GetAsync"/> rather than being a flag on it.
    ///
    /// <b>The shape is not Azure DevOps', and the difference is the deleted issue.</b> The elegant
    /// form - one search naming every key - is the most fragile one available: Jira answers 400 for
    /// the whole query when a <c>key IN</c> list names an issue it does not know, so one deleted issue
    /// would take the reconciliation down on the very case it exists to report. The search that
    /// follows the reads cannot fail that way, because every key in it has just answered 200. The
    /// price is a known one rather than a new one: Jira already pays a changelog call per waiting row,
    /// so N small reads is the same cost model. It also keeps the URL short, which matters because
    /// /search is a GET here and a JQL naming hundreds of keys approaches Data Center's limit on a
    /// request line.
    ///
    /// <b>Unmeasured against the real instance, in two places</b>, and they are the two the shape
    /// rests on: that <c>GET issue/{key}</c> answers <b>404</b> for a deleted issue rather than 400 or
    /// 403, and that a <c>key IN</c> list naming an unknown key really does fail. Nobody has put
    /// either question to that server. A green suite therefore says the source asks per key, keeps an
    /// unfound key out of the JQL and reads the answers correctly - it does not say Jira answers this
    /// way. If the first assumption is wrong, a deleted issue raises instead of being reported and the
    /// preview fails on the case the feature was written for; if the second is wrong, this shape is
    /// merely more expensive than it needed to be. The delivery's own history is the argument for
    /// labelling rather than asserting: Azure DevOps' batch read was predicted to answer 404 and
    /// measured at 400.
    ///
    /// <b>Both sets answer in the caller's spelling of a key, never Jira's.</b> Jira canonicalises an
    /// issue key's case, so a set built out of the response would fail the caller's own Contains
    /// silently and every reconciled issue would read as one the source does not know - which is the
    /// outcome that offers to close the task. Each read therefore keeps the key it was asked with, and
    /// the search's answer is matched back against those keys case-insensitively instead of being
    /// copied out of the response. Case is the only canonicalisation allowed for; anything else Jira
    /// might do to a key lands in neither set, which is the same silent wrong direction.
    ///
    /// <b>No project key is required.</b> The fetch demands one because without it its JQL would ask
    /// for every project the token can see - slice 11's lesson that an absent limit is not a neutral
    /// default. Here the <c>key IN</c> list is the limit, so demanding a setting this query does not
    /// use would be a refusal with nothing behind it.
    /// </summary>
    public async Task<ExternalReconciliation> ReconcileAsync(
        IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        var settings = await _settings(ct);
        var found = new List<ExternalTask>();

        foreach (var key in keys)
        {
            // Only a 404 is an absence. Anything else - a 403 on an issue the token may not read, a
            // 500 from the instance - is raised, because absence is what the app answers by offering
            // to close the task, and one expired permission must not read as "all of this is gone".
            var body = await GetOrNullAsync(
                settings, $"issue/{Uri.EscapeDataString(key)}", $"fields={Fields}", ct);

            if (body is null || Read<IssueBody>(body) is not { } issue)
            {
                continue;
            }

            // The key as the caller wrote it, and no deadline. Both are the contract's rules rather
            // than this source's, and ExternalReconciliation's two bullets are these two overrides:
            // Map fills the deadline from duedate for the fetch, so here it has to be cleared rather
            // than inherited - Azure DevOps has no due date to carry and passes null anyway, and one
            // source carrying one while the other does not is an asymmetry nothing else would notice.
            found.Add(Map(issue) with { Key = key, Deadline = null });
        }

        // Nothing answered, so there is no key left to name - and asking anyway would emit
        // key IN (), a JQL syntax error, the same fault JqlFor guards against one clause over. Not a
        // case that cannot happen: the deleted issue is ordinary here, and a list holding one is the
        // whole of it.
        if (found.Count == 0)
        {
            return new ExternalReconciliation([], new HashSet<string>(StringComparer.Ordinal));
        }

        var owned = await OwnersAsync(settings, found, ct);

        return new ExternalReconciliation(
            found,
            new HashSet<string>(
                found.Select(item => item.Key).Where(owned.Contains), StringComparer.Ordinal));
    }

    /// <summary>
    /// Which of the found keys are still assigned to the user, asked as one JQL. <c>currentUser()</c>
    /// is Jira's own word for the question, so the answer is the server's rather than a comparison
    /// made here - and it has to be: MyselfBody binds a display name and nothing else, so comparing on
    /// one would break the day somebody corrects a surname.
    ///
    /// The keys go in unquoted and unescaped, and nothing hostile can reach the clause: every one of
    /// them has just been resolved by Jira itself through <c>GET issue/{key}</c>, and a key Jira
    /// cannot parse is a 404 that never gets this far. That is a property of the flow rather than of
    /// what keys happen to look like.
    ///
    /// No resolution clause and no project clause. This asks one question - is it still mine - and the
    /// key list is what bounds it. A resolved issue has to stay in the answer, because "resolved and
    /// still mine" is one of the four outcomes and "resolved and handed over" is another.
    /// </summary>
    private async Task<HashSet<string>> OwnersAsync(
        JiraSettings settings, List<ExternalTask> found, CancellationToken ct)
    {
        var jql = $"key IN ({string.Join(", ", found.Select(item => item.Key))}) "
            + "AND assignee = currentUser()";

        var (issues, _) = await SearchAsync(settings, jql, ct);

        // Case-insensitive, because what comes back is Jira's spelling of a key and what goes out is
        // the caller's. This set is only ever asked about; nothing is read out of it.
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in issues)
        {
            if (issue.Key is { } key)
            {
                owned.Add(key);
            }
        }

        return owned;
    }

    /// <summary>
    /// Bound as a string and parsed here rather than typed as a DateTimeOffset on the DTO. Measured
    /// against this repo's fake, which serves the offset exactly as the real instance writes it:
    /// Jira uses the basic form <c>+0200</c>, System.Text.Json accepts only the extended ISO 8601
    /// form <c>+02:00</c>, and a typed field throws "The JSON value is not in a supported
    /// DateTimeOffset format" on every history entry. DateTimeOffset.Parse takes both forms.
    ///
    /// Invariant culture on purpose: under da-DK the current culture reads a date as dd-MM-yyyy.
    /// An unreadable value answers null rather than throwing, so one odd history entry costs its own
    /// waiting date instead of the whole import.
    /// </summary>
    private static DateTimeOffset? Moment(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var moment)
            ? moment
            : null;

    private static ExternalTask Map(IssueBody issue) => new(
        Key: issue.Key ?? string.Empty,
        Title: issue.Fields?.Summary ?? string.Empty,
        // Answers null for an empty description, which is what an unfilled Jira field arrives as.
        Note: WikiMarkup.ToCommonMark(issue.Fields?.Description),
        Deadline: Deadline(issue.Fields?.DueDate),
        Requester: Blank(issue.Fields?.Reporter?.DisplayName),
        StatusName: issue.Fields?.Status?.Name ?? string.Empty,
        // Both added in slice 12 and both null here, deliberately. The issue type is not asked for in
        // Fields above, and nothing in slice 11 or since has needed it, so saying nothing beats
        // inventing something. The status change date is the one Jira DC 10.3.24 does not return at
        // all - that is why FetchStatusChangedAtAsync exists - so a value here would be a guess where
        // null is the truth, and it is what makes the caller fall back to the changelog call.
        ItemType: null,
        StatusChangedAt: null);

    /// <summary>
    /// Jira writes a due date as <c>2026-08-20</c>. Parsed invariantly on purpose: under da-DK the
    /// current culture reads a date as dd-MM-yyyy, and an ISO date would fail there silently.
    /// </summary>
    private static DateOnly? Deadline(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The project key goes into JQL, and it comes from a settings field the user typed. Validated
    /// against Jira's own shape rather than escaped, because JQL quoting is its own dialect and a
    /// key that does not look like a key cannot match a project anyway.
    /// </summary>
    private static string ProjectKeyOf(JiraSettings settings)
    {
        var key = settings.ProjectKey?.Trim() ?? string.Empty;

        if (!ProjectKeyPattern().IsMatch(key))
        {
            throw new SourceException(
                ErrorCodes.JiraProjectKeyRequired,
                "A Jira project key is uppercase letters, digits and underscores, "
                + "starting with a letter.");
        }

        return key;
    }

    private async Task<string> GetAsync(
        JiraSettings settings, string path, string? query, CancellationToken ct) =>
        Interpret(path, await SendAsync(settings, path, query, ct));

    /// <summary>
    /// The same read, except that a <b>404 answers null</b> instead of raising. Its own method rather
    /// than a flag on <see cref="GetAsync"/>, so that no later caller can pass a true it had not
    /// thought about and swallow a 404 that meant something: /myself, the project's status list and
    /// the changelog all have paths that can answer 404 for reasons their callers would want to hear.
    ///
    /// Every other failure still raises, and that line is the whole of this method. Absence is what
    /// the reconciliation reports as "the source does not know this key", which the app answers by
    /// offering to close the task - so a 403 read as an absence would turn one expired permission
    /// into an invitation to close everything it covered.
    /// </summary>
    private async Task<string?> GetOrNullAsync(
        JiraSettings settings, string path, string? query, CancellationToken ct)
    {
        var answer = await SendAsync(settings, path, query, ct);

        return answer.Status == HttpStatusCode.NotFound ? null : Interpret(path, answer);
    }

    /// <summary>
    /// A failure becomes a SourceException carrying the status code and Jira's own words, and nothing
    /// of the request: the token is on the request, and slice 11's whole settings design is about it
    /// not getting out.
    /// </summary>
    private static string Interpret(string path, Answer answer) => answer.Success
        ? answer.Body
        : throw new SourceException(
            ErrorCodes.JiraRefused,
            $"Jira answered {(int)answer.Status} for {path}{Explanation(answer.Body)}.");

    /// <summary>
    /// The request itself, reporting what Jira said rather than deciding what it meant. Success is
    /// HttpClient's own verdict rather than a status range written out again here, so the two readers
    /// above cannot come to disagree with it about what counts as one.
    ///
    /// Being unreachable is not a status and stays an exception: there is no answer to interpret, and
    /// a caller willing to read a 404 as an absence must not read a dead host as one too.
    /// </summary>
    private async Task<Answer> SendAsync(
        JiraSettings settings, string path, string? query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UriFor(settings, path, query));

        // Per request rather than on the client. The client is shared and long-lived, so a header
        // set on it would outlive a token the user has since cleared.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

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
            // HttpClient throws this both for its own timeout and for a cancelled call, and
            // "A task was canceled" in a 500 tells the user nothing they can act on.
            throw Unreachable("it did not answer in time");
        }

        using (response)
        {
            return new Answer(
                response.IsSuccessStatusCode,
                response.StatusCode,
                await response.Content.ReadAsStringAsync(ct));
        }
    }

    private static SourceException Unreachable(string reason) =>
        new(ErrorCodes.JiraUnreachable, $"Jira could not be reached: {reason}");

    /// <summary>
    /// The base URL is a runtime setting, so HttpClient.BaseAddress is deliberately never set — one
    /// client serves whichever Jira the settings currently name. Built as a string rather than with
    /// UriBuilder, which unescapes: measured, UriBuilder turns the JQL's %20 back into a literal
    /// space while leaving %3D alone, producing a query string with raw spaces in it.
    /// </summary>
    private static Uri UriFor(JiraSettings settings, string path, string? query)
    {
        // Unreachable for a caller that checked JiraSettings.IsConfigured, which is the same test —
        // deliberately, so that flag being true means this cannot throw. Kept anyway, because there
        // is no endpoint checking it yet, and a configuration problem must not surface as a 500.
        if (settings.BaseUri is not { } baseUri)
        {
            throw new SourceException(
                ErrorCodes.JiraNotConfigured,
                "The Jira base URL is not an absolute http or https address.");
        }

        // AbsolutePath, not just the authority: Jira Data Center is often deployed under a context
        // path such as https://host/jira, and dropping it would ask the reverse proxy for /rest.
        var root = $"{baseUri.GetLeftPart(UriPartial.Authority)}{baseUri.AbsolutePath.TrimEnd('/')}";
        var suffix = query is null ? string.Empty : $"?{query}";

        return new Uri($"{root}/{ApiRoot}/{path}{suffix}");
    }

    /// <summary>
    /// Jira puts its reason in <c>errorMessages</c>. Anything that is not that shape is dropped
    /// rather than repeated: a reverse proxy in front of Jira answers a whole HTML page, and no
    /// part of it belongs in an error a user reads.
    /// </summary>
    private static string Explanation(string body)
    {
        try
        {
            var messages = Read<ErrorBody>(body)?.ErrorMessages ?? [];

            return messages.Count == 0 ? string.Empty : $": {string.Join(" ", messages)}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static T? Read<T>(string body) => JsonSerializer.Deserialize<T>(body, Json);

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex ProjectKeyPattern();

    /// <summary>
    /// One answer from Jira, before anybody has decided whether it is a failure. The status rides
    /// along beside the verdict because a 404 means an absence to one caller and a failure to
    /// another - which is the whole reason this type exists rather than the body alone.
    /// </summary>
    private sealed record Answer(bool Success, HttpStatusCode Status, string Body);

    private sealed record MyselfBody(string? DisplayName);

    private sealed record IssueTypeStatusesBody(List<StatusBody>? Statuses);

    private sealed record StatusBody(string Name);

    private sealed record PersonBody(string? DisplayName);

    private sealed record SearchBody(int Total, List<IssueBody>? Issues);

    private sealed record IssueBody(string? Key, IssueFieldsBody? Fields);

    /// <summary>
    /// Jira spells the due date <c>duedate</c>, all one word, so the web naming policy's
    /// <c>dueDate</c> would read as absent and every deadline would arrive null.
    /// </summary>
    private sealed record IssueFieldsBody(
        string? Summary,
        string? Description,
        [property: JsonPropertyName("duedate")] string? DueDate,
        PersonBody? Reporter,
        StatusBody? Status);

    private sealed record IssueDetailBody(ChangelogBody? Changelog);

    private sealed record ChangelogBody(List<HistoryBody>? Histories);

    /// <summary>
    /// <c>Created</c> is a string because Jira's offset format is not one System.Text.Json can bind
    /// to a DateTimeOffset — see <see cref="Moment"/>. Everything else binds off the web naming
    /// policy: <c>changelog</c>, <c>histories</c>, <c>created</c>, <c>items</c> and <c>field</c> are
    /// each one word, unlike <c>duedate</c>, so none of them needs a JsonPropertyName. Verified by
    /// reading real values out, not assumed.
    /// </summary>
    private sealed record HistoryBody(string? Created, List<HistoryItemBody>? Items);

    private sealed record HistoryItemBody(string? Field);

    private sealed record ErrorBody(List<string>? ErrorMessages);
}
