using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Ado;
using Todo.Core.Errors;
using Todo.Core.Persistence;
using Todo.Core.Settings;
using Todo.TestSupport;
using YamlDotNet.Serialization;

namespace Todo.Api.Tests;

/// <summary>
/// The seven ADO settings, measured through the API rather than through the reader: what a fresh
/// database answers, what a save stores, and what it refuses.
/// </summary>
public class AdoSettingsEndpointsTests : ApiTest
{
    private const string Token = "an-ado-secret-that-must-not-come-back";

    [Fact]
    public async Task The_collection_url_and_the_project_round_trip()
    {
        // The trailing slash is trimmed on the way in, as it is for Jira, so nothing builds a URL with
        // a doubled slash later. The escaped space is the case that matters: the measured collection
        // name has a space in it, and the plan names three layers that can un-escape it.
        var saved = await PutAsync(
            new { adoBaseUrl = "https://ado.example.invalid/Some%20Collection/", adoProject = "Saas" });

        Assert.Equal("https://ado.example.invalid/Some%20Collection", saved.AdoBaseUrl);
        Assert.Equal("Saas", saved.AdoProject);

        var stored = await GetAsync();

        Assert.Equal("https://ado.example.invalid/Some%20Collection", stored.AdoBaseUrl);
        Assert.Equal("Saas", stored.AdoProject);
    }

    [Fact]
    public async Task The_waiting_states_round_trip_as_a_list()
    {
        string[] names = ["Blocked", "PO Review"];

        var saved = await PutAsync(new { adoWaitingStates = names, adoIncludeWaiting = true });

        Assert.Equal(names, saved.AdoWaitingStates);
        Assert.True(saved.AdoIncludeWaiting);
        Assert.Equal(names, (await GetAsync()).AdoWaitingStates);
    }

    /// <summary>
    /// Why these lists do not go through SettingList.Write. That writer dedupes case-insensitively,
    /// which is right for people's names and wrong here: a state name is compared ordinally, because
    /// Azure DevOps keeps two states apart that differ only in case, and folding them would silently
    /// drop one of the two from the user's list. Measured - swapping OrdinalNameList for
    /// SettingList.Write in SettingsEndpoints fells exactly this.
    /// </summary>
    [Fact]
    public async Task Two_states_that_differ_only_in_case_stay_two_states()
    {
        string[] names = ["Blocked", "blocked"];

        var saved = await PutAsync(new { adoWaitingStates = names });

        Assert.Equal(names, saved.AdoWaitingStates);
        Assert.Equal(names, (await GetAsync()).AdoWaitingStates);
    }

    /// <summary>
    /// What a fresh database reads as, and the guard on the asymmetric storage: on is a row, off is no
    /// row at all, so the reader has to compare <c>== "true"</c>. Written as <c>!= "false"</c> it reads
    /// an absent row as on, and this is the test that says so.
    /// </summary>
    [Fact]
    public async Task Waiting_work_items_are_excluded_until_asked_for()
    {
        var settings = await GetAsync();

        Assert.False(settings.AdoIncludeWaiting);
        Assert.Empty(settings.AdoWaitingStates);
    }

    /// <summary>
    /// The way back, which needs its own test because the storage is asymmetric: dropping the clearing
    /// branch leaves every other test in this class green.
    /// </summary>
    [Fact]
    public async Task Turning_waiting_back_off_turns_it_off()
    {
        await PutAsync(new { adoWaitingStates = new[] { "Blocked" }, adoIncludeWaiting = true });

        var after = await PutAsync(new { adoIncludeWaiting = false });

        Assert.False(after.AdoIncludeWaiting);
        Assert.Empty(after.AdoWaitingStates);
    }

    [Fact]
    public async Task The_work_item_types_round_trip_as_a_list()
    {
        string[] types = ["Bug", "User Story"];

        var saved = await PutAsync(new { adoWorkItemTypes = types });

        Assert.Equal(types, saved.AdoWorkItemTypes);
        Assert.Equal(types, (await GetAsync()).AdoWorkItemTypes);
    }

