using System.Globalization;
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
        var jql =
            $"project = {ProjectKeyOf(settings)} AND assignee = currentUser() "
            + "AND resolution = Unresolved ORDER BY duedate ASC";

        var items = new List<ExternalTask>();
        var startAt = 0;
        var total = 0;

        while (true)
        {
            var query =
                $"jql={Uri.EscapeDataString(jql)}&startAt={startAt}"
                + $"&maxResults={PageSize}&fields={Fields}";

            var page = Read<SearchBody>(await GetAsync(settings, "search", query, ct));
            var issues = page?.Issues ?? [];

            total = page?.Total ?? 0;

            // An empty page ends the read even when total claims there is more. Advancing by the
            // count means an instance that answers inconsistently would otherwise ask for the same
            // offset forever, and a hung import is worse than a short one.
            if (issues.Count == 0)
            {
                break;
            }

            items.AddRange(issues.Select(Map));
            startAt += issues.Count;

            if (startAt >= total)
            {
                break;
            }
        }

        return new ExternalTaskPage(items, total);
    }

    public Task<DateTime?> FetchStatusChangedAtAsync(string externalKey, CancellationToken ct = default) =>
        // Task 5 fills this in from GET /rest/api/2/issue/{key}?expand=changelog. It is on the
        // interface already so that task changes this one method and nothing around it.
        throw new NotImplementedException(
            "Task 5 reads the status change date out of the issue's changelog.");

    private static ExternalTask Map(IssueBody issue) => new(
        Key: issue.Key ?? string.Empty,
        Title: issue.Fields?.Summary ?? string.Empty,
        // Answers null for an empty description, which is what an unfilled Jira field arrives as.
        Note: WikiMarkup.ToCommonMark(issue.Fields?.Description),
        Deadline: Deadline(issue.Fields?.DueDate),
        Requester: Blank(issue.Fields?.Reporter?.DisplayName),
        StatusName: issue.Fields?.Status?.Name ?? string.Empty);

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
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // The status code and Jira's own words, and nothing of the request: the token is
                // on the request, and slice 11's whole settings design is about it not getting out.
                throw new SourceException(
                    ErrorCodes.JiraRefused,
                    $"Jira answered {(int)response.StatusCode} for {path}{Explanation(body)}.");
            }

            return body;
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

    private sealed record ErrorBody(List<string>? ErrorMessages);
}
