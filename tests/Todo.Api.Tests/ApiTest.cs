using Microsoft.Extensions.DependencyInjection;
using Todo.TestSupport;

namespace Todo.Api.Tests;

/// <summary>
/// xUnit builds a new instance of the test class for every test method, so a host started here
/// is one host and one fresh database per test - the same isolation each test used to arrange
/// in its own first line, without that line saying anything about the test.
/// </summary>
public abstract class ApiTest : IAsyncLifetime
{
    private RunningHost _host = null!;

    protected RunningHost Host => _host;

    protected HttpClient Client => _host.Client;

    /// <summary>Stands in for a registration, for every test in the class.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

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