    /// <summary>
    /// Decision B's default, and the reason the list is a requirement rather than a filter. A fresh
    /// database has no row, and the absence has to mean these three - not "every type", which would
    /// import the Test Plan and Test Suite the filter exists to keep out.
    ///
    /// This assertion has teeth where the Jira pair's equivalent did not: the generated
    /// SettingsResponse initialises the collection, so a handler that never assigned it would answer
    /// <c>[]</c>, and <c>[]</c> is not three names.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_offers_the_three_default_work_item_types()
    {
        Assert.Equal(AdoDefaults.WorkItemTypes, (await GetAsync()).AdoWorkItemTypes);
    }

    /// <summary>
    /// The empty list is refused rather than read as "every type". Same rule as the Jira project key,
    /// and for the same reason: the absence of a limit is not a neutral default. The storage could not
    /// carry the other reading anyway - an empty list is stored as no row, and no row is what
    /// never-configured looks like.
    /// </summary>
    [Fact]
    public async Task An_empty_list_of_work_item_types_is_refused_rather_than_read_as_every_type()
    {
        await PutAsync(new { adoWorkItemTypes = new[] { "Bug" } });

        var response = await PutRawAsync(new { adoWorkItemTypes = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(ErrorCodes.AdoWorkItemTypesRequired, error!.Code);

        // The half with teeth: a refusal that stored the empty list anyway would answer 400 and still
        // have lost the setting.
        Assert.Equal(["Bug"], (await GetAsync()).AdoWorkItemTypes);
    }

    /// <summary>
    /// A list that is present but has nothing usable in it is an empty list, not an absent one - the
    /// check therefore runs on what survives the blank filter rather than on Count. Two empty rows in
    /// an editor must not quietly become the defaults.
    /// </summary>
    [Fact]
    public async Task A_list_of_only_blank_work_item_types_is_refused_too()
    {
        var response = await PutRawAsync(new { adoWorkItemTypes = new[] { "  ", "" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoWorkItemTypesRequired,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// Absence is not emptiness. PUT /api/settings is a full replacement, so an absent field means
    /// clear - and clearing this one restores the default rather than refusing, because a save that
    /// never mentioned the types must not be a 400.
    /// </summary>
    [Fact]
    public async Task An_absent_list_of_work_item_types_restores_the_default()
    {
        await PutAsync(new { adoWorkItemTypes = new[] { "Bug" } });

        var after = await PutAsync(new { language = "en" });

        Assert.Equal(AdoDefaults.WorkItemTypes, after.AdoWorkItemTypes);
        Assert.Equal(AdoDefaults.WorkItemTypes, (await GetAsync()).AdoWorkItemTypes);
    }

    /// <summary>
    /// A corrupt row reads as the default, not as none. SettingList.Read turns unreadable JSON into an
    /// empty list so the settings page still opens, and for this setting an empty list is not a legal
    /// state: read as empty it would import nothing at all.
    /// </summary>
    [Fact]
    public async Task A_corrupt_list_of_work_item_types_reads_as_the_default()
    {
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoWorkItemTypes, Value = "{not json" });

        var response = await Client.GetAsync("/api/settings");

        response.EnsureSuccessStatusCode();

        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.Equal(AdoDefaults.WorkItemTypes, settings!.AdoWorkItemTypes);
    }

    /// <summary>
    /// Decision A: Azure DevOps has no due date field, so the app proposes one, and the user's answer
    /// is three days. A fresh database has no row, and the absence is where the 3 comes from - the wire
    /// cannot carry it, because an absent int deserialises to 0 and 0 means something else here.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_proposes_a_deadline_three_days_ahead()
    {
        Assert.Equal(AdoDefaults.DeadlineDays, (await GetAsync()).AdoDefaultDeadlineDays);
        Assert.Equal(3, (await GetAsync()).AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// Zero means no deadline, and it is a value rather than an absence, so it has to survive a save.
    /// This is the test the whole default-on-the-contract arrangement exists for: without
    /// <c>default: 3</c> on SettingsRequest.adoDefaultDeadlineDays, an absent field and a deliberate 0
    /// arrive identical, and whichever reading is chosen loses the other one.
    /// </summary>
    [Fact]
    public async Task A_deadline_of_zero_days_is_stored_and_read_back_as_zero()
    {
        Assert.Equal(0, (await PutAsync(new { adoDefaultDeadlineDays = 0 })).AdoDefaultDeadlineDays);
        Assert.Equal(0, (await GetAsync()).AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// The other half of the same arrangement, asserted where it is decided rather than through the
    /// API: the contract's default becomes a property initializer, and System.Text.Json leaves an
    /// initialized property alone when the field is absent from the JSON. Remove the default from the
    /// contract and regenerate, and this reads 0.
    /// </summary>
    [Fact]
    public void An_absent_deadline_days_field_binds_to_the_default_rather_than_zero()
    {
        var request = JsonSerializer.Deserialize<SettingsRequest>("""{"language":"en"}""");

        Assert.Equal(AdoDefaults.DeadlineDays, request!.AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// And the consequence, said out loud: because absence binds to the default, a save that never
    /// mentions the field restores the default instead of keeping a stored 0. That is the same
    /// full-replacement rule the delegates follow, and the real client sends every field.
    /// </summary>
    [Fact]
    public async Task An_absent_deadline_days_field_restores_the_default_like_every_other_field()
    {
        await PutAsync(new { adoDefaultDeadlineDays = 0 });

        Assert.Equal(AdoDefaults.DeadlineDays, (await PutAsync(new { language = "en" })).AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// The default is the absence of a row, and that is not tidiness: two tests in
    /// SettingsEndpointsTests assert about the whole Settings table, and a row left behind by a save
    /// that only touched the language makes them red. Asserted here as well, from the ADO side, so
    /// nothing has to point back at a file about language.
    /// </summary>
    [Fact]
    public async Task Asking_for_the_default_deadline_leaves_no_row_behind()
    {
        await PutAsync(new { adoDefaultDeadlineDays = AdoDefaults.DeadlineDays });

        Assert.Empty(StoredSettings());
    }

    [Fact]
    public async Task A_negative_deadline_is_refused()
    {
        var response = await PutRawAsync(new { adoDefaultDeadlineDays = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoDefaultDeadlineDaysInvalid,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    /// <summary>
    /// 300 is a plausible typo for 3, so the upper bound is not decoration. Its own test rather than a
    /// second case on the negative one, so the two ends can be seen to fail separately: dropping the
    /// upper half of the range check leaves the negative test green.
    /// </summary>
    [Fact]
    public async Task A_deadline_beyond_the_limit_is_refused()
    {
        var response = await PutRawAsync(new { adoDefaultDeadlineDays = AdoDefaults.DeadlineDaysMax + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AdoDefaultDeadlineDaysInvalid,
            (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);

        // The boundary itself is allowed, so the refusal is a range and not an off-by-one.
        Assert.Equal(
            AdoDefaults.DeadlineDaysMax,
            (await PutAsync(new { adoDefaultDeadlineDays = AdoDefaults.DeadlineDaysMax })).AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// The write path refuses a negative number, but a hand-edited row is not on the write path. Read
    /// as written it would give every imported work item a deadline already overdue, so the reader
    /// checks the range as well and falls back to the default, the same way it treats a value that
    /// will not parse at all.
    /// </summary>
    [Theory]
    [InlineData("-5")]
    [InlineData("4000")]
    [InlineData("three")]
    public async Task A_hand_edited_deadline_outside_the_range_reads_as_the_default(string stored)
    {
        await Host.AddAndSaveChangesAsync(
            new Setting { Key = SettingKeys.AdoDefaultDeadlineDays, Value = stored });

        Assert.Equal(AdoDefaults.DeadlineDays, (await GetAsync()).AdoDefaultDeadlineDays);
    }

    /// <summary>
    /// The number lives in three places - AdoDefaults, the contract's <c>default:</c>, and the property
    /// initializer NSwag generates from it - and only the first two can be compared. The third is
    /// compared by An_absent_deadline_days_field_binds_to_the_default_rather_than_zero above, which
    /// reads the generated code by using it. Together they close the loop.
    /// </summary>
    [Fact]
    public void The_contract_declares_the_same_default_the_code_falls_back_to()
    {
        var document = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(File.ReadAllText(RepoPaths.ContractFile));

        var components = (Dictionary<object, object>)document["components"];
        var schemas = (Dictionary<object, object>)components["schemas"];
        var request = (Dictionary<object, object>)schemas["SettingsRequest"];
        var properties = (Dictionary<object, object>)request["properties"];
        var field = (Dictionary<object, object>)properties["adoDefaultDeadlineDays"];

        Assert.True(
            field.ContainsKey("default"),
            "SettingsRequest.adoDefaultDeadlineDays has no default:, so an absent field on the wire "
                + "binds to 0 - which means no deadline rather than the default.");
        Assert.Equal(
            AdoDefaults.DeadlineDays.ToString(),
            field["default"]?.ToString());
    }

    /// <summary>
    /// The whole reason the token has a route of its own, asserted on the raw body: a leak could arrive
    /// under any property name, including one the contract does not declare and the generated client
    /// would drop without a word.
    /// </summary>
    [Fact]
    public async Task The_token_never_comes_back_out_of_the_api()
    {
        var stored = await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = Token });

        stored.EnsureSuccessStatusCode();

        Assert.True(
            (await stored.Content.ReadFromJsonAsync<SettingsResponse>())!.HasAdoToken,
            "The token was not stored, so the leak assertion below would prove nothing.");

        Assert.DoesNotContain(Token, await Client.GetStringAsync("/api/settings"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Storing_a_token_shows_up_as_having_one()
    {
        Assert.False((await GetAsync()).HasAdoToken);

        var response = await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = Token });

        response.EnsureSuccessStatusCode();

        Assert.True((await response.Content.ReadFromJsonAsync<SettingsResponse>())!.HasAdoToken);
    }

    [Fact]
    public async Task Clearing_the_token_removes_it()
    {
        await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = Token });

        var response = await Client.DeleteAsync("/api/settings/ado-token");

        response.EnsureSuccessStatusCode();

        Assert.False((await response.Content.ReadFromJsonAsync<SettingsResponse>())!.HasAdoToken);
    }

    [Fact]
    public async Task An_empty_token_is_rejected_rather_than_stored_as_blank()
    {
        var response = await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Two tokens, two rows. The copy-paste this guards against is a handler that stores under the
    /// other system's key, which would make one token clear or answer for the other.
    /// </summary>
    [Fact]
    public async Task The_two_tokens_are_stored_apart()
    {
        await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = Token });

        var afterAdo = await GetAsync();

        Assert.True(afterAdo.HasAdoToken);
        Assert.False(afterAdo.HasJiraToken);

        await Client.PutAsJsonAsync("/api/settings/jira-token", new { token = "a-jira-secret" });
        await Client.DeleteAsync("/api/settings/ado-token");

        var afterJira = await GetAsync();

        Assert.False(afterJira.HasAdoToken);
        Assert.True(afterJira.HasJiraToken);
    }

    /// <summary>
    /// The regression the split exists to prevent. PUT /api/settings is a full replacement, so a save
    /// of anything else must not be able to reach the token.
    /// </summary>
    [Fact]
    public async Task Saving_the_other_settings_does_not_clear_the_token()
    {
        await Client.PutAsJsonAsync("/api/settings/ado-token", new { token = Token });

        var after = await PutAsync(new { language = "en", adoProject = "Saas" });

        Assert.True(after.HasAdoToken);
        Assert.Equal("Saas", after.AdoProject);
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

    /// <summary>
    /// An anonymous body rather than SettingsRequest, so a test can leave a field out - which is the
    /// very thing the full-replacement semantics turn into behaviour.
    /// </summary>
    private async Task<SettingsResponse> PutAsync(object body)
    {
        var response = await PutRawAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(settings);
        return settings;
    }

    private Task<HttpResponseMessage> PutRawAsync(object body)
        => Client.PutAsJsonAsync("/api/settings", body);
}
