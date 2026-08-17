using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Api.Tests;

public class SettingsEndpointsTests : ApiTest
{
    [Fact]
    public async Task No_language_is_stored_to_begin_with()
    {
        Assert.Null((await GetAsync()).Language);
    }

    [Fact]
    public async Task A_chosen_language_is_stored_and_read_back()
    {
        Assert.Equal("en", (await PutAsync("en")).Language);
        Assert.Equal("en", (await GetAsync()).Language);
    }

    [Fact]
    public async Task Clearing_the_language_removes_the_row_rather_than_storing_null()
    {
        await PutAsync("da");
        Assert.Null((await PutAsync(null)).Language);

        Assert.Null((await GetAsync()).Language);
        Assert.Empty(StoredSettings());
    }

    [Fact]
    public async Task An_unknown_language_is_rejected()
    {
        var response = await PutRawAsync("klingon");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetAsync()).Language);
    }

    [Fact]
    public async Task An_empty_language_is_rejected()
    {
        var response = await PutRawAsync(string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetAsync()).Language);
    }

    [Fact]
    public async Task Choosing_a_language_twice_overwrites_the_one_row()
    {
        await PutAsync("en");
        await PutAsync("en");

        var stored = Assert.Single(StoredSettings());
        Assert.Equal("language", stored.Key);
        Assert.Equal("en", stored.Value);
    }

    private List<Setting> StoredSettings()
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return [.. db.Settings];
    }

    private async Task<SettingsResponse> GetAsync()
    {
        var settings = await Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.NotNull(settings);
        return settings;
    }

    private async Task<SettingsResponse> PutAsync(string? language)
    {
        var response = await PutRawAsync(language);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(settings);
        return settings;
    }

    private Task<HttpResponseMessage> PutRawAsync(string? language)
        => Client.PutAsJsonAsync("/api/settings", new SettingsRequest { Language = language });
}
