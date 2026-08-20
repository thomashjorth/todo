using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Errors;
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

    /// <summary>
    /// Asserted on the names rather than on "the field is not null": the generated SettingsResponse
    /// carries a <c>new Collection&lt;string&gt;()</c> initializer because the contract makes
    /// delegates required, so Assert.NotNull here could not fail even if the handler never read the
    /// row. Read back through GET as well, so this says the list is in the database and not merely
    /// in one reply.
    /// </summary>
    [Fact]
    public async Task The_delegates_round_trip_as_a_list()
    {
        string[] names = ["Flemming", "Gitte"];

        var saved = await PutJsonAsync(new { delegates = names });

        Assert.Equal(names, saved.Delegates);
        Assert.Equal(names, (await GetAsync()).Delegates);
    }

    /// <summary>
    /// Empty means no row, not a row holding "[]". The claim is about the Settings table because the
    /// response cannot tell the two apart - both read back as an empty list - and a leftover row is
    /// what fells the two language tests in this class.
    /// </summary>
    [Fact]
    public async Task An_empty_list_of_delegates_removes_the_row()
    {
        await PutJsonAsync(new { delegates = new[] { "Flemming" } });

        var stored = Assert.Single(StoredSettings());
        Assert.Equal(SettingKeys.Delegates, stored.Key);

        var after = await PutJsonAsync(new { delegates = Array.Empty<string>() });

        Assert.Empty(after.Delegates);
        Assert.Empty(StoredSettings());
    }

    /// <summary>
    /// Rejected rather than quietly folded to one, the same choice the retro aliases make. A list
    /// where two names became one without anybody saying so is worse than an error - and the second
    /// assertion is the one with teeth: a silent dedup would answer 200 and store the folded list.
    /// </summary>
    [Fact]
    public async Task A_duplicate_delegate_is_rejected_rather_than_folded()
    {
        var response = await PutRawJsonAsync(new { delegates = new[] { "Flemming", "FLEMMING" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.SettingsDuplicateDelegate, error!.Code);
        Assert.Empty((await GetAsync()).Delegates);
    }

    /// <summary>
    /// PUT /api/settings is a full replacement, so an absent field means clear. Said out loud here
    /// because that is the trap the frontend has to carry the field through <c>current</c> to avoid -
    /// slice 9 lost a stored DeferUntil to exactly this shape.
    /// </summary>
    [Fact]
    public async Task An_absent_list_of_delegates_clears_the_stored_one()
    {
        await PutJsonAsync(new { delegates = new[] { "Flemming" } });

        var after = await PutJsonAsync(new { language = "en" });

        Assert.Empty(after.Delegates);
        Assert.Empty((await GetAsync()).Delegates);
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

    /// <summary>
    /// An anonymous body rather than SettingsRequest, so a test can leave a field out - which is the
    /// very thing the full-replacement semantics turn into behaviour.
    /// </summary>
    private async Task<SettingsResponse> PutJsonAsync(object body)
    {
        var response = await PutRawJsonAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(settings);
        return settings;
    }

    private Task<HttpResponseMessage> PutRawJsonAsync(object body)
        => Client.PutAsJsonAsync("/api/settings", body);
}
