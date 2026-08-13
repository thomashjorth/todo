using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }
}
