using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.Host;

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

        // Not merely non-empty: 1.0.0.0 is what an assembly answers when nobody set a version, and
        // that is exactly what a published exe reported until slice 16. The number itself is read
        // from the assembly rather than written down twice, so this says "somebody chose one".
        Assert.NotEqual("1.0.0.0", body.Version);
        Assert.Equal(
            typeof(TodoHost).Assembly.GetName().Version?.ToString(),
            body.Version);
    }
}
