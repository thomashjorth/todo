using System.Net;
using System.Net.Http.Json;

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
    /// A separate assertion with a separate reason. The derived document carries schema, not
    /// values, so it cannot catch a leak in the response builder — what it can catch is a real
    /// token pasted into an `example:` on the contract.
    /// </summary>
    [Fact]
    public async Task The_openapi_document_carries_no_token()
    {
        await Host.Client.PutAsJsonAsync("/api/settings/jira-token", new { token = Token });

        foreach (var path in new[] { "/openapi/v1.json", "/openapi/contract.yaml" })
        {
            var body = await Host.Client.GetStringAsync(path);

            Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
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
    /// Default off, and the design document's section 4a says why: an import that silently pulled
    /// waiting issues in would fill the list with things you cannot act on.
    /// </summary>
    [Fact]
    public async Task Waiting_issues_are_excluded_until_asked_for()
    {
        var settings = await Host.Client.GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.False(settings!.JiraIncludeWaiting);
        Assert.Empty(settings.JiraWaitingStatuses);
    }

    private sealed record SettingsBody(
        string? Language,
        string? JiraBaseUrl,
        string? JiraProjectKey,
        string[] JiraWaitingStatuses,
        bool JiraIncludeWaiting,
        bool HasJiraToken);
}
