using Microsoft.Extensions.Configuration;
using Todo.Core.Auth;

namespace Todo.Host.Auth;

/// <summary>
/// The login's half of the host: the one password, the cookie that remembers it, and the limit on
/// guessing it. Kept out of <see cref="TodoHost.Build"/>, which is long enough already.
/// </summary>
public static class HostAuth
{
    public const string PasswordHashKey = "Auth:PasswordHash";

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
}
