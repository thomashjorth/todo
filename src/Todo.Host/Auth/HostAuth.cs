using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Todo.Tunnel;
using Todo.Core.Errors;

namespace Todo.Host.Auth;

/// <summary>
/// The login's half of the host: the one password, the cookie that remembers it, and the limit on
/// guessing it. Kept out of <see cref="TodoHost.Build"/>, which is long enough already.
/// </summary>
public static class HostAuth
{
    public const string PasswordHashKey = "Auth:PasswordHash";

    public const string KeysPathKey = "Data:KeysPath";

    /// <summary>The rate-limit policy the login route carries.</summary>
    public const string LoginPolicy = "login";

    /// <summary>
    /// The stored hash, or a refusal to start. Refusing is the point: a host that came up without a
    /// password on the NAS would be the whole app, tokens and all, open to everyone on the network. An
    /// empty value counts as missing, so a test can say "none" on the command line - which beats the
    /// user's own Auth__PasswordHash from the environment, where a missing key would not.
    /// </summary>
    public static string RequirePasswordHash(IConfiguration configuration)
    {
        var stored = configuration[PasswordHashKey];

        if (string.IsNullOrWhiteSpace(stored))
        {
            throw new InvalidOperationException(
                "No password is set, so the app will not start. Run SetPassword.cmd, or give the "
                + "host Auth__PasswordHash in its environment - a hash, never the password itself.");
        }

        if (!PasswordHash.IsWellFormed(stored))
        {
            throw new InvalidOperationException(
                "Auth__PasswordHash is not a hash this app made, so the app will not start. Run "
                + "SetPassword.cmd again, or make one with 'Todo.Host hash-password'.");
        }

        return stored;
    }

    /// <summary>
    /// Where the keys that encrypt the cookie live: named, or a folder beside the database. Beside the
    /// database because that is the one place that outlives a deploy - in the container it is the
    /// mounted volume, and keys left in the container's own file system would be new after every
    /// pull, logging the user out each time something was pushed.
    /// </summary>
    public static string KeysPath(IConfiguration configuration, string databasePath) =>
        configuration[KeysPathKey]
        ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "keys");

    public static void AddLogin(this IServiceCollection services, string passwordHash, string keysPath)
    {
        services.AddSingleton(new LoginPassword(passwordHash));

        services.AddDataProtection()
            .SetApplicationName("Todo")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "todo.login";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;

                // The LAN is plain http, so a Secure-only cookie would never come back. Behind https
                // the cookie is marked Secure by the same rule.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // 400 days is as long as a browser keeps a cookie at all; sliding, so a user who
                // opens the app now and then never meets the login page again.
                options.ExpireTimeSpan = TimeSpan.FromDays(400);
                options.SlidingExpiration = true;

                // An API answers 401, never a redirect: the frontend decides where to go, and a 302
                // to a login path the server does not have would arrive as index.html with a 200.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // An ApiError with its own code, because the generated client only reads a body as
            // ApiError for a status the contract declares - and the contract declares this one.
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError
                    {
                        Code = ErrorCodes.AuthTooManyAttempts,
                        Message = "Too many attempts. Wait a minute and try again.",
                    },
                    cancellationToken);
            };
        });
    }
}

/// <summary>The stored hash, handed to the login route through DI.</summary>
public sealed record LoginPassword(string Hash);
