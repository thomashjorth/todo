using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.Core.Persistence;
using Todo.Core.Settings;

namespace Todo.Api.Tests;

/// <summary>
/// The two review settings follow AdoDefaults' precedent field for field, and the reason is not
/// tidiness: the default has to be the <em>absence</em> of a row, or the two whole-table assertions
/// in SettingsEndpointsTests go red on a row this feature left behind. Those live in a file about
/// language and point nowhere near here, which is why it is said out loud.
/// </summary>
public class ReviewSettingsTests : ApiTest
{
    [Fact]
    public async Task An_absent_weekday_row_reads_as_Friday()
    {
        var settings = await GetAsync();

        Assert.Equal(5, settings.ReviewWeekday);
        Assert.Equal("15:00", settings.ReviewTimeOfDay);
    }

    /// <summary>
    /// 0 is Sunday, a real weekday rather than a missing value, so it must survive the round trip.
    /// This is the assertion the contract's <c>default: 5</c> exists for: without it an absent field
    /// would arrive as 0 too, and the reader could not tell the two apart.
    /// </summary>
    [Fact]
    public async Task A_deliberate_Sunday_is_preserved_rather_than_folded_to_the_default()
    {
        Assert.Equal(0, (await PutJsonAsync(new { reviewWeekday = 0 })).ReviewWeekday);
        Assert.Equal(0, (await GetAsync()).ReviewWeekday);
    }

    /// <summary>
    /// The guard on the whole precedent, and it is measured rather than assumed: storing the default
    /// unconditionally fells <em>five</em> tests, in three files that between them mention neither
    /// the review nor each other -
    /// SettingsEndpointsTests.Clearing_the_language_removes_the_row_rather_than_storing_null,
    /// .Choosing_a_language_twice_overwrites_the_one_row, .An_empty_list_of_delegates_removes_the_row,
    /// AdoSettingsEndpointsTests.Asking_for_the_default_deadline_leaves_no_row_behind, and this one.
    /// The plan predicted three; the extra two are the tell that the whole-table rule is guarded from
    /// more directions than anybody writing a new setting would look.
    /// </summary>
    [Fact]
    public async Task Saving_the_default_weekday_stores_no_row()
    {
        await PutJsonAsync(new { reviewWeekday = 5, reviewTimeOfDay = "15:00" });

        Assert.Empty(StoredSettings());
    }

    [Fact]
    public async Task A_chosen_weekday_and_time_are_stored_and_read_back()
    {
        var saved = await PutJsonAsync(new { reviewWeekday = 1, reviewTimeOfDay = "09:30" });

        Assert.Equal(1, saved.ReviewWeekday);
        Assert.Equal("09:30", saved.ReviewTimeOfDay);

        var read = await GetAsync();
        Assert.Equal(1, read.ReviewWeekday);
        Assert.Equal("09:30", read.ReviewTimeOfDay);
    }

    [Fact]
    public async Task A_weekday_outside_the_week_is_rejected()
    {
        var response = await PutRawJsonAsync(new { reviewWeekday = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.ReviewWeekdayInvalid, error!.Code);
    }

    [Fact]
    public async Task A_time_that_will_not_parse_is_rejected()
    {
        var response = await PutRawJsonAsync(new { reviewTimeOfDay = "halv fire" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.ReviewTimeOfDayInvalid, error!.Code);
    }

    /// <summary>
    /// A hand-edited row must not stop the app from opening, the same rule SettingList.Read applies
    /// to corrupt JSON. Written straight into the table, because the endpoint refuses it on the way
    /// in - so this is the only way the reader can ever meet it.
    /// </summary>
    [Fact]
    public async Task An_unreadable_stored_time_reads_as_three_in_the_afternoon()
    {
        using (var scope = Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            db.Settings.Add(new Setting { Key = SettingKeys.ReviewTimeOfDay, Value = "25:99" });
            db.Settings.Add(new Setting { Key = SettingKeys.ReviewWeekday, Value = "onsdag" });
            await db.SaveChangesAsync();
        }

        var settings = await GetAsync();

        Assert.Equal("15:00", settings.ReviewTimeOfDay);
        Assert.Equal(5, settings.ReviewWeekday);
    }

    /// <summary>
    /// The drift test compares only paths and methods, so a field serialised under the wrong name is
    /// invisible to it. Asserted on the raw body for the same reason the enum names are.
    /// </summary>
    [Fact]
    public async Task Wire_format_uses_the_names_the_contract_declares()
    {
        var body = await (await Client.GetAsync("/api/settings")).Content.ReadAsStringAsync();

        Assert.Contains("\"reviewWeekday\":5", body);
        Assert.Contains("\"reviewTimeOfDay\":\"15:00\"", body);
    }

    private async Task<SettingsResponse> GetAsync()
    {
        var settings = await Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.NotNull(settings);
        return settings;
    }

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

    private List<Setting> StoredSettings()
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return [.. db.Settings];
    }
}
