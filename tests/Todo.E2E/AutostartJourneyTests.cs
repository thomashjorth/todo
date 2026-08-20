using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.Core.Autostart;
using Todo.TestSupport.Autostart;

namespace Todo.E2E;

/// <summary>
/// Turning autostart on and off from the settings page.
/// <para>
/// The registry is faked, and that is not a convenience: the real implementation writes under HKCU,
/// so a journey using it would leave an autostart entry on whatever machine ran the suite, and would
/// read back whatever the developer had already chosen for themselves. The same rule that has
/// /api/system/open-link aborted rather than answered - a test must not reach outside the app.
/// </para>
/// <para>
/// What is <em>not</em> faked is /api/settings. The tick's state comes from the real backend reading
/// the fake through, which is the whole design: the switch shows what the machine says, not what the
/// last click intended.
/// </para>
/// </summary>
public class AutostartJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    private readonly RecordingAutostart _autostart = new();

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IAutostart>(_autostart);

    [Fact]
    public async Task Autostart_can_be_turned_on_and_off_and_survives_a_reload()
    {
        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var settings = await App.GoToSettings();

        // The general group, which holds both the language and this - it was called Sprog until
        // slice 16 put a second control in it.
        await settings.OpenAsync(SettingsScreen.LanguageSection);

        await Assertions.Expect(settings.Autostart).Not.ToBeCheckedAsync();

        await settings.Autostart.ClickAsync();

        await Assertions.Expect(settings.Autostart).ToBeCheckedAsync();

        // The path, not only the fact. An entry pointing somewhere else starts nothing at sign-in
        // while the tick still reads on, so this is the half that can be wrong without showing.
        var registered = Assert.Single(_autostart.EnabledPaths);

        Assert.Equal(Environment.ProcessPath, registered);

        // Read back from the server rather than from the tick that was just clicked. The page asks
        // GET /api/settings on arrival, and the answer comes from the fake through the endpoint -
        // so a switch that only looked right in the browser would come back off here.
        await App.Page.ReloadAsync();

        settings = await App.GoToSettings();

        await settings.OpenAsync(SettingsScreen.LanguageSection);
        await Assertions.Expect(settings.Autostart).ToBeCheckedAsync();

        await settings.Autostart.ClickAsync();

        await Assertions.Expect(settings.Autostart).Not.ToBeCheckedAsync();
        Assert.Equal(1, _autostart.DisableCount);
    }

    /// <summary>
    /// A machine whose registry refuses. The message has to land in this group's own line: since the
    /// accordion, a message written to another group's line can be inside a folded section, which
    /// means the user sees nothing at all.
    /// </summary>
    [Fact]
    public async Task A_registry_that_refuses_says_so_beside_the_switch()
    {
        _autostart.EnableThrows = new UnauthorizedAccessException("Group policy says no.");

        await OpenAppAsync(new() { Width = ColumnWidth, Height = 1000 });

        var settings = await App.GoToSettings();

        await settings.OpenAsync(SettingsScreen.LanguageSection);
        await settings.Autostart.ClickAsync();

        await Assertions.Expect(settings.AutostartError).ToBeVisibleAsync();

        // Still off, because the answer decides and not the click.
        await Assertions.Expect(settings.Autostart).Not.ToBeCheckedAsync();

        // The language group's own line stays empty. One signal shown in two places would print
        // this above the language picker as well.
        await Assertions.Expect(settings.SettingsError).Not.ToBeVisibleAsync();
    }
}
