using System.Net;

namespace Todo.Api.Tests;

/// <summary>
/// What answers without a cookie. Written before the login existed and green on both sides of it: the
/// login page has to load before anyone can log in, the documentation shows the API's shape rather
/// than its data, and health is what the deploy probe and the helper ask. Every other test talks
/// through the logged-in client, so without this one nothing would notice the public surface
/// shrinking - or a protected route slipping into it.
/// </summary>
public class PublicSurfaceTests : ApiTest
{
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/i18n/da.json")]
    [InlineData("/scalar/")]
    [InlineData("/openapi/contract.yaml")]
    [InlineData("/api/health")]
    public async Task Answers_without_a_cookie(string path)
    {
        using var anonymous = new HttpClient { BaseAddress = new Uri(Host.BaseUrl) };

        var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
