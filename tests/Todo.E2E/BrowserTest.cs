using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

/// <summary>
/// xUnit builds a new instance of the test class for every test method, so the host and its
/// database start fresh here for each journey while the browser stays shared across the class:
/// launching one is the expensive part, and no journey leaves a mark on it.
/// </summary>
public abstract class BrowserTest(BrowserFixture fixture) : IClassFixture<BrowserFixture>, IAsyncLifetime
{
    private RunningHost _host = null!;

    protected RunningHost Host => _host;

    /// <summary>The app under test, once a journey has opened it.</summary>
    protected TodoApp App { get; private set; } = null!;

    /// <summary>Stands in for a registration, for every test in the class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected async Task OpenAppAsync(
        ViewportSize? viewport = null, ColorScheme? colorScheme = null)
        => App = await TodoApp.OpenAsync(fixture.Browser, _host, viewport, colorScheme);

    public async ValueTask InitializeAsync()
        => _host = await RunningHost.StartWithAsync(ConfigureServices);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }
}
