using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.Api.Tests;

public class HealthEndpointTests : ApiTest
{
    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        var response = await Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }
}
