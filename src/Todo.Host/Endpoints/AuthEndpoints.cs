using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Todo.Core.Auth;
using Todo.Core.Errors;
using Todo.Host.Auth;

namespace Todo.Host.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async Task<Results<NoContent, BadRequest<ApiError>>> (
            LoginRequest request, LoginPassword stored, HttpContext http) =>
        {
            if (string.IsNullOrEmpty(request.Password) || !PasswordHash.Verify(request.Password, stored.Hash))
            {
                return ApiErrors.BadRequest(ErrorCodes.AuthWrongPassword, "The password is wrong.");
            }

            // One user, so the identity carries nothing to look up: being signed in is the claim.
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "owner")], CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });

            return TypedResults.NoContent();
        })
        .RequireRateLimiting(HostAuth.LoginPolicy)
        .WithName("login")
        .WithTags("Auth");

        return app;
    }
}
