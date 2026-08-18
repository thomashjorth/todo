using System.Net;
using System.Net.Http.Json;
using YamlDotNet.Serialization;

namespace Todo.Api.Tests;

public class JiraSettingsEndpointsTests : ApiTest
{
    private const string Token = "a-secret-that-must-not-come-back";

    /// <summary>
    /// The whole reason the token has its own endpoint. This asserts on the raw response body
    /// rather than on a deserialised field, because a leak could arrive under any property name —
    /// including one the contract does not declare and the generated client would drop silently.
    /// </summary>
    [Fact]
    public async Task The_token_never_comes_back_out_of_the_api()
    {
        var stored = await Host.Client.PutAsJsonAsync(
            "/api/settings/jira-token", new { token = Token });

        // Without this the guard passes on a token that was never stored: a route answering 400
        // would leave it looking for a string the system does not have. Assert the precondition,
        // then assert the absence.
        stored.EnsureSuccessStatusCode();

        Assert.True(
            (await stored.Content.ReadFromJsonAsync<SettingsBody>())!.HasJiraToken,
            "The token was not stored, so the leak assertion below would prove nothing.");

        var body = await Host.Client.GetStringAsync("/api/settings");

        Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The documents the app serves must not spell a token out, and this asserts about their
    /// shape rather than searching them for this class's own constant. A search for the constant
    /// was the first version of this test, and it could only ever have failed if someone pasted
    /// that exact string: measured, a real PAT put on JiraTokenRequest.token as an
    /// <c>example:</c> left it green. So instead every property whose name mentions a token is
    /// checked for an <c>example</c> or a <c>default</c> - the two places a real value ends up
    /// when someone documents a field by filling it in - and SettingsResponse's token property is
    /// required to stay a boolean, which is what a <c>jiraToken</c> string added to either
    /// document would break.
    ///
    /// Both documents are read because they fail differently. The contract is the one a person
    /// reads on the documentation page, so an <c>example:</c> lands there; the derivation is what
    /// the code says about itself, so a property added to the response type lands there.
    /// </summary>
    [Fact]
    public async Task No_token_property_in_either_document_carries_a_value()
    {
        foreach (var route in new[] { "/openapi/contract.yaml", "/openapi/v1.json" })
        {
            var document = Parse(await Host.Client.GetStringAsync(route));
            var visited = new List<string>();

            foreach (var (name, schema) in TokenProperties(document))
            {
                visited.Add(name);

                Assert.False(
                    schema.ContainsKey("example"),
                    $"{route}: '{name}' carries an example, which is where a real token gets pasted.");
                Assert.False(
                    schema.ContainsKey("default"),
                    $"{route}: '{name}' carries a default, which is where a real token gets pasted.");
            }

            // Without this the sweep above proves nothing: a walker that found no properties at
            // all would run zero assertions and pass. These two are the token-named properties
            // both documents are known to have.
            Assert.Contains("token", visited);
            Assert.Contains("hasJiraToken", visited);

            // The structural half. Whether a token exists is a boolean; a token is a string. Any
            // token-named property on the response schema being a string means the value itself
            // reached a contract type, which is the thing this whole endpoint split prevents.
            var properties = PropertiesOf(SchemaNamed(document, "SettingsResponse"));
            var tokenNamed = properties
                .Where(p => Named(p.Key).Contains("token", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(tokenNamed);

            foreach (var property in tokenNamed)
            {
                var types = TypesOf((Dictionary<object, object>)property.Value);

                Assert.Contains("boolean", types);
                Assert.DoesNotContain("string", types);
            }
        }
    }

    [Fact]
    public async Task Storing_a_token_shows_up_as_having_one()
    {
        var before = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(before!.HasJiraToken);

        var response = await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.True(after!.HasJiraToken);
    }

    [Fact]
    public async Task Clearing_the_token_removes_it()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        var response = await Host.Client.DeleteAsync("/api/settings/jira-token");

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.False(after!.HasJiraToken);
    }

    [Fact]
    public async Task An_empty_token_is_rejected_rather_than_stored_as_blank()
    {
        var response = await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The regression this slice's own convention warns about. PUT /api/settings is a full
    /// replacement — an absent field means clear — so a settings save must not be able to reach
    /// the token. Slice 9 lost a stored DeferUntil to exactly this shape of bug.
    /// </summary>
    [Fact]
    public async Task Saving_the_other_settings_does_not_clear_the_token()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings", new { language = "en", jiraProjectKey = "SAAS" });

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.True(after!.HasJiraToken);
        Assert.Equal("SAAS", after.JiraProjectKey);
    }

    [Fact]
    public async Task The_waiting_statuses_round_trip_as_a_list()
    {
        string[] names = ["Afventer general", "Venter på support"];

        var response = await Host.Client.PutAsJsonAsync(
            "/api/settings", new { jiraWaitingStatuses = names, jiraIncludeWaiting = true });

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.Equal(names, after!.JiraWaitingStatuses);
        Assert.True(after.JiraIncludeWaiting);
    }

    /// <summary>
    /// What this catches is a reader that reads an absent row as on, which is exactly what the
    /// <c>? "true" : null</c> storage invites: <c>Value(...) != "false"</c> in JiraSettingsReader
    /// fells it. It does not prove the design document's section 4a requirement - with both fields
    /// dropped from ReadAllAsync it still passes. That claim belongs to Task 6's journey tests,
    /// which run all the way through a fake Jira.
    ///
    /// It was the only test that mutation felled when it was written. It no longer is:
    /// Turning_waiting_back_off_turns_it_off catches the same one, from the other end. Kept anyway,
    /// because this is the one that says what a *fresh* database reads as.
    /// </summary>
    [Fact]
    public async Task Waiting_issues_are_excluded_until_asked_for()
    {
        var settings = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(settings!.JiraIncludeWaiting);
        Assert.Empty(settings.JiraWaitingStatuses);
    }

    /// <summary>
    /// Storing on as a row and off as no row at all is asymmetric, so the way back needs its own
    /// test: dropping the clearing branch leaves every other test in this class green.
    /// </summary>
    [Fact]
    public async Task Turning_waiting_back_off_turns_it_off()
    {
        await Host.Client.PutAsJsonAsync(
            "/api/settings",
            new { jiraWaitingStatuses = new[] { "Afventer general" }, jiraIncludeWaiting = true });

        var response = await Host.Client.PutAsJsonAsync("/api/settings", new { jiraIncludeWaiting = false });

        response.EnsureSuccessStatusCode();

        var after = await response.Content.ReadFromJsonAsync<SettingsBody>();

        Assert.False(after!.JiraIncludeWaiting);
        Assert.Empty(after.JiraWaitingStatuses);
    }

    /// <summary>
    /// YAML is a superset of JSON, so one deserialiser reads both documents and one walker below
    /// covers them - the alternative was two walkers with the same assertions written twice.
    /// </summary>
    private static object Parse(string text) =>
        new DeserializerBuilder().Build().Deserialize<object>(text)
            ?? throw new InvalidOperationException("The document parsed to nothing.");

    /// <summary>
    /// Every property whose name mentions a token, anywhere in the document - recursive on purpose,
    /// so an inline schema nested inside an operation is not a blind spot.
    /// </summary>
    private static IEnumerable<(string Name, Dictionary<object, object> Schema)> TokenProperties(object? node)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach (var entry in map)
                {
                    if (Named(entry.Key) == "properties" && entry.Value is Dictionary<object, object> properties)
                    {
                        foreach (var property in properties)
                        {
                            if (Named(property.Key).Contains("token", StringComparison.OrdinalIgnoreCase)
                                && property.Value is Dictionary<object, object> schema)
                            {
                                yield return (Named(property.Key), schema);
                            }
                        }
                    }

                    foreach (var found in TokenProperties(entry.Value))
                    {
                        yield return found;
                    }
                }

                break;

            case List<object> list:
                foreach (var item in list)
                {
                    foreach (var found in TokenProperties(item))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }

    private static string Named(object? key) => key as string ?? string.Empty;

    private static Dictionary<object, object> SchemaNamed(object document, string name)
    {
        var components = (Dictionary<object, object>)((Dictionary<object, object>)document)["components"];
        var schemas = (Dictionary<object, object>)components["schemas"];

        return (Dictionary<object, object>)schemas[name];
    }

    private static Dictionary<object, object> PropertiesOf(Dictionary<object, object> schema) =>
        (Dictionary<object, object>)schema["properties"];

    /// <summary>
    /// The contract writes a type as a scalar; the derivation writes a nullable one as a list,
    /// <c>["null", "string"]</c>. Both end up as a set here so the assertions read the same way.
    /// </summary>
    private static IReadOnlyCollection<string> TypesOf(Dictionary<object, object> schema) =>
        schema.TryGetValue("type", out var type)
            ? type switch
            {
                List<object> many => [.. many.Select(Named)],
                _ => new[] { Named(type) },
            }
            : [];

    private sealed record SettingsBody(
        string? Language,
        string? JiraBaseUrl,
        string? JiraProjectKey,
        string[] JiraWaitingStatuses,
        bool JiraIncludeWaiting,
        bool HasJiraToken);
}
