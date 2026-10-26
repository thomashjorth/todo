using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using Todo.Core.Errors;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class AuthEndpointsTests : ApiTest
{
    [Fact]
    public async Task The_right_password_sets_a_cookie_that_opens_the_api()
    {
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(Host.BaseUrl),
        };

        var login = await LogInAsync(client, RunningHost.TestPassword);

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    // The three flags are what keep the cookie out of script, off other sites' requests, and - behind
    // https - off plain http. Read off the header, because the cookie container hides them.
    [Fact]
    public async Task The_cookie_is_http_only_strict_and_long_lived()
    {
        using var anonymous = Host.AnonymousClient();

        var login = await LogInAsync(anonymous, RunningHost.TestPassword);

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("todo.login=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("forkert")]
    [InlineData("")]
    public async Task A_wrong_password_is_refused_without_a_cookie(string password)
    {
        using var anonymous = Host.AnonymousClient();

        var login = await LogInAsync(anonymous, password);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(ErrorCodes.AuthWrongPassword, (await login.Content.ReadFromJsonAsync<ApiError>())?.Code);
        Assert.False(login.Headers.Contains("Set-Cookie"));
    }

    // A 401 rather than a 302: a redirect to a login path the server does not have would come back as
    // index.html with a 200, and the frontend could not tell it from data.
    [Fact]
    public async Task The_api_answers_401_without_a_cookie()
    {
        using var anonymous = Host.AnonymousClient();

        var response = await anonymous.GetAsync("/api/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_eleventh_attempt_in_a_minute_is_turned_away()
    {
        using var anonymous = Host.AnonymousClient();

        // Nine, not ten: the limit is per address, and RunningHost has already logged its own client
        // in from the same 127.0.0.1. Measured - a loop of ten met the 429 on its tenth attempt.
        for (var attempt = 2; attempt <= 10; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await LogInAsync(anonymous, "forkert")).StatusCode);
        }

        // The right password, on purpose: the limit is on attempts, not on wrong ones, so even this
        // one waits.
        var eleventh = await LogInAsync(anonymous, RunningHost.TestPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(ErrorCodes.AuthTooManyAttempts, (await eleventh.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    /// <summary>
    /// Every route under /api/ wants the cookie but the two that cannot: health, which the deploy probe
    /// and the helper ask, and the login itself. Walked off the routing table rather than listed, so a
    /// Map* added outside the protected group is caught the day it is written.
    /// </summary>
    [Fact]
    public void Every_api_route_but_health_and_login_requires_the_cookie()
    {
        // And the helper's tunnel, which is not a browser and proves itself with its own key instead -
        // HelperTunnelTests holds it to that, by being refused without one.
        string[] open = ["/api/health", "/api/auth/login", "/api/helper/tunnel"];

        var routes = Host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();

        // A table that came back empty would make the claim below pass on nothing.
        Assert.True(routes.Count > 30, $"Only {routes.Count} /api/ routes were found.");

        var unprotected = routes
            .Where(endpoint => !open.Contains(endpoint.RoutePattern.RawText))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(endpoint => endpoint.DisplayName)
            .ToList();

        Assert.True(unprotected.Count == 0, "Open without a cookie: " + string.Join(", ", unprotected));
    }

    private static Task<HttpResponseMessage> LogInAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Password = password });
}
