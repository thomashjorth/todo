using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Autostart;
using Todo.Core.Errors;
using Todo.TestSupport;
using Todo.TestSupport.Autostart;

namespace Todo.Api.Tests;

/// <summary>
/// The two autostart routes, measured against a fake registry. Never the real one: the Windows
/// implementation writes under HKCU, so a test that used it would change the machine running the
/// suite and would read whatever the developer had already chosen.
/// </summary>
public class AutostartEndpointsTests
{
    [Fact]
    public async Task Autostart_reads_off_on_a_machine_where_nothing_is_registered()
    {
        var autostart = new RecordingAutostart();

        await using var host = await StartAsync(autostart);

        var settings = await ReadAsync(host);

        Assert.False(settings.Autostart);
    }

    /// <summary>
    /// Read through rather than remembered. The point of the whole design: the registry is what
    /// Windows reads at sign-in, so an answer this app stored could only ever disagree with it.
    /// Arranged as already-on without the API ever being asked to turn it on, which a cached or
    /// stored value could not produce.
    /// </summary>
    [Fact]
    public async Task Autostart_reads_on_when_the_registry_already_says_so()
    {
        var autostart = new RecordingAutostart(enabled: true);

        await using var host = await StartAsync(autostart);

        var settings = await ReadAsync(host);

        Assert.True(settings.Autostart);
        Assert.Empty(autostart.EnabledPaths);
    }

    [Fact]
    public async Task Turning_autostart_on_registers_the_running_program()
    {
        var autostart = new RecordingAutostart();

        await using var host = await StartAsync(autostart);

        var response = await host.Client.PutAsync("/api/settings/autostart", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(settings);
        Assert.True(settings.Autostart);

        // The path, not only the fact. An entry pointing at the wrong file starts nothing at
        // sign-in while the switch still reads on, so this is the half that can be silently wrong.
        var path = Assert.Single(autostart.EnabledPaths);

        Assert.Equal(Environment.ProcessPath, path);
    }

    [Fact]
    public async Task Turning_autostart_off_removes_it()
    {
        var autostart = new RecordingAutostart(enabled: true);

        await using var host = await StartAsync(autostart);

        var response = await host.Client.DeleteAsync("/api/settings/autostart");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(settings);
        Assert.False(settings.Autostart);
        Assert.Equal(1, autostart.DisableCount);
    }

    /// <summary>
    /// A registry a policy has locked down. Read rather than left to become a 500, because this is
    /// one the user can do something about - and the message has to reach the settings page, which
    /// means an error code with a translation rather than an exception page.
    /// </summary>
    [Fact]
    public async Task A_registry_that_refuses_becomes_an_error_the_user_can_read()
    {
        var autostart = new RecordingAutostart
        {
            EnableThrows = new UnauthorizedAccessException("Group policy says no."),
        };

        await using var host = await StartAsync(autostart);

        var response = await host.Client.PutAsync("/api/settings/autostart", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.AutostartFailed, error.Code);
    }

    /// <summary>
    /// Turning it on is not the only way the switch changes shape, and this is the regression that
    /// matters: autostart is absent from SettingsRequest on purpose, so a full replacement of every
    /// other setting must leave it alone. Were it a field on that route, saving a language would
    /// read the absent field as "clear" and switch it off - the trap both tokens have their own
    /// route for.
    /// </summary>
    [Fact]
    public async Task Saving_every_other_setting_leaves_autostart_alone()
    {
        var autostart = new RecordingAutostart(enabled: true);

        await using var host = await StartAsync(autostart);

        var response = await host.Client.PutAsJsonAsync("/api/settings", new SettingsRequest
        {
            Language = "en",
            AdoWorkItemTypes = ["Bug"],
            AdoDefaultDeadlineDays = 3,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

        Assert.NotNull(settings);
        Assert.True(settings.Autostart);
        Assert.Equal(0, autostart.DisableCount);
    }

    private static Task<RunningHost> StartAsync(IAutostart autostart) =>
        RunningHost.StartWithAsync(services =>
        {
            // After the app's own registration, so this one wins - which is the whole reason
            // TodoHost.Build takes the callback.
            services.AddSingleton(autostart);
        });

    private static async Task<SettingsResponse> ReadAsync(RunningHost host)
    {
        var settings = await host.Client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        Assert.NotNull(settings);

        return settings;
    }
}
