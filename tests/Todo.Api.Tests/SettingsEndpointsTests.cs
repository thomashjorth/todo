using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class SettingsEndpointsTests
{
    [Fact]
    public async Task No_language_is_stored_to_begin_with()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.Null((await GetAsync(host)).Language);
    }

    [Fact]
    public async Task A_chosen_language_is_stored_and_read_back()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.Equal("en", (await PutAsync(host, "en")).Language);
        Assert.Equal("en", (await GetAsync(host)).Language);
    }

    [Fact]
    public async Task Clearing_the_language_removes_the_row_rather_than_storing_null()
    {
        await using var host = await RunningHost.StartAsync();

        await PutAsync(host, "da");
        Assert.Null((await PutAsync(host, null)).Language);

        Assert.Null((await GetAsync(host)).Language);
        Assert.Empty(StoredSettings(host));
    }

    [Fact]
    public async Task An_unknown_language_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await PutRawAsync(host, "klingon");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetAsync(host)).Language);
    }

    [Fact]
    public async Task An_empty_language_is_rejected()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await PutRawAsync(host, string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetAsync(host)).Language);
    }

    [Fact]
    public async Task Choosing_a_language_twice_overwrites_the_one_row()
    {
        await using var host = await RunningHost.StartAsync();

        await PutAsync(host, "en");
        await PutAsync(host, "en");

        var stored = Assert.Single(StoredSettings(host));
        Assert.Equal("language", stored.Key);
        Assert.Equal("en", stored.Value);
    }

    private static List<Setting> StoredSettings(RunningHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return [.. db.Settings];
    }

    private static async Task<SettingsResponse> GetAsync(RunningHost host)
    {
        var settings = await host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.NotNull(settings);
        return settings;
    }

    private static async Task<SettingsResponse> PutAsync(RunningHost host, string? language)
    {
        var response = await PutRawAsync(host, language);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(settings);
        return settings;
    }

    private static Task<HttpResponseMessage> PutRawAsync(RunningHost host, string? language)
        => host.Client.PutAsJsonAsync("/api/settings", new SettingsRequest { Language = language });
}
