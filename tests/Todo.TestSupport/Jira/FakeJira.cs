using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private const string Loopback = "http://127.0.0.1:0";
    private const string Owner = "Thomas";

    /// <summary>
    /// Explicit rather than inherited from the hosting defaults, so the property names this fake
    /// answers with are decided here and cannot drift with a framework default.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Three issues covering the mapping cases: everything filled in, a waiting status, and every
    /// optional field absent. In the order the JQL's ORDER BY duedate leaves them.
    /// </summary>
    private static readonly IssueBody[] Issues =
    [
        new("SAAS-1", new IssueFieldsBody(
            "Kunden kan ikke logge ind", "*vigtigt*", "2026-08-20",
            new PersonBody("Anna Andersen"), new StatusBody("I gang"))),
        new("SAAS-2", new IssueFieldsBody(
            "Venter på svar fra kunden", "h1. Sag", null,
            new PersonBody("Bo Bertelsen"), new StatusBody("Afventer general"))),
        new("SAAS-3", new IssueFieldsBody(
            "Uden noget som helst", null, null, null, new StatusBody("I gang"))),
    ];

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

    /// <summary>The issue key of every changelog read.</summary>
    public List<string> ChangelogRequests { get; } = [];

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
    public JiraTaskSource SourceFor(string projectKey) => JiraTaskSource.With(
        _client,
        new JiraSettings(
            BaseUrl: BaseUrl,
            ProjectKey: projectKey,
            Token: Token,
            WaitingStatuses: [],
            IncludeWaiting: false));

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
            LastJql = request.Query["jql"].ToString();

            var startAt = int.TryParse(request.Query["startAt"], out var asked) ? asked : 0;

            return Results.Json(
                new SearchBody(
                    startAt, _pageSize, Issues.Length, [.. Issues.Skip(startAt).Take(_pageSize)]),
                Json);
        });

        // Task 5 answers this one. Recorded from now, so that task only has to fill in a body.
        app.MapGet("/rest/api/2/issue/{key}", (string key) =>
        {
            ChangelogRequests.Add(key);

            return Results.Json(new ChangelogStubBody(key), Json);
        });
    }

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

    private sealed record ChangelogStubBody(string Key);

    private sealed record ErrorBody(string[] ErrorMessages);
}
