using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
using Todo.TestSupport;
using Todo.TestSupport.Jira;
using Todo.Tunnel;

namespace Todo.Helper.Tests;

/// <summary>
/// The whole chain, nothing played: the real host in Helper mode, the real helper with its own store,
/// and a FakeJira that wants the user's token. The one claim the design rests on is measured here -
/// the token reaches Jira, and never the host.
/// </summary>
public class TunnelIntegrationTests
{
    private const string Key = "the-helpers-key";

    [Fact]
    public async Task A_jira_call_goes_through_the_helper_with_its_token_and_the_host_never_holds_it()
    {
        await using var jira = await FakeJira.StartAsync();
        await using var host = await RunningHost.StartAsync(
            "--Sources:Mode", "Helper",
            "--Helper:KeyHash", PasswordHash.Create(Key, iterations: 1));
        await using var helper = await RunningHelper.StartAsync(new HelperSettings
        {
            ServerUrl = host.BaseUrl,
            HelperKey = Key,
            Jira = new SourceSettings { BaseUrl = jira.BaseUrl, Token = FakeJira.Token },
            // A closed port, so nothing here can reach the user's real Ollama on 11434.
            OllamaUrl = $"http://127.0.0.1:{RunningHelper.FreePort()}",
        });

        await WaitUntilOnlineAsync(host);

        var response = await host.Client.PostAsync("/api/jira/test", null);

        response.EnsureSuccessStatusCode();
        Assert.Equal("Bearer", jira.LastAuthorizationScheme);
        Assert.Equal(FakeJira.Token, jira.LastAuthorizationParameter);

        // And never the host: not in its answers, not in its database.
        Assert.DoesNotContain(FakeJira.Token, await host.Client.GetStringAsync("/api/settings"));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        Assert.False(
            await db.Settings.AnyAsync(s => s.Value.Contains(FakeJira.Token)),
            "A setting row holds the token.");
    }

    /// <summary>
    /// A changed address in the helper reaches the host without the tunnel dropping: the helper says
    /// hello again, and the host's row follows.
    /// </summary>
    [Fact]
    public async Task A_base_url_changed_in_the_helper_reaches_the_host()
    {
        await using var host = await RunningHost.StartAsync(
            "--Sources:Mode", "Helper",
            "--Helper:KeyHash", PasswordHash.Create(Key, iterations: 1));
        await using var helper = await RunningHelper.StartAsync(new HelperSettings
        {
            ServerUrl = host.BaseUrl,
            HelperKey = Key,
            Jira = new SourceSettings { BaseUrl = "https://jira.first", Token = "t" },
        });

        await WaitUntilOnlineAsync(host);

        helper.Services.GetRequiredService<HelperStore>().Update(s => s with
        {
            Jira = s.Jira with { BaseUrl = "https://jira.second" },
        });

        await WaitUntilAsync(host, s => s.JiraBaseUrl == "https://jira.second");
    }

    private static Task WaitUntilOnlineAsync(RunningHost host) => WaitUntilAsync(host, s => s.HelperOnline);

    private static async Task WaitUntilAsync(RunningHost host, Func<SettingsResponse, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            if (condition((await host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("The host never saw what the helper said within fifteen seconds.");
    }
}
